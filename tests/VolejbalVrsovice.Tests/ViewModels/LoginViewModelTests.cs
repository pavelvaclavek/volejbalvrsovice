using System;
using System.Linq;
using System.Threading.Tasks;
using Serilog.Core;
using VolejbalVrsovice.App.ViewModels;
using VolejbalVrsovice.Core.Errors;
using VolejbalVrsovice.Core.Services;
using VolejbalVrsovice.Tests.TestSupport;
using Xunit;

namespace VolejbalVrsovice.Tests.ViewModels
{
    public class LoginViewModelTests
    {
        private readonly FakeAuthService _auth = new FakeAuthService();
        private readonly FakeDialogService _dialogs = new FakeDialogService();

        private async Task<LoginViewModel> CreateReadyAsync()
        {
            var viewModel = new LoginViewModel(_auth, _dialogs, Logger.None);
            await viewModel.InitializeAsync();
            return viewModel;
        }

        [Fact]
        public void Login_form_starts_empty_and_locked_until_the_store_is_ready()
        {
            var viewModel = new LoginViewModel(_auth, _dialogs, Logger.None);

            // Výchozí přihlašovací údaje se na přihlašovací obrazovce nezobrazují.
            Assert.Equal(string.Empty, viewModel.Email);
            Assert.Equal(string.Empty, viewModel.Password);
            Assert.False(viewModel.IsReady);
            Assert.False(viewModel.IsInputEnabled);
            Assert.False(viewModel.LoginCommand.CanExecute(null));
            Assert.False(viewModel.ForgotPasswordCommand.CanExecute(null));
        }

        [Fact]
        public async Task Initialization_prepares_the_store_and_unlocks_the_form()
        {
            var viewModel = await CreateReadyAsync();

            Assert.Contains("Initialize", _auth.Calls);
            Assert.True(viewModel.IsReady);
            Assert.True(viewModel.IsInputEnabled);
            Assert.False(viewModel.IsBusy);
            Assert.Null(viewModel.StatusMessage);
            Assert.True(viewModel.LoginCommand.CanExecute(null));
        }

        [Fact]
        public async Task Mismatched_store_during_initialization_is_fatal()
        {
            _auth.InitializeError = new DataStoreMismatchException();
            var viewModel = new LoginViewModel(_auth, _dialogs, Logger.None);
            var fatal = false;
            viewModel.FatalError += (sender, args) => fatal = true;

            await viewModel.InitializeAsync();

            Assert.True(fatal);
            Assert.False(viewModel.IsReady);
            Assert.Equal(DataStoreMismatchException.UserMessage, Assert.Single(_dialogs.Errors));
        }

        [Fact]
        public async Task Unexpected_failure_during_initialization_is_fatal_without_technical_details()
        {
            _auth.InitializeError = new InvalidOperationException("disk se pokazil: C:\\tajna\\cesta");
            var viewModel = new LoginViewModel(_auth, _dialogs, Logger.None);
            var fatal = false;
            viewModel.FatalError += (sender, args) => fatal = true;

            await viewModel.InitializeAsync();

            Assert.True(fatal);
            Assert.StartsWith("Databázi se nepodařilo připravit.", Assert.Single(_dialogs.Errors));
            Assert.DoesNotContain("tajna", _dialogs.Errors[0]);
        }

        [Fact]
        public async Task Wrong_password_and_unknown_email_show_exactly_the_same_message()
        {
            var viewModel = await CreateReadyAsync();

            // Služba v obou případech vrací InvalidCredentials – okno nesmí přidat nic, co by je rozlišilo.
            viewModel.Email = "admin@admin.cz";
            viewModel.Password = "spatne-heslo";
            await viewModel.LoginCommand.ExecuteAsync(null);
            var wrongPassword = viewModel.ErrorMessage;

            viewModel.Email = "nikdo@example.cz";
            viewModel.Password = "admin";
            await viewModel.LoginCommand.ExecuteAsync(null);
            var unknownEmail = viewModel.ErrorMessage;

            Assert.Equal("Neplatný e-mail nebo heslo.", wrongPassword);
            Assert.Equal(wrongPassword, unknownEmail);
        }

        [Fact]
        public async Task Successful_login_is_announced_and_the_password_is_cleared()
        {
            _auth.LoginResult = LoginResult.Success(TestUsers.Admin());
            var viewModel = await CreateReadyAsync();
            var loggedIn = false;
            viewModel.LoggedIn += (sender, args) => loggedIn = true;

            viewModel.Email = "admin@admin.cz";
            viewModel.Password = "Spravne-heslo-1";
            await viewModel.LoginCommand.ExecuteAsync(null);

            Assert.True(loggedIn);
            Assert.Contains("Login:admin@admin.cz:Spravne-heslo-1", _auth.Calls);
            Assert.Equal(string.Empty, viewModel.Password);
            Assert.Null(viewModel.ErrorMessage);
            Assert.False(viewModel.IsBusy);
        }

        [Fact]
        public async Task Form_stays_locked_after_a_successful_login_until_the_login_is_abandoned()
        {
            _auth.LoginResult = LoginResult.Success(TestUsers.Admin());
            var viewModel = await CreateReadyAsync();
            viewModel.Email = "admin@admin.cz";
            viewModel.Password = "Spravne-heslo-1";

            await viewModel.LoginCommand.ExecuteAsync(null);

            // Mezi přihlášením a otevřením hlavního okna (změna hesla, záchranný kód) nejde přihlášení spustit znovu.
            Assert.False(viewModel.IsInputEnabled);
            Assert.False(viewModel.LoginCommand.CanExecute(null));
            Assert.False(viewModel.ForgotPasswordCommand.CanExecute(null));

            viewModel.NotifyLoginAbandoned("Bez nastavení vlastního hesla nelze pokračovat – byli jste odhlášeni.");

            Assert.True(viewModel.IsInputEnabled);
            Assert.True(viewModel.LoginCommand.CanExecute(null));
        }

        [Fact]
        public async Task Password_is_cleared_after_a_failed_attempt_too()
        {
            var viewModel = await CreateReadyAsync();
            viewModel.Email = "admin@admin.cz";
            viewModel.Password = "spatne-heslo";

            await viewModel.LoginCommand.ExecuteAsync(null);

            Assert.Equal(string.Empty, viewModel.Password);
            Assert.Equal("admin@admin.cz", viewModel.Email);
        }

        [Fact]
        public async Task Empty_fields_are_reported_at_the_fields_without_calling_the_service()
        {
            var viewModel = await CreateReadyAsync();

            await viewModel.LoginCommand.ExecuteAsync(null);

            Assert.True(viewModel.HasErrors);
            Assert.Equal("Zadejte e-mail.", viewModel.GetError(nameof(viewModel.Email)));
            Assert.Equal("Zadejte heslo.", viewModel.GetError(nameof(viewModel.Password)));
            Assert.DoesNotContain(_auth.Calls, call => call.StartsWith("Login"));

            viewModel.Email = "admin@admin.cz";   // oprava pole chybu hned smaže
            Assert.Null(viewModel.GetError(nameof(viewModel.Email)));
        }

        [Theory]
        [InlineData(300, "za 5 min.")]
        [InlineData(241, "za 5 min.")]   // zaokrouhluje se nahoru
        [InlineData(60, "za 1 min.")]
        [InlineData(5, "za 1 min.")]
        public async Task Lockout_message_says_how_long_to_wait(int secondsRemaining, string expectedEnding)
        {
            _auth.LoginResult = LoginResult.LockedOut(TimeSpan.FromSeconds(secondsRemaining));
            var viewModel = await CreateReadyAsync();
            viewModel.Email = "admin@admin.cz";
            viewModel.Password = "cokoli";

            await viewModel.LoginCommand.ExecuteAsync(null);

            Assert.StartsWith("Příliš mnoho neúspěšných pokusů.", viewModel.ErrorMessage);
            Assert.EndsWith(expectedEnding, viewModel.ErrorMessage);
        }

        [Fact]
        public async Task Deactivated_account_gets_its_own_message()
        {
            _auth.LoginResult = LoginResult.AccountInactive();
            var viewModel = await CreateReadyAsync();
            viewModel.Email = "byvaly@example.cz";
            viewModel.Password = "Spravne-heslo-1";

            await viewModel.LoginCommand.ExecuteAsync(null);

            Assert.Equal("Tento účet je deaktivován. Obraťte se na správce.", viewModel.ErrorMessage);
        }

        [Fact]
        public async Task Store_mismatch_found_after_unlocking_shows_the_error_and_ends_the_application()
        {
            _auth.LoginError = new DataStoreMismatchException();
            var viewModel = await CreateReadyAsync();
            var fatal = false;
            viewModel.FatalError += (sender, args) => fatal = true;
            viewModel.Email = "admin@admin.cz";
            viewModel.Password = "admin";

            await viewModel.LoginCommand.ExecuteAsync(null);

            Assert.True(fatal);
            Assert.Equal("Databáze a soubor klíčů k sobě nepatří nebo jeden chybí – obnovte je ze zálohy.", Assert.Single(_dialogs.Errors));
            Assert.Equal(string.Empty, viewModel.Password);
        }

        [Fact]
        public async Task Forgot_password_dialog_gets_the_typed_email_and_returns_the_admin_email()
        {
            var viewModel = await CreateReadyAsync();
            viewModel.Email = "admin@admin.cz";
            _dialogs.OnDialog = dialog =>
            {
                var forgot = Assert.IsType<ForgotPasswordViewModel>(dialog);
                Assert.Equal("admin@admin.cz", forgot.AdminEmail);
                forgot.AdminEmail = "druhy@example.cz";
                return true;
            };

            viewModel.ForgotPasswordCommand.Execute(null);

            Assert.Equal("druhy@example.cz", viewModel.Email);
            Assert.Single(_dialogs.Dialogs.OfType<ForgotPasswordViewModel>());
        }

        [Fact]
        public async Task Store_mismatch_found_during_password_recovery_ends_the_application_too()
        {
            var viewModel = await CreateReadyAsync();
            var fatal = false;
            viewModel.FatalError += (sender, args) => fatal = true;
            _auth.RecoverError = new DataStoreMismatchException();
            _dialogs.OnDialog = dialog =>
            {
                var forgot = Assert.IsType<ForgotPasswordViewModel>(dialog);
                forgot.RecoveryCode = "ABCD-EFGH-JKLM-NPQR-STUV-WXYZ";
                forgot.AdminEmail = "admin@admin.cz";
                forgot.NewPassword = "Obnovene-heslo-3";
                forgot.ConfirmPassword = "Obnovene-heslo-3";

                var confirmed = true;
                forgot.CloseRequested += (sender, args) => confirmed = args.Result;
                forgot.SubmitCommand.Execute(null);   // s falešnou službou doběhne synchronně
                return confirmed;
            };

            viewModel.ForgotPasswordCommand.Execute(null);

            Assert.True(fatal);
            Assert.Equal(DataStoreMismatchException.UserMessage, Assert.Single(_dialogs.Errors));
            Assert.Empty(_dialogs.Infos);
        }

        [Fact]
        public async Task Abandoned_forced_password_change_is_explained()
        {
            var viewModel = await CreateReadyAsync();

            viewModel.NotifyLoginAbandoned("Bez nastavení vlastního hesla nelze pokračovat – byli jste odhlášeni.");

            Assert.Equal("Bez nastavení vlastního hesla nelze pokračovat – byli jste odhlášeni.", viewModel.ErrorMessage);
        }
    }
}
