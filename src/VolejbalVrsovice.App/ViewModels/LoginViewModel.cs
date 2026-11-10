using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using VolejbalVrsovice.App.Services;
using VolejbalVrsovice.Core.Errors;
using VolejbalVrsovice.Core.Services;

namespace VolejbalVrsovice.App.ViewModels
{
    /// <summary>
    /// Přihlašovací okno: e-mail + heslo. Bez úspěšného přihlášení není dostupné nic jiného.
    /// Výchozí přihlašovací údaje se tu záměrně nikde nezobrazují (jsou jen v návodu).
    /// </summary>
    public sealed class LoginViewModel : ValidatingViewModel
    {
        /// <summary>Stejný text pro neznámý e-mail i špatné heslo.</summary>
        public const string InvalidCredentialsMessage = "Neplatný e-mail nebo heslo.";

        public const string AccountInactiveMessage = "Tento účet je deaktivován. Obraťte se na správce.";

        private static readonly TimeSpan StatusDelay = TimeSpan.FromMilliseconds(200);

        private readonly IAuthService _auth;
        private readonly IDialogService _dialogs;
        private readonly ILogger _logger;

        private string _email = string.Empty;
        private string _password = string.Empty;
        private string _errorMessage;
        private string _statusMessage;
        private bool _isBusy;
        private bool _isReady;
        private bool _isSignedIn;

        public LoginViewModel(IAuthService auth, IDialogService dialogs, ILogger logger)
        {
            _auth = auth ?? throw new ArgumentNullException(nameof(auth));
            _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            LoginCommand = new AsyncRelayCommand(LoginAsync, CanInteract);
            ForgotPasswordCommand = new RelayCommand(ForgotPassword, CanInteract);
        }

        /// <summary>Uživatel se úspěšně přihlásil (relace je zahájená).</summary>
        public event EventHandler LoggedIn;

        /// <summary>Úložiště dat je v nepoužitelném stavu – aplikace se má ukončit.</summary>
        public event EventHandler FatalError;

        public IAsyncRelayCommand LoginCommand { get; }

        public IRelayCommand ForgotPasswordCommand { get; }

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

        public string Password
        {
            get { return _password; }
            set
            {
                if (SetProperty(ref _password, value ?? string.Empty))
                {
                    SetError(nameof(Password), null);
                }
            }
        }

        /// <summary>Chyba přihlášení zobrazená pod formulářem.</summary>
        public string ErrorMessage
        {
            get { return _errorMessage; }
            private set { SetProperty(ref _errorMessage, value); }
        }

        /// <summary>Co aplikace právě dělá (příprava databáze, ověřování hesla).</summary>
        public string StatusMessage
        {
            get { return _statusMessage; }
            private set { SetProperty(ref _statusMessage, value); }
        }

        public bool IsBusy
        {
            get { return _isBusy; }
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    OnPropertyChanged(nameof(IsInputEnabled));
                    NotifyCommands();
                }
            }
        }

        /// <summary>Úložiště je připravené (při prvním spuštění až po vytvoření databáze).</summary>
        public bool IsReady
        {
            get { return _isReady; }
            private set
            {
                if (SetProperty(ref _isReady, value))
                {
                    OnPropertyChanged(nameof(IsInputEnabled));
                    NotifyCommands();
                }
            }
        }

        public bool IsInputEnabled
        {
            get { return IsReady && !IsBusy && !_isSignedIn; }
        }

        /// <summary>
        /// Připraví úložiště dat. Při prvním spuštění vytvoří databázi a výchozího správce – trvá to
        /// několik sekund, proto to běží až po zobrazení okna.
        /// </summary>
        public async Task InitializeAsync()
        {
            IsBusy = true;
            try
            {
                var initialization = _auth.InitializeAsync();

                // Hlášku ukázat jen tehdy, když příprava opravdu trvá (první spuštění).
                if (await Task.WhenAny(initialization, Task.Delay(StatusDelay)) != initialization)
                {
                    StatusMessage = "První spuštění – připravuji databázi…";
                }

                await initialization;
                IsReady = true;
            }
            catch (DataStoreException ex)
            {
                Fail(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.Fatal(ex, "Příprava úložiště dat selhala");
                Fail("Databázi se nepodařilo připravit.\n\nPodrobnosti jsou zapsány v protokolu ve složce Data\\logs.");
            }
            finally
            {
                StatusMessage = null;
                IsBusy = false;
            }
        }

        /// <summary>Přihlášení nebylo dokončeno (uživatel zavřel vynucenou změnu hesla) – relace už je ukončená.</summary>
        public void NotifyLoginAbandoned(string message)
        {
            SetSignedIn(false);
            ErrorMessage = message;
        }

        // Po úspěšném přihlášení zůstává formulář zamčený, dokud se neotevře hlavní okno
        // (mezitím může běžet vynucená změna hesla nebo vydání záchranného kódu).
        private void SetSignedIn(bool value)
        {
            _isSignedIn = value;
            OnPropertyChanged(nameof(IsInputEnabled));
            NotifyCommands();
        }

        private bool CanInteract()
        {
            return IsInputEnabled;
        }

        private async Task LoginAsync()
        {
            ErrorMessage = null;
            SetError(nameof(Email), string.IsNullOrWhiteSpace(Email) ? "Zadejte e-mail." : null);
            SetError(nameof(Password), string.IsNullOrEmpty(Password) ? "Zadejte heslo." : null);
            if (HasErrors)
            {
                return;
            }

            LoginResult result;

            IsBusy = true;
            StatusMessage = "Ověřuji přihlášení…";
            try
            {
                result = await _auth.LoginAsync(Email, Password);
            }
            catch (DataStoreException ex)
            {
                Fail(ex.Message);
                return;
            }
            finally
            {
                // Heslo nedržet v paměti déle, než je nutné.
                Password = string.Empty;
                SetError(nameof(Password), null);
                StatusMessage = null;
                IsBusy = false;
            }

            switch (result.Status)
            {
                case LoginStatus.Success:
                    SetSignedIn(true);
                    LoggedIn?.Invoke(this, EventArgs.Empty);
                    break;

                case LoginStatus.AccountInactive:
                    ErrorMessage = AccountInactiveMessage;
                    break;

                case LoginStatus.LockedOut:
                    ErrorMessage = FormatLockout(result.LockoutRemaining ?? TimeSpan.Zero);
                    break;

                default:
                    ErrorMessage = InvalidCredentialsMessage;
                    break;
            }
        }

        private void ForgotPassword()
        {
            var viewModel = new ForgotPasswordViewModel(_auth, _dialogs) { AdminEmail = Email };
            if (_dialogs.ShowDialog(viewModel))
            {
                Email = viewModel.AdminEmail;
                ErrorMessage = null;
            }
            else if (viewModel.FatalMessage != null)
            {
                Fail(viewModel.FatalMessage);
            }
        }

        private void Fail(string message)
        {
            _dialogs.ShowError(message);
            FatalError?.Invoke(this, EventArgs.Empty);
        }

        private void NotifyCommands()
        {
            LoginCommand.NotifyCanExecuteChanged();
            ForgotPasswordCommand.NotifyCanExecuteChanged();
        }

        public static string FormatLockout(TimeSpan remaining)
        {
            var minutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
            return "Příliš mnoho neúspěšných pokusů. Přihlášení je zablokováno, zkuste to znovu za " + minutes + " min.";
        }
    }
}
