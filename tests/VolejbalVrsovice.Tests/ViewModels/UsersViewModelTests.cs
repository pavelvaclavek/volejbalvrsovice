using System;
using System.Linq;
using System.Threading.Tasks;
using VolejbalVrsovice.App.ViewModels;
using VolejbalVrsovice.Core.Entities;
using VolejbalVrsovice.Core.Services;
using VolejbalVrsovice.Tests.TestSupport;
using Xunit;

namespace VolejbalVrsovice.Tests.ViewModels
{
    public class UsersViewModelTests
    {
        private readonly FakeUserService _users = new FakeUserService();
        private readonly FakeSession _session = new FakeSession { User = TestUsers.Admin() };
        private readonly FakeDialogService _dialogs = new FakeDialogService();
        private readonly FakeClipboard _clipboard = new FakeClipboard();
        private readonly FakePrinter _printer = new FakePrinter();

        public UsersViewModelTests()
        {
            _users.Users.Add(TestUsers.Admin());
            _users.Users.Add(TestUsers.Ordinary());
        }

        private async Task<UsersViewModel> CreateLoadedAsync()
        {
            var viewModel = new UsersViewModel(_users, _session, _dialogs, _clipboard, _printer);
            await viewModel.LoadAsync();
            return viewModel;
        }

        [Fact]
        public async Task List_shows_role_state_and_last_login_in_czech()
        {
            _users.Users[0].LastLoginAt = new DateTime(2026, 3, 5, 9, 30, 0, DateTimeKind.Utc);
            _users.Users[1].MustChangePassword = true;
            _users.Users.Add(new AppUser { Id = 3, Email = "byvaly@example.cz", DisplayName = "Bývalý", Role = UserRole.User, IsActive = false });

            var viewModel = await CreateLoadedAsync();

            Assert.Equal(new[] { "Správce", "Uživatel", "Uživatel" }, viewModel.Users.Select(row => row.RoleText));
            Assert.Equal(new[] { "Aktivní", "Aktivní – čeká na změnu hesla", "Deaktivovaný" }, viewModel.Users.Select(row => row.StateText));
            Assert.Equal(new DateTime(2026, 3, 5, 9, 30, 0, DateTimeKind.Utc).ToLocalTime().ToString("d. M. yyyy H:mm"), viewModel.Users[0].LastLoginText);
            Assert.Equal("–", viewModel.Users[1].LastLoginText);
            Assert.Equal("Petr Svoboda (trener@example.cz)", viewModel.Users[1].ToString());
        }

        [Fact]
        public async Task Reset_and_deactivation_need_a_selected_active_user()
        {
            _users.Users.Add(new AppUser { Id = 3, Email = "byvaly@example.cz", DisplayName = "Bývalý", Role = UserRole.User, IsActive = false });
            var viewModel = await CreateLoadedAsync();

            Assert.False(viewModel.ResetPasswordCommand.CanExecute(null));
            Assert.False(viewModel.DeactivateCommand.CanExecute(null));
            Assert.True(viewModel.AddUserCommand.CanExecute(null));

            viewModel.SelectedUser = viewModel.Users[1];
            Assert.True(viewModel.ResetPasswordCommand.CanExecute(null));
            Assert.True(viewModel.DeactivateCommand.CanExecute(null));

            viewModel.SelectedUser = viewModel.Users[2];   // deaktivovaný
            Assert.False(viewModel.ResetPasswordCommand.CanExecute(null));
            Assert.False(viewModel.DeactivateCommand.CanExecute(null));
        }

        [Fact]
        public async Task Own_password_cannot_be_reset_from_the_user_list()
        {
            var viewModel = await CreateLoadedAsync();

            viewModel.SelectedUser = viewModel.Users[0];   // přihlášený správce

            // Vlastní heslo se mění přes „Změnit heslo“ v hlavičce, ne resetem.
            Assert.False(viewModel.ResetPasswordCommand.CanExecute(null));
            Assert.True(viewModel.DeactivateCommand.CanExecute(null));

            await viewModel.ResetPasswordCommand.ExecuteAsync(null);
            Assert.Empty(_dialogs.Dialogs);
            Assert.Empty(_users.Calls);
        }

        [Fact]
        public async Task Page_reports_itself_busy_while_a_new_recovery_code_is_being_generated()
        {
            var gate = new TaskCompletionSource<bool>();
            _users.OperationGate = gate.Task;
            var viewModel = await CreateLoadedAsync();
            var busyChanges = 0;
            viewModel.IsBusyChanged += (sender, args) => busyChanges++;

            var generating = viewModel.NewRecoveryCodeCommand.ExecuteAsync(null);

            Assert.True(viewModel.IsBusy);
            Assert.False(viewModel.AddUserCommand.CanExecute(null));
            Assert.Equal(1, busyChanges);

            gate.SetResult(true);
            await generating;

            Assert.False(viewModel.IsBusy);
            Assert.Equal(2, busyChanges);
            Assert.IsType<RecoveryCodeViewModel>(Assert.Single(_dialogs.Dialogs));
        }

        [Fact]
        public async Task Deactivation_asks_first_and_does_nothing_when_declined()
        {
            var viewModel = await CreateLoadedAsync();
            viewModel.SelectedUser = viewModel.Users[1];
            _dialogs.ConfirmAnswer = false;

            await viewModel.DeactivateCommand.ExecuteAsync(null);

            Assert.Contains("Petr Svoboda (trener@example.cz)", Assert.Single(_dialogs.Questions));
            Assert.Empty(_users.Calls);
            Assert.True(viewModel.Users[1].IsActive);
        }

        [Fact]
        public async Task Confirmed_deactivation_updates_the_list()
        {
            var viewModel = await CreateLoadedAsync();
            viewModel.SelectedUser = viewModel.Users[1];

            await viewModel.DeactivateCommand.ExecuteAsync(null);

            Assert.Equal(new[] { "Deactivate:2" }, _users.Calls);
            Assert.Equal("Deaktivovaný", viewModel.Users[1].StateText);
            Assert.Equal(2, viewModel.SelectedUser.User.Id);              // výběr zůstal na stejném uživateli
            Assert.False(viewModel.DeactivateCommand.CanExecute(null));   // podruhé už to nejde
        }

        [Fact]
        public async Task Refused_deactivation_shows_the_reason()
        {
            _users.DeactivateResult = OperationResult.Failure("Posledního aktivního správce nelze deaktivovat.");
            var viewModel = await CreateLoadedAsync();
            viewModel.SelectedUser = viewModel.Users[0];

            await viewModel.DeactivateCommand.ExecuteAsync(null);

            Assert.Equal("Posledního aktivního správce nelze deaktivovat.", Assert.Single(_dialogs.Warnings));
            Assert.True(viewModel.Users[0].IsActive);
        }

        [Fact]
        public async Task Deactivating_own_account_warns_explicitly_and_ends_in_logout()
        {
            _users.Users.Add(new AppUser { Id = 3, Email = "druhy@example.cz", DisplayName = "Druhý správce", Role = UserRole.Admin, IsActive = true });
            var viewModel = await CreateLoadedAsync();
            var selfDeactivated = false;
            viewModel.SelfDeactivated += (sender, args) => selfDeactivated = true;
            viewModel.SelectedUser = viewModel.Users[0];   // přihlášený správce (Id 1)

            await viewModel.DeactivateCommand.ExecuteAsync(null);

            Assert.StartsWith("Opravdu chcete deaktivovat svůj vlastní účet?", Assert.Single(_dialogs.Questions));
            Assert.True(selfDeactivated);
        }

        [Fact]
        public async Task New_user_dialog_reloads_the_list_and_selects_the_new_user()
        {
            var viewModel = await CreateLoadedAsync();
            _dialogs.OnDialog = dialog =>
            {
                var add = Assert.IsType<AddUserViewModel>(dialog);
                add.Email = "nova@example.cz";
                add.DisplayName = "Nová Uživatelka";
                add.SelectedRole = add.Roles.Single(option => option.Role == UserRole.Admin);
                add.TemporaryPassword = "Docasne-heslo-1";
                add.ConfirmPassword = "Docasne-heslo-1";

                var confirmed = false;
                add.CloseRequested += (sender, args) => confirmed = args.Result;
                add.SaveCommand.Execute(null);   // s falešnou službou doběhne synchronně
                return confirmed;
            };

            await viewModel.AddUserCommand.ExecuteAsync(null);

            Assert.Equal(new[] { "Create:nova@example.cz:Nová Uživatelka:Admin:Docasne-heslo-1" }, _users.Calls);
            Assert.Equal(3, viewModel.Users.Count);
            Assert.Equal("nova@example.cz", viewModel.SelectedUser.Email);
            Assert.StartsWith("Uživatel byl založen.", Assert.Single(_dialogs.Infos));
        }

        [Fact]
        public async Task Cancelled_new_user_dialog_changes_nothing()
        {
            var viewModel = await CreateLoadedAsync();
            _dialogs.OnDialog = dialog => false;

            await viewModel.AddUserCommand.ExecuteAsync(null);

            Assert.Equal(2, viewModel.Users.Count);
            Assert.Empty(_dialogs.Infos);
        }

        [Fact]
        public async Task New_user_dialog_validates_before_calling_the_service()
        {
            var dialog = new AddUserViewModel(_users)
            {
                Email = "bez-zavinace",
                DisplayName = " ",
                TemporaryPassword = "kratke",
                ConfirmPassword = "jine",
            };

            await dialog.SaveCommand.ExecuteAsync(null);

            Assert.Equal("Zadejte platný e-mail.", dialog.GetError(nameof(dialog.Email)));
            Assert.Equal("Zadejte jméno uživatele.", dialog.GetError(nameof(dialog.DisplayName)));
            Assert.Equal("Heslo musí mít alespoň 8 znaků.", dialog.GetError(nameof(dialog.TemporaryPassword)));
            Assert.Equal("Hesla se neshodují.", dialog.GetError(nameof(dialog.ConfirmPassword)));
            Assert.Empty(_users.Calls);
            Assert.Equal(UserRole.User, dialog.SelectedRole.Role);   // výchozí role je Uživatel
        }

        [Fact]
        public async Task New_user_dialog_shows_a_refusal_from_the_service()
        {
            _users.CreateResult = CreateUserResult.Failure("Uživatel s tímto e-mailem už existuje.");
            var dialog = new AddUserViewModel(_users)
            {
                Email = "trener@example.cz",
                DisplayName = "Petr",
                TemporaryPassword = "Docasne-heslo-1",
                ConfirmPassword = "Docasne-heslo-1",
            };
            var closed = false;
            dialog.CloseRequested += (sender, args) => closed = true;

            await dialog.SaveCommand.ExecuteAsync(null);

            Assert.False(closed);
            Assert.Equal("Uživatel s tímto e-mailem už existuje.", dialog.ErrorMessage);
            Assert.Null(dialog.CreatedUserId);
        }

        [Fact]
        public async Task Password_reset_sets_a_temporary_password_for_the_selected_user()
        {
            var viewModel = await CreateLoadedAsync();
            viewModel.SelectedUser = viewModel.Users[1];
            _dialogs.OnDialog = dialog =>
            {
                var reset = Assert.IsType<ResetPasswordViewModel>(dialog);
                Assert.Equal("Petr Svoboda (trener@example.cz)", reset.UserDescription);
                reset.TemporaryPassword = "Nove-docasne-2";
                reset.ConfirmPassword = "Nove-docasne-2";

                var confirmed = false;
                reset.CloseRequested += (sender, args) => confirmed = args.Result;
                reset.SaveCommand.Execute(null);   // s falešnou službou doběhne synchronně
                return confirmed;
            };

            await viewModel.ResetPasswordCommand.ExecuteAsync(null);

            Assert.Equal(new[] { "Reset:2:Nove-docasne-2" }, _users.Calls);
            Assert.StartsWith("Dočasné heslo je nastaveno.", Assert.Single(_dialogs.Infos));
        }

        [Fact]
        public async Task New_recovery_code_is_generated_only_after_confirmation_and_shown_once()
        {
            var viewModel = await CreateLoadedAsync();

            _dialogs.ConfirmAnswer = false;
            await viewModel.NewRecoveryCodeCommand.ExecuteAsync(null);
            Assert.Empty(_users.Calls);
            Assert.Contains("Dosavadní záchranný kód tím přestane platit.", _dialogs.Questions[0]);

            _dialogs.ConfirmAnswer = true;
            await viewModel.NewRecoveryCodeCommand.ExecuteAsync(null);

            Assert.Equal(new[] { "GenerateRecoveryCode" }, _users.Calls);
            var shown = Assert.IsType<RecoveryCodeViewModel>(Assert.Single(_dialogs.Dialogs));
            Assert.Equal("ABCD-EFGH-JKLM-NPQR-STUV-WXYZ", shown.Code);
        }
    }
}
