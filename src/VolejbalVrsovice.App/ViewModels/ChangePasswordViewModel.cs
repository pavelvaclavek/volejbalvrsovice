using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using VolejbalVrsovice.Core.Security;
using VolejbalVrsovice.Core.Services;

namespace VolejbalVrsovice.App.ViewModels
{
    /// <summary>
    /// Změna hesla přihlášeného uživatele. Ve vynuceném režimu (po přihlášení dočasným nebo výchozím
    /// heslem) se neptá na současné heslo a zrušení dialogu znamená odhlášení.
    /// </summary>
    public sealed class ChangePasswordViewModel : DialogViewModel
    {
        private readonly IAuthService _auth;

        private string _currentPassword = string.Empty;
        private string _newPassword = string.Empty;
        private string _confirmPassword = string.Empty;
        private string _errorMessage;
        private bool _isBusy;

        public ChangePasswordViewModel(IAuthService auth, bool isForced)
        {
            _auth = auth ?? throw new ArgumentNullException(nameof(auth));
            IsForced = isForced;

            SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
            CancelCommand = new RelayCommand(() => Close(false), () => !IsBusy);
        }

        public IAsyncRelayCommand SaveCommand { get; }

        public IRelayCommand CancelCommand { get; }

        public bool IsForced { get; }

        public override string Title
        {
            get { return IsForced ? "Nastavení vlastního hesla" : "Změna hesla"; }
        }

        public string Description
        {
            get
            {
                return IsForced
                    ? "Přihlásili jste se dočasným heslem. Než budete pokračovat, nastavte si vlastní heslo – alespoň " + PasswordPolicy.MinLength + " znaků."
                    : "Zadejte současné heslo a dvakrát nové heslo – alespoň " + PasswordPolicy.MinLength + " znaků.";
            }
        }

        public string CancelText
        {
            get { return IsForced ? "Odhlásit" : "Zrušit"; }
        }

        /// <summary>Záchranný kód vydaný při této změně hesla (jen první nastavení hesla správce).</summary>
        public string RecoveryCode { get; private set; }

        public string CurrentPassword
        {
            get { return _currentPassword; }
            set
            {
                if (SetProperty(ref _currentPassword, value ?? string.Empty))
                {
                    SetError(nameof(CurrentPassword), null);
                }
            }
        }

        public string NewPassword
        {
            get { return _newPassword; }
            set
            {
                if (SetProperty(ref _newPassword, value ?? string.Empty))
                {
                    SetError(nameof(NewPassword), null);
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
            private set { SetProperty(ref _errorMessage, value); }
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

        /// <summary>
        /// Během ukládání nejde dialog zavřít. Zavření vynuceného dialogu znamená odhlášení – a to by
        /// uprostřed zabalování klíče novým heslem nechalo účet v nepoužitelném stavu.
        /// </summary>
        public override bool CanClose
        {
            get { return !IsBusy; }
        }

        private async Task SaveAsync()
        {
            ErrorMessage = null;

            SetError(nameof(CurrentPassword), !IsForced && CurrentPassword.Length == 0 ? "Zadejte současné heslo." : null);
            SetError(nameof(NewPassword), PasswordPolicy.Validate(NewPassword));
            SetError(nameof(ConfirmPassword), ConfirmPassword == NewPassword ? null : "Hesla se neshodují.");
            if (HasErrors)
            {
                return;
            }

            PasswordChangeResult result;

            IsBusy = true;
            try
            {
                result = IsForced
                    ? await _auth.CompleteForcedPasswordChangeAsync(NewPassword)
                    : await _auth.ChangeOwnPasswordAsync(CurrentPassword, NewPassword);
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

            RecoveryCode = result.RecoveryCode;
            CurrentPassword = string.Empty;
            NewPassword = string.Empty;
            ConfirmPassword = string.Empty;
            Close(true);
        }
    }
}
