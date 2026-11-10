using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using VolejbalVrsovice.App.Services;
using VolejbalVrsovice.Core.Errors;
using VolejbalVrsovice.Core.Security;
using VolejbalVrsovice.Core.Services;
using RecoveryCodeFormat = VolejbalVrsovice.Core.Security.RecoveryCode;

namespace VolejbalVrsovice.App.ViewModels
{
    /// <summary>
    /// „Zapomenuté heslo“: záchranný kód + e-mail správce + nové heslo. Kódem se odemkne klíč databáze
    /// a znovu se zabalí novým heslem toho správce.
    /// </summary>
    public sealed class ForgotPasswordViewModel : DialogViewModel
    {
        private readonly IAuthService _auth;
        private readonly IDialogService _dialogs;

        private string _recoveryCode = string.Empty;
        private string _adminEmail = string.Empty;
        private string _newPassword = string.Empty;
        private string _confirmPassword = string.Empty;
        private string _errorMessage;
        private bool _isBusy;

        public ForgotPasswordViewModel(IAuthService auth, IDialogService dialogs)
        {
            _auth = auth ?? throw new ArgumentNullException(nameof(auth));
            _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));

            SubmitCommand = new AsyncRelayCommand(SubmitAsync, () => !IsBusy);
            CancelCommand = new RelayCommand(() => Close(false), () => !IsBusy);
        }

        public IAsyncRelayCommand SubmitCommand { get; }

        public IRelayCommand CancelCommand { get; }

        public override string Title
        {
            get { return "Zapomenuté heslo"; }
        }

        public string Description
        {
            get
            {
                return "Zadejte záchranný kód, který správce dostal při prvním nastavení hesla, e-mail správce "
                       + "a nové heslo. " + RecoveryCodeTexts.DataLossWarning;
            }
        }

        public string RecoveryCode
        {
            get { return _recoveryCode; }
            set
            {
                if (SetProperty(ref _recoveryCode, value ?? string.Empty))
                {
                    SetError(nameof(RecoveryCode), null);
                }
            }
        }

        public string AdminEmail
        {
            get { return _adminEmail; }
            set
            {
                if (SetProperty(ref _adminEmail, value ?? string.Empty))
                {
                    SetError(nameof(AdminEmail), null);
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
                    SubmitCommand.NotifyCanExecuteChanged();
                    CancelCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public bool IsInputEnabled
        {
            get { return !IsBusy; }
        }

        /// <summary>Během nastavování hesla nejde dialog zavřít – operace by doběhla bez vědomí uživatele.</summary>
        public override bool CanClose
        {
            get { return !IsBusy; }
        }

        /// <summary>
        /// Hláška chyby úložiště (databáze a soubor klíčů k sobě nepatří apod.), po které se aplikace
        /// má ukončit; jinak <c>null</c>.
        /// </summary>
        public string FatalMessage { get; private set; }

        private async Task SubmitAsync()
        {
            ErrorMessage = null;

            SetError(nameof(RecoveryCode), RecoveryCodeFormat.Normalize(RecoveryCode) == null
                ? "Kód má 24 znaků ve skupinách po čtyřech (XXXX-XXXX-XXXX-XXXX-XXXX-XXXX)."
                : null);
            SetError(nameof(AdminEmail), string.IsNullOrWhiteSpace(AdminEmail) ? "Zadejte e-mail správce." : null);
            SetError(nameof(NewPassword), PasswordPolicy.Validate(NewPassword));
            SetError(nameof(ConfirmPassword), ConfirmPassword == NewPassword ? null : "Hesla se neshodují.");
            if (HasErrors)
            {
                return;
            }

            OperationResult result = null;

            IsBusy = true;
            try
            {
                result = await _auth.RecoverAsync(RecoveryCode, AdminEmail, NewPassword);
            }
            catch (DataStoreException ex)
            {
                // Stejně jako při přihlášení: nic nevytvářet ani nepřepisovat, ukázat hlášku a skončit.
                FatalMessage = ex.Message;
            }
            finally
            {
                IsBusy = false;
            }

            if (FatalMessage != null)
            {
                Close(false);
                return;
            }

            if (!result.Succeeded)
            {
                ErrorMessage = result.Error;
                return;
            }

            RecoveryCode = string.Empty;
            NewPassword = string.Empty;
            ConfirmPassword = string.Empty;

            _dialogs.ShowInfo("Heslo bylo změněno. Teď se můžete přihlásit novým heslem.");
            Close(true);
        }
    }
}
