using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VolejbalVrsovice.App.Services;
using VolejbalVrsovice.Core.Entities;
using VolejbalVrsovice.Core.Services;

namespace VolejbalVrsovice.App.ViewModels
{
    /// <summary>Řádek seznamu uživatelů.</summary>
    public sealed class UserRowViewModel
    {
        public UserRowViewModel(AppUser user)
        {
            User = user ?? throw new ArgumentNullException(nameof(user));
        }

        public AppUser User { get; }

        public string Email
        {
            get { return User.Email; }
        }

        public string DisplayName
        {
            get { return User.DisplayName; }
        }

        public string RoleText
        {
            get { return User.IsAdmin ? "Správce" : "Uživatel"; }
        }

        public bool IsActive
        {
            get { return User.IsActive; }
        }

        public string StateText
        {
            get
            {
                if (!User.IsActive)
                {
                    return "Deaktivovaný";
                }

                return User.MustChangePassword ? "Aktivní – čeká na změnu hesla" : "Aktivní";
            }
        }

        public string LastLoginText
        {
            get
            {
                return User.LastLoginAt.HasValue
                    ? User.LastLoginAt.Value.ToLocalTime().ToString(UiCulture.DateTimePattern, UiCulture.Create())
                    : "–";
            }
        }

        // Název řádku pro čtečky obrazovky (UI Automation bere jméno prvku z ToString).
        public override string ToString()
        {
            return DisplayName + " (" + Email + ")";
        }
    }

    /// <summary>
    /// Správa uživatelů (jen Správce): seznam, přidání, reset hesla, deaktivace a nový záchranný kód.
    /// </summary>
    public sealed class UsersViewModel : ObservableObject, ILoadable, IBusyAware
    {
        private readonly IUserService _users;
        private readonly ISession _session;
        private readonly IDialogService _dialogs;
        private readonly IClipboardService _clipboard;
        private readonly IRecoveryCodePrinter _printer;

        private UserRowViewModel _selectedUser;
        private bool _isBusy;

        public UsersViewModel(
            IUserService users,
            ISession session,
            IDialogService dialogs,
            IClipboardService clipboard,
            IRecoveryCodePrinter printer)
        {
            _users = users ?? throw new ArgumentNullException(nameof(users));
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
            _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
            _printer = printer ?? throw new ArgumentNullException(nameof(printer));

            Users = new ObservableCollection<UserRowViewModel>();

            AddUserCommand = new AsyncRelayCommand(AddUserAsync, () => !IsBusy);
            ResetPasswordCommand = new AsyncRelayCommand(ResetPasswordAsync, CanResetSelectedUser);
            DeactivateCommand = new AsyncRelayCommand(DeactivateAsync, CanChangeSelectedUser);
            NewRecoveryCodeCommand = new AsyncRelayCommand(NewRecoveryCodeAsync, () => !IsBusy);
        }

        /// <summary>Přihlášený správce deaktivoval sám sebe – je potřeba ho odhlásit.</summary>
        public event EventHandler SelfDeactivated;

        public event EventHandler IsBusyChanged;

        public ObservableCollection<UserRowViewModel> Users { get; }

        public IAsyncRelayCommand AddUserCommand { get; }

        public IAsyncRelayCommand ResetPasswordCommand { get; }

        public IAsyncRelayCommand DeactivateCommand { get; }

        public IAsyncRelayCommand NewRecoveryCodeCommand { get; }

        public UserRowViewModel SelectedUser
        {
            get { return _selectedUser; }
            set
            {
                if (SetProperty(ref _selectedUser, value))
                {
                    NotifyCommands();
                }
            }
        }

        /// <summary>Běží operace s klíčem databáze (deaktivace, nový záchranný kód) – po tu dobu se nelze odhlásit.</summary>
        public bool IsBusy
        {
            get { return _isBusy; }
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    NotifyCommands();
                    IsBusyChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public async Task LoadAsync()
        {
            var selectedId = SelectedUser == null ? (int?)null : SelectedUser.User.Id;
            var users = await _users.GetUsersAsync();

            Users.Clear();
            foreach (var user in users)
            {
                Users.Add(new UserRowViewModel(user));
            }

            SelectedUser = Users.FirstOrDefault(row => row.User.Id == selectedId);
        }

        private bool CanChangeSelectedUser()
        {
            return !IsBusy && SelectedUser != null && SelectedUser.IsActive;
        }

        // Vlastní heslo se neresetuje – mění se přes „Změnit heslo“ v hlavičce.
        private bool CanResetSelectedUser()
        {
            return CanChangeSelectedUser() && SelectedUser.User.Id != _session.UserId;
        }

        private async Task AddUserAsync()
        {
            var dialog = new AddUserViewModel(_users);
            if (!_dialogs.ShowDialog(dialog))
            {
                return;
            }

            await LoadAsync();
            SelectedUser = Users.FirstOrDefault(row => row.User.Id == dialog.CreatedUserId);

            _dialogs.ShowInfo(
                "Uživatel byl založen. Předejte mu dočasné heslo – při prvním přihlášení si ho bude muset změnit.");
        }

        private async Task ResetPasswordAsync()
        {
            var row = SelectedUser;
            if (row == null)
            {
                return;
            }

            if (!CanResetSelectedUser() || !_dialogs.ShowDialog(new ResetPasswordViewModel(_users, row.User)))
            {
                return;
            }

            await LoadAsync();
            _dialogs.ShowInfo(
                "Dočasné heslo je nastaveno. Předejte ho uživateli – při příštím přihlášení si ho bude muset změnit.");
        }

        private async Task DeactivateAsync()
        {
            var row = SelectedUser;
            if (row == null)
            {
                return;
            }

            var isSelf = row.User.Id == _session.UserId;
            var question = isSelf
                ? "Opravdu chcete deaktivovat svůj vlastní účet?\n\nBudete odhlášeni a už se nepřihlásíte."
                : "Opravdu chcete deaktivovat uživatele " + row.DisplayName + " (" + row.Email + ")?\n\nUž se nepřihlásí.";

            if (!_dialogs.Confirm(question))
            {
                return;
            }

            OperationResult result;

            IsBusy = true;
            try
            {
                result = await _users.DeactivateAsync(row.User.Id);
            }
            finally
            {
                IsBusy = false;
            }

            if (!result.Succeeded)
            {
                _dialogs.ShowWarning(result.Error);
                return;
            }

            if (isSelf)
            {
                SelfDeactivated?.Invoke(this, EventArgs.Empty);
                return;
            }

            await LoadAsync();
        }

        private async Task NewRecoveryCodeAsync()
        {
            if (!_dialogs.Confirm(
                "Vygenerovat nový záchranný kód?\n\nDosavadní záchranný kód tím přestane platit."))
            {
                return;
            }

            string code;

            IsBusy = true;
            try
            {
                code = await _users.GenerateRecoveryCodeAsync();
            }
            finally
            {
                IsBusy = false;
            }

            _dialogs.ShowDialog(new RecoveryCodeViewModel(code, _clipboard, _printer));
        }

        private void NotifyCommands()
        {
            AddUserCommand.NotifyCanExecuteChanged();
            ResetPasswordCommand.NotifyCanExecuteChanged();
            DeactivateCommand.NotifyCanExecuteChanged();
            NewRecoveryCodeCommand.NotifyCanExecuteChanged();
        }
    }
}
