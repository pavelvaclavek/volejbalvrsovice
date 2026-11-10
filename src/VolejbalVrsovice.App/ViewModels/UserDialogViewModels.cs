using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using VolejbalVrsovice.Core;
using VolejbalVrsovice.Core.Entities;
using VolejbalVrsovice.Core.Security;
using VolejbalVrsovice.Core.Services;

namespace VolejbalVrsovice.App.ViewModels
{
    /// <summary>Volba role v dialogu nového uživatele.</summary>
    public sealed class RoleOption
    {
        public RoleOption(UserRole role, string title)
        {
            Role = role;
            Title = title;
        }

        public UserRole Role { get; }

        public string Title { get; }

        public override string ToString()
        {
            return Title;
        }
    }

    /// <summary>Společný základ dialogů, ve kterých správce zadává dočasné heslo.</summary>
    public abstract class TemporaryPasswordDialogViewModel : DialogViewModel
    {
        private string _temporaryPassword = string.Empty;
        private string _confirmPassword = string.Empty;
        private string _errorMessage;
        private bool _isBusy;

        protected TemporaryPasswordDialogViewModel()
        {
            SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
            CancelCommand = new RelayCommand(() => Close(false), () => !IsBusy);
        }

        public IAsyncRelayCommand SaveCommand { get; }

        public IRelayCommand CancelCommand { get; }

        public string PasswordHint
        {
            get { return "Dočasné heslo (alespoň " + PasswordPolicy.MinLength + " znaků) předejte uživateli. Při přihlášení si ho bude muset změnit."; }
        }

        public string TemporaryPassword
        {
            get { return _temporaryPassword; }
            set
            {
                if (SetProperty(ref _temporaryPassword, value ?? string.Empty))
                {
                    SetError(nameof(TemporaryPassword), null);
                }
            }
        }

        public string ConfirmPassword
        {
            get { return _confirmPassword; }
            set
            {
                if (SetProperty(ref _confirmPassword, value ?? string.Empty))
                {
                    SetError(nameof(ConfirmPassword), null);
                }
            }
        }

        public string ErrorMessage
        {
            get { return _errorMessage; }
            protected set { SetProperty(ref _errorMessage, value); }
        }

        public bool IsBusy
        {
            get { return _isBusy; }
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    OnPropertyChanged(nameof(IsInputEnabled));
                    SaveCommand.NotifyCanExecuteChanged();
                    CancelCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public bool IsInputEnabled
        {
            get { return !IsBusy; }
        }

        /// <summary>Během ukládání nejde dialog zavřít – operace by doběhla, ale správce by si myslel, že ji zrušil.</summary>
        public override bool CanClose
        {
            get { return !IsBusy; }
        }

        /// <summary>Zvaliduje ostatní pole dialogu (hesla validuje základ).</summary>
        protected abstract void ValidateFields();

        /// <summary>Provede operaci; vrací výsledek služby.</summary>
        protected abstract Task<OperationResult> ExecuteAsync();

        private async Task SaveAsync()
        {
            ErrorMessage = null;

            ValidateFields();
            SetError(nameof(TemporaryPassword), PasswordPolicy.Validate(TemporaryPassword));
            SetError(nameof(ConfirmPassword), ConfirmPassword == TemporaryPassword ? null : "Hesla se neshodují.");
            if (HasErrors)
            {
                return;
            }

            OperationResult result;

            IsBusy = true;
            try
            {
                result = await ExecuteAsync();
            }
            finally
            {
                IsBusy = false;
            }

            if (!result.Succeeded)
            {
                ErrorMessage = result.Error;
                return;
            }

            TemporaryPassword = string.Empty;
            ConfirmPassword = string.Empty;
            Close(true);
        }
    }

    /// <summary>Nový uživatel: e-mail, jméno, role a dočasné heslo.</summary>
    public sealed class AddUserViewModel : TemporaryPasswordDialogViewModel
    {
        private readonly IUserService _users;

        private string _email = string.Empty;
        private string _displayName = string.Empty;
        private RoleOption _selectedRole;

        public AddUserViewModel(IUserService users)
        {
            _users = users ?? throw new ArgumentNullException(nameof(users));

            Roles = new[]
            {
                new RoleOption(UserRole.User, "Uživatel – vše kromě správy uživatelů"),
                new RoleOption(UserRole.Admin, "Správce – vše včetně správy uživatelů"),
            };
            _selectedRole = Roles[0];
        }

        public override string Title
        {
            get { return "Nový uživatel"; }
        }

        public IReadOnlyList<RoleOption> Roles { get; }

        /// <summary>Id založeného uživatele (po úspěšném uložení).</summary>
        public int? CreatedUserId { get; private set; }

        public string Email
        {
            get { return _email; }
            set
            {
                if (SetProperty(ref _email, value ?? string.Empty))
                {
                    SetError(nameof(Email), null);
                }
            }
        }

        public string DisplayName
        {
            get { return _displayName; }
            set
            {
                if (SetProperty(ref _displayName, value ?? string.Empty))
                {
                    SetError(nameof(DisplayName), null);
                }
            }
        }

        public RoleOption SelectedRole
        {
            get { return _selectedRole; }
            set { SetProperty(ref _selectedRole, value); }
        }

        protected override void ValidateFields()
        {
            SetError(nameof(Email), EmailAddress.IsValid(Email) ? null : "Zadejte platný e-mail.");
            SetError(nameof(DisplayName), string.IsNullOrWhiteSpace(DisplayName) ? "Zadejte jméno uživatele." : null);
        }

        protected override async Task<OperationResult> ExecuteAsync()
        {
            var role = SelectedRole == null ? UserRole.User : SelectedRole.Role;
            var result = await _users.CreateUserAsync(Email, DisplayName, role, TemporaryPassword);
            if (result.Succeeded)
            {
                CreatedUserId = result.User.Id;
            }

            return result;
        }
    }

    /// <summary>Reset hesla: správce nastaví uživateli dočasné heslo.</summary>
    public sealed class ResetPasswordViewModel : TemporaryPasswordDialogViewModel
    {
        private readonly IUserService _users;
        private readonly AppUser _user;

        public ResetPasswordViewModel(IUserService users, AppUser user)
        {
            _users = users ?? throw new ArgumentNullException(nameof(users));
            _user = user ?? throw new ArgumentNullException(nameof(user));
        }

        public override string Title
        {
            get { return "Reset hesla"; }
        }

        public string UserDescription
        {
            get { return _user.DisplayName + " (" + _user.Email + ")"; }
        }

        protected override void ValidateFields()
        {
        }

        protected override Task<OperationResult> ExecuteAsync()
        {
            return _users.ResetPasswordAsync(_user.Id, TemporaryPassword);
        }
    }
}
