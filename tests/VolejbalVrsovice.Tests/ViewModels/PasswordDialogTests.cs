using System.Threading.Tasks;
using VolejbalVrsovice.App.ViewModels;
using VolejbalVrsovice.Core.Services;
using VolejbalVrsovice.Tests.TestSupport;
using Xunit;

namespace VolejbalVrsovice.Tests.ViewModels
{
    public class PasswordDialogTests
    {
        private readonly FakeAuthService _auth = new FakeAuthService();
        private readonly FakeDialogService _dialogs = new FakeDialogService();

        // --- změna hesla ------------------------------------------------------------------------------

        [Fact]
        public async Task Forced_change_does_not_ask_for_the_current_password()
        {
            var viewModel = new ChangePasswordViewModel(_auth, isForced: true)
            {
                NewPassword = "Moje-nove-heslo-1",
                ConfirmPassword = "Moje-nove-heslo-1",
            };
            bool? closedWith = null;
            viewModel.CloseRequested += (sender, args) => closedWith = args.Result;

            await viewModel.SaveCommand.ExecuteAsync(null);

            Assert.Equal(new[] { "ForcedChange:Moje-nove-heslo-1" }, _auth.Calls);
            Assert.True(closedWith);
            Assert.Equal("Odhlásit", viewModel.CancelText);   // zavření vynuceného dialogu = odhlášení
            Assert.Equal(string.Empty, viewModel.NewPassword);
        }

        [Fact]
        public async Task Forced_change_hands_over_the_recovery_code_when_one_was_issued()
        {
            _auth.ForcedChangeResult = PasswordChangeResult.Changed("ABCD-EFGH-JKLM-NPQR-STUV-WXYZ");
            var viewModel = new ChangePasswordViewModel(_auth, isForced: true)
            {
                NewPassword = "Moje-nove-heslo-1",
                ConfirmPassword = "Moje-nove-heslo-1",
            };

            await viewModel.SaveCommand.ExecuteAsync(null);

            Assert.Equal("ABCD-EFGH-JKLM-NPQR-STUV-WXYZ", viewModel.RecoveryCode);
        }

        [Theory]
        [InlineData("", "", "Zadejte heslo.", null)]
        [InlineData("kratke", "kratke", "Heslo musí mít alespoň 8 znaků.", null)]
        [InlineData("admin", "admin", "Heslo nesmí být „admin“.", null)]
        [InlineData("Dobre-heslo-1", "Jine-heslo-22", null, "Hesla se neshodují.")]
        public async Task Invalid_new_password_is_reported_at_the_field_and_nothing_is_sent(
            string newPassword, string confirmPassword, string newPasswordError, string confirmError)
        {
            var viewModel = new ChangePasswordViewModel(_auth, isForced: true) { NewPassword = newPassword, ConfirmPassword = confirmPassword };
            var closed = false;
            viewModel.CloseRequested += (sender, args) => closed = true;

            await viewModel.SaveCommand.ExecuteAsync(null);

            Assert.Equal(newPasswordError, viewModel.GetError(nameof(viewModel.NewPassword)));
            Assert.Equal(confirmError, viewModel.GetError(nameof(viewModel.ConfirmPassword)));
            Assert.Empty(_auth.Calls);
            Assert.False(closed);
        }

        [Fact]
        public async Task Voluntary_change_requires_and_sends_the_current_password()
        {
            var viewModel = new ChangePasswordViewModel(_auth, isForced: false)
            {
                NewPassword = "Moje-nove-heslo-1",
                ConfirmPassword = "Moje-nove-heslo-1",
            };

            await viewModel.SaveCommand.ExecuteAsync(null);
            Assert.Equal("Zadejte současné heslo.", viewModel.GetError(nameof(viewModel.CurrentPassword)));
            Assert.Empty(_auth.Calls);

            viewModel.CurrentPassword = "Stare-heslo-0";
            await viewModel.SaveCommand.ExecuteAsync(null);

            Assert.Equal(new[] { "OwnChange:Stare-heslo-0:Moje-nove-heslo-1" }, _auth.Calls);
            Assert.Equal("Zrušit", viewModel.CancelText);
        }

        [Fact]
        public async Task Refusal_from_the_service_keeps_the_dialog_open_with_the_reason()
        {
            _auth.OwnChangeResult = PasswordChangeResult.Failure("Současné heslo není správné.");
            var viewModel = new ChangePasswordViewModel(_auth, isForced: false)
            {
                CurrentPassword = "spatne",
                NewPassword = "Moje-nove-heslo-1",
                ConfirmPassword = "Moje-nove-heslo-1",
            };
            var closed = false;
            viewModel.CloseRequested += (sender, args) => closed = true;

            await viewModel.SaveCommand.ExecuteAsync(null);

            Assert.False(closed);
            Assert.Equal("Současné heslo není správné.", viewModel.ErrorMessage);
            Assert.False(viewModel.IsBusy);
        }

        /// <summary>
        /// Zavření vynuceného dialogu znamená odhlášení. Kdyby šel zavřít během několikasekundového
        /// ukládání, odhlášení by přišlo uprostřed zabalování klíče novým heslem.
        /// </summary>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Password_dialog_cannot_be_closed_while_the_password_is_being_saved(bool isForced)
        {
            var gate = new TaskCompletionSource<bool>();
            _auth.OperationGate = gate.Task;
            var viewModel = new ChangePasswordViewModel(_auth, isForced)
            {
                CurrentPassword = "Stare-heslo-0",
                NewPassword = "Moje-nove-heslo-1",
                ConfirmPassword = "Moje-nove-heslo-1",
            };
            Assert.True(viewModel.CanClose);

            var saving = viewModel.SaveCommand.ExecuteAsync(null);

            Assert.True(viewModel.IsBusy);
            Assert.False(viewModel.CanClose);                              // křížek ani Alt+F4 teď dialog nezavřou
            Assert.False(viewModel.CancelCommand.CanExecute(null));        // ani tlačítko Zrušit / Odhlásit / Esc
            Assert.False(viewModel.SaveCommand.CanExecute(null));

            gate.SetResult(true);
            await saving;

            Assert.False(viewModel.IsBusy);
            Assert.True(viewModel.CanClose);
        }

        [Fact]
        public void Cancel_closes_the_dialog_unconfirmed()
        {
            var viewModel = new ChangePasswordViewModel(_auth, isForced: true);
            bool? closedWith = null;
            viewModel.CloseRequested += (sender, args) => closedWith = args.Result;

            viewModel.CancelCommand.Execute(null);

            Assert.False(closedWith);
            Assert.Empty(_auth.Calls);
        }

        // --- záchranný kód ----------------------------------------------------------------------------

        [Fact]
        public void Recovery_code_dialog_cannot_be_closed_until_the_user_confirms_saving_the_code()
        {
            var viewModel = new RecoveryCodeViewModel("ABCD-EFGH-JKLM-NPQR-STUV-WXYZ", new FakeClipboard(), new FakePrinter());

            Assert.False(viewModel.CanClose);
            Assert.False(viewModel.CloseCommand.CanExecute(null));

            viewModel.IsConfirmed = true;

            Assert.True(viewModel.CanClose);
            Assert.True(viewModel.CloseCommand.CanExecute(null));
        }

        [Fact]
        public void Recovery_code_dialog_warns_about_irreversible_data_loss()
        {
            var viewModel = new RecoveryCodeViewModel("ABCD-EFGH-JKLM-NPQR-STUV-WXYZ", new FakeClipboard(), new FakePrinter());

            Assert.Equal("Bez hesla i záchranného kódu jsou data nenávratně ztracena.", viewModel.DataLossWarning);
            Assert.Equal("ABCD-EFGH-JKLM-NPQR-STUV-WXYZ", viewModel.Code);
        }

        [Fact]
        public void Recovery_code_can_be_copied_and_printed()
        {
            var clipboard = new FakeClipboard();
            var printer = new FakePrinter();
            var viewModel = new RecoveryCodeViewModel("ABCD-EFGH-JKLM-NPQR-STUV-WXYZ", clipboard, printer);

            viewModel.CopyCommand.Execute(null);
            Assert.Equal("ABCD-EFGH-JKLM-NPQR-STUV-WXYZ", clipboard.Text);
            Assert.Equal("Kód je zkopírován do schránky.", viewModel.StatusMessage);

            viewModel.PrintCommand.Execute(null);
            Assert.Equal(new[] { "ABCD-EFGH-JKLM-NPQR-STUV-WXYZ" }, printer.Printed);

            clipboard.IsAvailable = false;
            viewModel.CopyCommand.Execute(null);
            Assert.StartsWith("Kód se nepodařilo zkopírovat", viewModel.StatusMessage);
        }

        // --- zapomenuté heslo ---------------------------------------------------------------------------

        [Fact]
        public async Task Forgot_password_sends_code_email_and_new_password()
        {
            var viewModel = new ForgotPasswordViewModel(_auth, _dialogs)
            {
                RecoveryCode = "abcd-efgh-jklm-npqr-stuv-wxyz",
                AdminEmail = "admin@admin.cz",
                NewPassword = "Obnovene-heslo-3",
                ConfirmPassword = "Obnovene-heslo-3",
            };
            bool? closedWith = null;
            viewModel.CloseRequested += (sender, args) => closedWith = args.Result;

            await viewModel.SubmitCommand.ExecuteAsync(null);

            Assert.Equal(new[] { "Recover:abcd-efgh-jklm-npqr-stuv-wxyz:admin@admin.cz:Obnovene-heslo-3" }, _auth.Calls);
            Assert.True(closedWith);
            Assert.Equal("Heslo bylo změněno. Teď se můžete přihlásit novým heslem.", Assert.Single(_dialogs.Infos));
            Assert.Equal(string.Empty, viewModel.RecoveryCode);
            Assert.Equal(string.Empty, viewModel.NewPassword);
            Assert.Equal("admin@admin.cz", viewModel.AdminEmail);   // přihlašovací okno si ho převezme
        }

        [Fact]
        public async Task Forgot_password_validates_all_fields_before_calling_the_service()
        {
            var viewModel = new ForgotPasswordViewModel(_auth, _dialogs)
            {
                RecoveryCode = "ABCD-1234",
                AdminEmail = " ",
                NewPassword = "kratke",
                ConfirmPassword = "jine",
            };

            await viewModel.SubmitCommand.ExecuteAsync(null);

            Assert.StartsWith("Kód má 24 znaků", viewModel.GetError(nameof(viewModel.RecoveryCode)));
            Assert.Equal("Zadejte e-mail správce.", viewModel.GetError(nameof(viewModel.AdminEmail)));
            Assert.Equal("Heslo musí mít alespoň 8 znaků.", viewModel.GetError(nameof(viewModel.NewPassword)));
            Assert.Equal("Hesla se neshodují.", viewModel.GetError(nameof(viewModel.ConfirmPassword)));
            Assert.Empty(_auth.Calls);
        }

        [Fact]
        public async Task Forgot_password_shows_why_recovery_was_refused()
        {
            _auth.RecoverResult = OperationResult.Failure("Záchranný kód není platný.");
            var viewModel = new ForgotPasswordViewModel(_auth, _dialogs)
            {
                RecoveryCode = "ABCD-EFGH-JKLM-NPQR-STUV-WXYZ",
                AdminEmail = "admin@admin.cz",
                NewPassword = "Obnovene-heslo-3",
                ConfirmPassword = "Obnovene-heslo-3",
            };
            var closed = false;
            viewModel.CloseRequested += (sender, args) => closed = true;

            await viewModel.SubmitCommand.ExecuteAsync(null);

            Assert.False(closed);
            Assert.Equal("Záchranný kód není platný.", viewModel.ErrorMessage);
            Assert.Empty(_dialogs.Infos);
        }

        [Fact]
        public async Task Forgot_password_dialog_cannot_be_closed_while_the_password_is_being_set()
        {
            var gate = new TaskCompletionSource<bool>();
            _auth.OperationGate = gate.Task;
            var viewModel = new ForgotPasswordViewModel(_auth, _dialogs)
            {
                RecoveryCode = "ABCD-EFGH-JKLM-NPQR-STUV-WXYZ",
                AdminEmail = "admin@admin.cz",
                NewPassword = "Obnovene-heslo-3",
                ConfirmPassword = "Obnovene-heslo-3",
            };

            var submitting = viewModel.SubmitCommand.ExecuteAsync(null);

            Assert.False(viewModel.CanClose);
            Assert.False(viewModel.CancelCommand.CanExecute(null));

            gate.SetResult(true);
            await submitting;

            Assert.True(viewModel.CanClose);
        }

        [Fact]
        public async Task Forgot_password_reports_a_store_mismatch_as_fatal()
        {
            _auth.RecoverError = new VolejbalVrsovice.Core.Errors.DataStoreMismatchException();
            var viewModel = new ForgotPasswordViewModel(_auth, _dialogs)
            {
                RecoveryCode = "ABCD-EFGH-JKLM-NPQR-STUV-WXYZ",
                AdminEmail = "admin@admin.cz",
                NewPassword = "Obnovene-heslo-3",
                ConfirmPassword = "Obnovene-heslo-3",
            };
            bool? closedWith = null;
            viewModel.CloseRequested += (sender, args) => closedWith = args.Result;

            await viewModel.SubmitCommand.ExecuteAsync(null);

            Assert.False(closedWith);
            Assert.Equal("Databáze a soubor klíčů k sobě nepatří nebo jeden chybí – obnovte je ze zálohy.", viewModel.FatalMessage);
            Assert.Empty(_dialogs.Infos);
        }

        [Fact]
        public void Forgot_password_dialog_states_that_data_are_lost_without_password_and_code()
        {
            var viewModel = new ForgotPasswordViewModel(_auth, _dialogs);

            Assert.Contains("Bez hesla i záchranného kódu jsou data nenávratně ztracena.", viewModel.Description);
            Assert.Empty(_auth.Calls);
        }
    }
}
