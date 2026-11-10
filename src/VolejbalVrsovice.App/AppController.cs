using System;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using VolejbalVrsovice.App.Bootstrap;
using VolejbalVrsovice.App.Services;
using VolejbalVrsovice.App.ViewModels;
using VolejbalVrsovice.App.Views;
using VolejbalVrsovice.Core.Services;

namespace VolejbalVrsovice.App
{
    /// <summary>
    /// Řídí, které okno je vidět: přihlášení → (vynucená změna hesla → záchranný kód) → hlavní okno,
    /// a po odhlášení zpět na přihlášení. Bez úspěšného přihlášení se nezobrazí nic jiného.
    /// </summary>
    public sealed class AppController
    {
        private readonly IServiceProvider _services;
        private readonly IAuthService _auth;
        private readonly ISession _session;
        private readonly IDialogService _dialogs;
        private readonly IClipboardService _clipboard;
        private readonly IRecoveryCodePrinter _printer;

        private Window _currentWindow;

        public AppController(
            IServiceProvider services,
            IAuthService auth,
            ISession session,
            IDialogService dialogs,
            IClipboardService clipboard,
            IRecoveryCodePrinter printer)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _auth = auth ?? throw new ArgumentNullException(nameof(auth));
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
            _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
            _printer = printer ?? throw new ArgumentNullException(nameof(printer));
        }

        public void Start()
        {
            ShowLogin();
        }

        private async void ShowLogin()
        {
            var viewModel = _services.GetRequiredService<LoginViewModel>();
            var window = new LoginWindow { DataContext = viewModel };

            viewModel.LoggedIn += (sender, args) => OnLoggedIn(viewModel);
            viewModel.FatalError += (sender, args) => Exit(ExitCodes.DataStoreInconsistent);

            SwitchTo(window);

            // Při prvním spuštění se teprve teď (už za zobrazeným oknem) vytváří databáze.
            await viewModel.InitializeAsync();
        }

        // async void: obsluha události; případná chyba doputuje do globálního handleru výjimek.
        private async void OnLoggedIn(LoginViewModel login)
        {
            try
            {
                await CompleteLoginAsync(login);
            }
            catch (Exception)
            {
                // Nedokončené přihlášení nesmí nechat otevřenou relaci za zamčeným formulářem.
                _auth.Logout();
                login.NotifyLoginAbandoned("Přihlášení se nepodařilo dokončit.");
                throw;
            }
        }

        private async Task CompleteLoginAsync(LoginViewModel login)
        {
            var user = _session.User;
            if (user == null)
            {
                return;
            }

            string recoveryCode = null;

            if (user.MustChangePassword)
            {
                // Vynucená změna hesla je nepřeskočitelná: zavření dialogu = odhlášení.
                var change = new ChangePasswordViewModel(_auth, isForced: true);
                if (!_dialogs.ShowDialog(change))
                {
                    _auth.Logout();
                    login.NotifyLoginAbandoned("Bez nastavení vlastního hesla nelze pokračovat – byli jste odhlášeni.");
                    return;
                }

                recoveryCode = change.RecoveryCode;
            }

            if (recoveryCode == null)
            {
                // Pojistka: správce bez záchranného kódu (vydání při první změně hesla nedoběhlo) ho dostane teď.
                recoveryCode = await _auth.IssueMissingRecoveryCodeAsync();
            }

            if (recoveryCode != null)
            {
                // Záchranný kód se zobrazí jen jednou, hned po prvním nastavení hesla správce.
                _dialogs.ShowDialog(new RecoveryCodeViewModel(recoveryCode, _clipboard, _printer));
            }

            ShowMain();
        }

        private void ShowMain()
        {
            var viewModel = _services.GetRequiredService<MainViewModel>();
            var window = new MainWindow { DataContext = viewModel };

            viewModel.LogoutRequested += (sender, args) =>
            {
                _auth.Logout();
                ShowLogin();
            };

            SwitchTo(window);
        }

        /// <summary>Zobrazí nové okno a předchozí zavře, aniž by se tím ukončila aplikace.</summary>
        private void SwitchTo(Window next)
        {
            var previous = _currentWindow;

            _currentWindow = next;
            Application.Current.MainWindow = next;
            next.Closed += OnWindowClosed;
            next.Show();

            if (previous != null)
            {
                previous.Closed -= OnWindowClosed;
                previous.Close();
            }
        }

        // Uživatel zavřel aktuální okno křížkem → konec aplikace.
        private void OnWindowClosed(object sender, EventArgs e)
        {
            if (ReferenceEquals(sender, _currentWindow))
            {
                Exit(ExitCodes.Ok);
            }
        }

        private void Exit(int exitCode)
        {
            _auth.Logout();   // přepíše klíč databáze v paměti nulami
            Application.Current.Shutdown(exitCode);
        }
    }
}
