using System;
using System.Linq;
using System.Threading.Tasks;
using VolejbalVrsovice.Core.Entities;
using VolejbalVrsovice.Core.Security;
using VolejbalVrsovice.Core.Services;
using VolejbalVrsovice.Data.Security;
using VolejbalVrsovice.Tests.TestSupport;
using Xunit;

namespace VolejbalVrsovice.Tests.Security
{
    public class UserServiceTests : IDisposable
    {
        private readonly AuthFixture _fixture = new AuthFixture();

        public void Dispose()
        {
            _fixture.Dispose();
        }

        // --- nový uživatel ---------------------------------------------------------------------------

        [Fact]
        public async Task New_user_logs_in_with_the_temporary_password_and_must_change_it()
        {
            await _fixture.SetUpAdminAsync();

            var created = await _fixture.Users.CreateUserAsync(" Trener@Example.cz ", "  Petr Svoboda ", UserRole.User, "Docasne-heslo-1");

            Assert.True(created.Succeeded, created.Error);
            Assert.Equal("trener@example.cz", created.User.Email);
            Assert.Equal("Petr Svoboda", created.User.DisplayName);
            Assert.True(created.User.IsActive);
            Assert.True(created.User.MustChangePassword);

            _fixture.Auth.Logout();
            var login = await _fixture.Auth.LoginAsync("trener@example.cz", "Docasne-heslo-1");

            Assert.True(login.IsSuccess);
            Assert.True(login.User.MustChangePassword);
            Assert.Equal(UserRole.User, login.User.Role);

            var change = await _fixture.Auth.CompleteForcedPasswordChangeAsync("Vlastni-heslo-9");
            Assert.True(change.Succeeded, change.Error);
            Assert.Null(change.RecoveryCode);   // záchranný kód dostává jen správce při prvním nastavení

            _fixture.Auth.Logout();
            Assert.Equal(LoginStatus.InvalidCredentials, (await _fixture.Auth.LoginAsync("trener@example.cz", "Docasne-heslo-1")).Status);
            Assert.True((await _fixture.Auth.LoginAsync("trener@example.cz", "Vlastni-heslo-9")).IsSuccess);
        }

        [Theory]
        [InlineData("", "Jméno", "Docasne-heslo-1", "Zadejte platný e-mail.")]
        [InlineData("bez-zavinace", "Jméno", "Docasne-heslo-1", "Zadejte platný e-mail.")]
        [InlineData("a b@example.cz", "Jméno", "Docasne-heslo-1", "Zadejte platný e-mail.")]
        [InlineData("novy@example.cz", "  ", "Docasne-heslo-1", "Zadejte jméno uživatele.")]
        [InlineData("novy@example.cz", "Jméno", "kratke", "Heslo musí mít alespoň 8 znaků.")]
        [InlineData("novy@example.cz", "Jméno", "admin", "Heslo nesmí být „admin“.")]
        [InlineData("ADMIN@admin.cz", "Jméno", "Docasne-heslo-1", "Uživatel s tímto e-mailem už existuje.")]
        public async Task Invalid_new_user_is_refused(string email, string displayName, string password, string expectedError)
        {
            await _fixture.SetUpAdminAsync();

            var result = await _fixture.Users.CreateUserAsync(email, displayName, UserRole.User, password);

            Assert.False(result.Succeeded);
            Assert.Equal(expectedError, result.Error);
            Assert.Single(await _fixture.Users.GetUsersAsync());
            Assert.Single(_fixture.Store.KeyStore.GetUserEmails());
        }

        [Fact]
        public async Task Email_of_a_deactivated_user_cannot_be_reused()
        {
            await _fixture.SetUpAdminAsync();
            var user = await _fixture.CreateUserAsync("trener@example.cz", "Docasne-heslo-1");
            await _fixture.Users.DeactivateAsync(user.Id);

            var result = await _fixture.Users.CreateUserAsync("trener@example.cz", "Znovu", UserRole.User, "Docasne-heslo-2");

            Assert.Equal("Uživatel s tímto e-mailem už existuje (je deaktivovaný).", result.Error);
        }

        // --- role ------------------------------------------------------------------------------------

        [Fact]
        public async Task Ordinary_user_cannot_manage_users()
        {
            await _fixture.SetUpAdminAsync();
            var admin = _fixture.Session.User;
            await _fixture.CreateUserAsync("trener@example.cz", "Docasne-heslo-1");
            await _fixture.ActivateUserAsync("trener@example.cz", "Docasne-heslo-1", "Vlastni-heslo-9");

            Assert.False(_fixture.Session.User.IsAdmin);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _fixture.Users.GetUsersAsync());
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _fixture.Users.CreateUserAsync("dalsi@example.cz", "Další", UserRole.Admin, "Docasne-heslo-1"));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _fixture.Users.ResetPasswordAsync(admin.Id, "Docasne-heslo-1"));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _fixture.Users.DeactivateAsync(admin.Id));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _fixture.Users.GenerateRecoveryCodeAsync());
        }

        [Fact]
        public async Task Nobody_logged_in_cannot_manage_users()
        {
            await _fixture.SetUpAdminAsync();
            _fixture.Auth.Logout();

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _fixture.Users.GetUsersAsync());
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _fixture.Users.GenerateRecoveryCodeAsync());
        }

        [Fact]
        public async Task Admin_with_a_pending_password_change_cannot_manage_users_yet()
        {
            await _fixture.Auth.InitializeAsync();
            await _fixture.Auth.LoginAsync(DefaultAdmin.Email, DefaultAdmin.Password);

            // Dokud výchozí správce nezmění heslo, není přístupné nic jiného.
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _fixture.Users.GetUsersAsync());
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _fixture.Users.CreateUserAsync("novy@example.cz", "Nový", UserRole.User, "Docasne-heslo-1"));
        }

        [Fact]
        public async Task Only_an_admin_ever_receives_a_recovery_code()
        {
            // Výchozí správce si heslo ještě nezměnil (záchranný kód neexistuje) a v úložišti je už i běžný
            // uživatel s dočasným heslem – při jeho vynucené změně hesla se kód vydat nesmí.
            await _fixture.Auth.InitializeAsync();
            await _fixture.Auth.LoginAsync(DefaultAdmin.Email, DefaultAdmin.Password);
            await _fixture.UserRepository.InsertAsync(new AppUser { Email = "trener@example.cz", DisplayName = "Trenér", Role = UserRole.User, IsActive = true, MustChangePassword = true });
            _fixture.Store.KeyStore.SetUser("trener@example.cz", KeyEnvelope.Wrap(
                _fixture.Session.GetKey(), "Docasne-heslo-1", TestStore.FastIterations, KeyBinding.ForUser("trener@example.cz")));
            _fixture.Auth.Logout();

            await _fixture.Auth.LoginAsync("trener@example.cz", "Docasne-heslo-1");
            var change = await _fixture.Auth.CompleteForcedPasswordChangeAsync("Vlastni-heslo-9");

            Assert.True(change.Succeeded, change.Error);
            Assert.Null(change.RecoveryCode);
            Assert.Null(_fixture.Store.KeyStore.GetRecovery());
        }

        // --- reset hesla -------------------------------------------------------------------------------

        [Fact]
        public async Task After_reset_the_user_logs_in_with_the_temporary_password_and_must_change_it()
        {
            await _fixture.SetUpAdminAsync();
            var user = await _fixture.CreateUserAsync("trener@example.cz", "Docasne-heslo-1");
            await _fixture.ActivateUserAsync("trener@example.cz", "Docasne-heslo-1", "Vlastni-heslo-9");
            _fixture.Auth.Logout();
            await _fixture.LoginAdminAsync();

            var reset = await _fixture.Users.ResetPasswordAsync(user.Id, "Nove-docasne-2");

            Assert.True(reset.Succeeded, reset.Error);
            _fixture.Auth.Logout();
            Assert.Equal(LoginStatus.InvalidCredentials, (await _fixture.Auth.LoginAsync("trener@example.cz", "Vlastni-heslo-9")).Status);

            var login = await _fixture.Auth.LoginAsync("trener@example.cz", "Nove-docasne-2");
            Assert.True(login.IsSuccess);
            Assert.True(login.User.MustChangePassword);
        }

        /// <summary>
        /// Vlastní heslo se mění přes „Změnit heslo“. Reset sebe sama by přihlášeného správce převedl
        /// do stavu „musí změnit heslo“ a správa uživatelů by mu přestala fungovat.
        /// </summary>
        [Fact]
        public async Task Admin_cannot_reset_the_own_password()
        {
            await _fixture.SetUpAdminAsync();
            var admin = _fixture.Session.User;
            var keysBefore = System.IO.File.ReadAllBytes(_fixture.Store.Paths.KeysFile);

            var reset = await _fixture.Users.ResetPasswordAsync(admin.Id, "Nove-docasne-2");

            Assert.Equal("Vlastní heslo nelze resetovat – použijte „Změnit heslo“.", reset.Error);
            Assert.Equal(keysBefore, System.IO.File.ReadAllBytes(_fixture.Store.Paths.KeysFile));
            Assert.False((await _fixture.UserRepository.GetByIdAsync(admin.Id)).MustChangePassword);
            Assert.NotEmpty(await _fixture.Users.GetUsersAsync());   // správa uživatelů funguje dál
        }

        [Fact]
        public async Task Reset_is_refused_for_weak_passwords_unknown_and_deactivated_users()
        {
            await _fixture.SetUpAdminAsync();
            var user = await _fixture.CreateUserAsync("trener@example.cz", "Docasne-heslo-1");

            var weak = await _fixture.Users.ResetPasswordAsync(user.Id, "1234567");
            var unknown = await _fixture.Users.ResetPasswordAsync(98765, "Nove-docasne-2");
            await _fixture.Users.DeactivateAsync(user.Id);
            var deactivated = await _fixture.Users.ResetPasswordAsync(user.Id, "Nove-docasne-2");

            Assert.Equal("Heslo musí mít alespoň 8 znaků.", weak.Error);
            Assert.Equal("Uživatel neexistuje.", unknown.Error);
            Assert.Equal("Deaktivovanému uživateli nelze nastavit heslo.", deactivated.Error);
            Assert.Null(_fixture.Store.KeyStore.FindUser("trener@example.cz"));
        }

        // --- deaktivace --------------------------------------------------------------------------------

        [Fact]
        public async Task Deactivated_user_can_no_longer_log_in()
        {
            await _fixture.SetUpAdminAsync();
            var user = await _fixture.CreateUserAsync("trener@example.cz", "Docasne-heslo-1");

            var result = await _fixture.Users.DeactivateAsync(user.Id);

            Assert.True(result.Succeeded, result.Error);
            Assert.False((await _fixture.UserRepository.GetByIdAsync(user.Id)).IsActive);
            Assert.Null(_fixture.Store.KeyStore.FindUser("trener@example.cz"));   // záznam z keys.json je pryč
            Assert.Equal(new[] { "admin@admin.cz" }, _fixture.Store.KeyStore.GetUserEmails());

            _fixture.Auth.Logout();
            var login = await _fixture.Auth.LoginAsync("trener@example.cz", "Docasne-heslo-1");
            Assert.Equal(LoginStatus.InvalidCredentials, login.Status);
            Assert.False(_fixture.Session.IsAuthenticated);
        }

        [Fact]
        public async Task Last_active_admin_cannot_be_deactivated()
        {
            await _fixture.SetUpAdminAsync();
            var admin = _fixture.Session.User;
            await _fixture.CreateUserAsync("trener@example.cz", "Docasne-heslo-1");   // běžný uživatel se nepočítá

            var result = await _fixture.Users.DeactivateAsync(admin.Id);

            Assert.False(result.Succeeded);
            Assert.Equal("Posledního aktivního správce nelze deaktivovat.", result.Error);
            Assert.True((await _fixture.UserRepository.GetByIdAsync(admin.Id)).IsActive);
            Assert.NotNull(_fixture.Store.KeyStore.FindUser("admin@admin.cz"));
        }

        [Fact]
        public async Task One_of_two_admins_can_be_deactivated_but_not_the_remaining_one()
        {
            await _fixture.SetUpAdminAsync();
            var first = _fixture.Session.User;
            var second = await _fixture.CreateUserAsync("druhy@example.cz", "Docasne-heslo-1", UserRole.Admin);

            var deactivateFirst = await _fixture.Users.DeactivateAsync(first.Id);   // správce deaktivuje sám sebe
            Assert.True(deactivateFirst.Succeeded, deactivateFirst.Error);

            // Druhý správce převezme správu; sám je teď poslední a deaktivovat nejde.
            await _fixture.ActivateUserAsync("druhy@example.cz", "Docasne-heslo-1", "Vlastni-heslo-9");
            var deactivateSecond = await _fixture.Users.DeactivateAsync(second.Id);

            Assert.Equal("Posledního aktivního správce nelze deaktivovat.", deactivateSecond.Error);
            Assert.Equal(1, await _fixture.UserRepository.CountActiveAdminsAsync());

            _fixture.Auth.Logout();
            Assert.Equal(LoginStatus.InvalidCredentials, (await _fixture.LoginAdminAsync()).Status);
        }

        [Fact]
        public async Task Deactivating_twice_is_harmless()
        {
            await _fixture.SetUpAdminAsync();
            var user = await _fixture.CreateUserAsync("trener@example.cz", "Docasne-heslo-1");

            Assert.True((await _fixture.Users.DeactivateAsync(user.Id)).Succeeded);
            Assert.True((await _fixture.Users.DeactivateAsync(user.Id)).Succeeded);
            Assert.Equal("Uživatel neexistuje.", (await _fixture.Users.DeactivateAsync(424242)).Error);
        }

        // --- záchranný kód na vyžádání ---------------------------------------------------------------------

        [Fact]
        public async Task New_recovery_code_replaces_the_previous_one()
        {
            var original = await _fixture.SetUpAdminAsync();

            var replacement = await _fixture.Users.GenerateRecoveryCodeAsync();

            Assert.Matches("^([A-HJ-NP-Z2-9]{4}-){5}[A-HJ-NP-Z2-9]{4}$", replacement);
            Assert.NotEqual(original, replacement);

            _fixture.Auth.Logout();
            Assert.Equal("Záchranný kód není platný.", (await _fixture.Auth.RecoverAsync(original, "admin@admin.cz", "Obnovene-heslo-3")).Error);
            Assert.True((await _fixture.Auth.RecoverAsync(replacement, "admin@admin.cz", "Obnovene-heslo-3")).Succeeded);
        }

        // --- seznam ----------------------------------------------------------------------------------------

        [Fact]
        public async Task Admin_sees_all_users_including_deactivated_ones()
        {
            await _fixture.SetUpAdminAsync();
            await _fixture.CreateUserAsync("trener@example.cz", "Docasne-heslo-1");
            var former = await _fixture.CreateUserAsync("byvaly@example.cz", "Docasne-heslo-1");
            await _fixture.Users.DeactivateAsync(former.Id);

            var users = await _fixture.Users.GetUsersAsync();

            Assert.Equal(new[] { "admin@admin.cz", "byvaly@example.cz", "trener@example.cz" }, users.Select(user => user.Email));
            Assert.False(users.Single(user => user.Email == "byvaly@example.cz").IsActive);
        }
    }
}
