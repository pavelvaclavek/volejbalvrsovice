using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using VolejbalVrsovice.Core.Entities;
using VolejbalVrsovice.Core.Errors;
using VolejbalVrsovice.Core.Security;
using VolejbalVrsovice.Core.Services;
using VolejbalVrsovice.Data.Db;
using VolejbalVrsovice.Data.Repositories;
using VolejbalVrsovice.Data.Security;
using VolejbalVrsovice.Tests.TestSupport;
using Xunit;

namespace VolejbalVrsovice.Tests.Security
{
    public class AuthServiceTests : IDisposable
    {
        private readonly AuthFixture _fixture = new AuthFixture();

        public void Dispose()
        {
            _fixture.Dispose();
        }

        // --- první spuštění a přihlášení ----------------------------------------------------------

        [Fact]
        public async Task Initialize_creates_the_store_once()
        {
            await _fixture.Auth.InitializeAsync();
            var keysAfterFirstRun = File.ReadAllBytes(_fixture.Store.Paths.KeysFile);
            var databaseAfterFirstRun = File.ReadAllBytes(_fixture.Store.Paths.DatabaseFile);

            await _fixture.Auth.InitializeAsync();

            Assert.Equal(keysAfterFirstRun, File.ReadAllBytes(_fixture.Store.Paths.KeysFile));
            Assert.Equal(databaseAfterFirstRun, File.ReadAllBytes(_fixture.Store.Paths.DatabaseFile));
        }

        [Fact]
        public async Task Initialize_refuses_an_incomplete_store()
        {
            await _fixture.Auth.InitializeAsync();
            File.Delete(_fixture.Store.Paths.KeysFile);

            await Assert.ThrowsAsync<DataStoreMismatchException>(() => _fixture.Auth.InitializeAsync());

            Assert.False(File.Exists(_fixture.Store.Paths.KeysFile));
        }

        [Fact]
        public async Task Default_admin_logs_in_and_is_required_to_change_the_password()
        {
            await _fixture.Auth.InitializeAsync();

            var result = await _fixture.Auth.LoginAsync("admin@admin.cz", "admin");

            Assert.Equal(LoginStatus.Success, result.Status);
            Assert.Equal("admin@admin.cz", result.User.Email);
            Assert.True(result.User.IsAdmin);
            Assert.True(result.User.MustChangePassword);
            Assert.Equal(_fixture.Store.Clock.UtcNow, result.User.LastLoginAt);
            Assert.True(_fixture.Session.IsAuthenticated);
            Assert.Equal(result.User.Id, _fixture.Session.UserId);
        }

        [Fact]
        public async Task Email_is_matched_case_insensitively()
        {
            await _fixture.Auth.InitializeAsync();

            var result = await _fixture.Auth.LoginAsync("  ADMIN@Admin.CZ ", "admin");

            Assert.True(result.IsSuccess);
        }

        [Fact]
        public async Task Wrong_password_and_unknown_email_are_indistinguishable()
        {
            await _fixture.Auth.InitializeAsync();

            var wrongPassword = await _fixture.Auth.LoginAsync("admin@admin.cz", "spatne-heslo");
            var unknownEmail = await _fixture.Auth.LoginAsync("nikdo@example.cz", "admin");

            Assert.Equal(LoginStatus.InvalidCredentials, wrongPassword.Status);
            Assert.Equal(LoginStatus.InvalidCredentials, unknownEmail.Status);
            Assert.Null(wrongPassword.User);
            Assert.Null(unknownEmail.User);
            Assert.False(_fixture.Session.IsAuthenticated);
        }

        [Theory]
        [InlineData("", "")]
        [InlineData(null, null)]
        [InlineData("admin@admin.cz", "")]
        [InlineData("", "admin")]
        public async Task Empty_credentials_are_simply_invalid(string email, string password)
        {
            await _fixture.Auth.InitializeAsync();

            var result = await _fixture.Auth.LoginAsync(email, password);

            Assert.Equal(LoginStatus.InvalidCredentials, result.Status);
        }

        [Fact]
        public async Task Login_unlocks_the_database_for_repositories()
        {
            await _fixture.SetUpAdminAsync();
            var teams = new TeamRepository(_fixture.SessionDatabase, _fixture.Session, _fixture.Store.Clock);

            var team = new Team { Name = "Ženy A", IsActive = true };
            await teams.InsertAsync(team);

            var loaded = await teams.GetByIdAsync(team.Id);
            Assert.Equal("Ženy A", loaded.Name);
            Assert.Equal(_fixture.Session.UserId, loaded.CreatedBy);   // auditní sloupce = přihlášený uživatel
        }

        [Fact]
        public async Task Logout_ends_the_session_and_locks_the_database()
        {
            await _fixture.SetUpAdminAsync();

            _fixture.Auth.Logout();

            Assert.False(_fixture.Session.IsAuthenticated);
            Assert.Null(_fixture.Session.User);
            Assert.Throws<InvalidOperationException>(() => _fixture.Session.GetKey());
            await Assert.ThrowsAsync<InvalidOperationException>(() => _fixture.UserRepository.GetAllAsync());
        }

        [Fact]
        public void Session_wipes_its_key_from_memory_when_it_ends()
        {
            var session = new AppSession();
            var key = KeyEnvelope.GenerateKey();
            var original = (byte[])key.Clone();

            session.Start(TestUsers.Admin(), key);   // relace pole přebírá
            var copy = session.GetKey();

            session.End();

            Assert.All(key, value => Assert.Equal(0, value));           // vlastní pole relace je přepsané nulami
            Assert.Equal(original, copy);                               // dříve vydaná kopie zůstala platná…
            Assert.NotSame(key, copy);                                  // …protože relace své pole ven nedává
            Assert.Throws<InvalidOperationException>(() => session.GetKey());
        }

        /// <summary>
        /// Odhlášení během několikasekundového zabalování klíče nesmí vést k uložení nepoužitelného záznamu:
        /// operace pracuje s kopií klíče a před zápisem ověří, že relace trvá.
        /// </summary>
        [Fact]
        public async Task Password_change_interrupted_by_logout_saves_nothing()
        {
            using (var slow = new AuthFixture(iterations: 20000))   // odvození klíče trvá dost dlouho na to, aby odhlášení přišlo uprostřed
            {
                await slow.SetUpAdminAsync();
                var keysBefore = File.ReadAllBytes(slow.Store.Paths.KeysFile);

                var change = slow.Auth.ChangeOwnPasswordAsync(AuthFixture.AdminPassword, "Uplne-nove-heslo-2");
                slow.Auth.Logout();   // uživatel se odhlásil, zatímco se heslo ověřuje a klíč zabaluje

                await Assert.ThrowsAsync<InvalidOperationException>(() => change);

                Assert.Equal(keysBefore, File.ReadAllBytes(slow.Store.Paths.KeysFile));
                Assert.True((await slow.LoginAdminAsync()).IsSuccess);   // původní heslo platí dál
            }
        }

        [Fact]
        public async Task Failed_login_leaves_no_session()
        {
            await _fixture.Auth.InitializeAsync();

            await _fixture.Auth.LoginAsync("admin@admin.cz", "spatne-heslo");

            Assert.Throws<InvalidOperationException>(() => _fixture.Session.GetKey());
        }

        // --- vynucená změna hesla ---------------------------------------------------------------------

        [Fact]
        public async Task After_forced_change_the_default_password_no_longer_works()
        {
            await _fixture.Auth.InitializeAsync();
            await _fixture.Auth.LoginAsync("admin@admin.cz", "admin");

            var change = await _fixture.Auth.CompleteForcedPasswordChangeAsync("Moje-nove-heslo-1");

            Assert.True(change.Succeeded);
            Assert.False(_fixture.Session.User.MustChangePassword);

            _fixture.Auth.Logout();
            Assert.Equal(LoginStatus.InvalidCredentials, (await _fixture.Auth.LoginAsync("admin@admin.cz", "admin")).Status);

            var login = await _fixture.Auth.LoginAsync("admin@admin.cz", "Moje-nove-heslo-1");
            Assert.True(login.IsSuccess);
            Assert.False(login.User.MustChangePassword);
        }

        [Fact]
        public async Task First_forced_change_issues_a_recovery_code_exactly_once()
        {
            await _fixture.Auth.InitializeAsync();
            await _fixture.Auth.LoginAsync("admin@admin.cz", "admin");
            Assert.Null(_fixture.Store.KeyStore.GetRecovery());

            var first = await _fixture.Auth.CompleteForcedPasswordChangeAsync("Moje-nove-heslo-1");

            Assert.Matches("^([A-HJ-NP-Z2-9]{4}-){5}[A-HJ-NP-Z2-9]{4}$", first.RecoveryCode);
            Assert.NotNull(_fixture.Store.KeyStore.GetRecovery());

            // Pozdější změny hesla už kód nevydávají.
            var second = await _fixture.Auth.ChangeOwnPasswordAsync("Moje-nove-heslo-1", "Jeste-novejsi-2");
            Assert.True(second.Succeeded);
            Assert.Null(second.RecoveryCode);
        }

        [Theory]
        [InlineData("", "Zadejte heslo.")]
        [InlineData("kratke7", "Heslo musí mít alespoň 8 znaků.")]
        [InlineData("admin", "Heslo nesmí být „admin“.")]
        [InlineData("ADMIN", "Heslo nesmí být „admin“.")]
        public async Task Forced_change_enforces_the_password_policy(string newPassword, string expectedError)
        {
            await _fixture.Auth.InitializeAsync();
            await _fixture.Auth.LoginAsync("admin@admin.cz", "admin");

            var change = await _fixture.Auth.CompleteForcedPasswordChangeAsync(newPassword);

            Assert.False(change.Succeeded);
            Assert.Equal(expectedError, change.Error);
            Assert.True(_fixture.Session.User.MustChangePassword);
            Assert.Null(_fixture.Store.KeyStore.GetRecovery());

            // Výchozí heslo platí dál – nic se nezměnilo.
            _fixture.Auth.Logout();
            Assert.True((await _fixture.Auth.LoginAsync("admin@admin.cz", "admin")).IsSuccess);
        }

        [Fact]
        public async Task Forced_change_refuses_to_keep_the_temporary_password()
        {
            await _fixture.SetUpAdminAsync();
            await _fixture.CreateUserAsync("trener@example.cz", "Docasne-heslo-1");
            _fixture.Auth.Logout();
            await _fixture.Auth.LoginAsync("trener@example.cz", "Docasne-heslo-1");

            var change = await _fixture.Auth.CompleteForcedPasswordChangeAsync("Docasne-heslo-1");

            Assert.False(change.Succeeded);
            Assert.Equal("Nové heslo se musí lišit od dosavadního.", change.Error);
            Assert.True(_fixture.Session.User.MustChangePassword);
        }

        [Fact]
        public async Task Password_operations_require_a_logged_in_user()
        {
            await _fixture.Auth.InitializeAsync();

            await Assert.ThrowsAsync<InvalidOperationException>(() => _fixture.Auth.CompleteForcedPasswordChangeAsync("Nejake-heslo-1"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => _fixture.Auth.ChangeOwnPasswordAsync("admin", "Nejake-heslo-1"));
        }

        // --- dobrovolná změna hesla ----------------------------------------------------------------------

        [Fact]
        public async Task Own_password_change_requires_the_current_password()
        {
            await _fixture.SetUpAdminAsync();

            var wrong = await _fixture.Auth.ChangeOwnPasswordAsync("spatne-soucasne", "Uplne-nove-heslo-2");
            var same = await _fixture.Auth.ChangeOwnPasswordAsync(AuthFixture.AdminPassword, AuthFixture.AdminPassword);
            var weak = await _fixture.Auth.ChangeOwnPasswordAsync(AuthFixture.AdminPassword, "kratke");

            Assert.Equal("Současné heslo není správné.", wrong.Error);
            Assert.Equal("Nové heslo se musí lišit od současného.", same.Error);
            Assert.Equal("Heslo musí mít alespoň 8 znaků.", weak.Error);

            _fixture.Auth.Logout();
            Assert.True((await _fixture.LoginAdminAsync()).IsSuccess);   // původní heslo platí dál
        }

        [Fact]
        public async Task Own_password_change_replaces_the_old_password()
        {
            await _fixture.SetUpAdminAsync();

            var change = await _fixture.Auth.ChangeOwnPasswordAsync(AuthFixture.AdminPassword, "Uplne-nove-heslo-2");

            Assert.True(change.Succeeded);
            _fixture.Auth.Logout();
            Assert.Equal(LoginStatus.InvalidCredentials, (await _fixture.LoginAdminAsync()).Status);
            Assert.True((await _fixture.Auth.LoginAsync("admin@admin.cz", "Uplne-nove-heslo-2")).IsSuccess);
        }

        // --- blokace po 5 pokusech ----------------------------------------------------------------------

        [Fact]
        public async Task Fifth_failed_attempt_locks_login_for_five_minutes()
        {
            await _fixture.SetUpAdminAsync();
            _fixture.Auth.Logout();

            for (var attempt = 1; attempt <= 4; attempt++)
            {
                Assert.Equal(LoginStatus.InvalidCredentials, (await _fixture.Auth.LoginAsync("admin@admin.cz", "spatne-" + attempt)).Status);
            }

            var fifth = await _fixture.Auth.LoginAsync("nikdo@example.cz", "spatne-5");   // počítá se i neznámý e-mail
            Assert.Equal(LoginStatus.LockedOut, fifth.Status);
            Assert.Equal(TimeSpan.FromMinutes(5), fifth.LockoutRemaining);

            // Během blokace neprojde ani správné heslo.
            _fixture.Store.Clock.Advance(TimeSpan.FromMinutes(4));
            var duringLockout = await _fixture.LoginAdminAsync();
            Assert.Equal(LoginStatus.LockedOut, duringLockout.Status);
            Assert.Equal(TimeSpan.FromMinutes(1), duringLockout.LockoutRemaining);
            Assert.False(_fixture.Session.IsAuthenticated);

            _fixture.Store.Clock.Advance(TimeSpan.FromMinutes(1));
            Assert.True((await _fixture.LoginAdminAsync()).IsSuccess);
        }

        [Fact]
        public async Task Successful_login_resets_the_failure_counter()
        {
            await _fixture.SetUpAdminAsync();
            _fixture.Auth.Logout();

            for (var round = 0; round < 2; round++)
            {
                for (var attempt = 0; attempt < 4; attempt++)
                {
                    Assert.Equal(LoginStatus.InvalidCredentials, (await _fixture.Auth.LoginAsync("admin@admin.cz", "spatne")).Status);
                }

                Assert.True((await _fixture.LoginAdminAsync()).IsSuccess);
                _fixture.Auth.Logout();
            }
        }

        // --- záchranný kód ---------------------------------------------------------------------------------

        [Fact]
        public async Task Recovery_code_sets_a_new_admin_password_and_data_stay_readable()
        {
            var code = await _fixture.SetUpAdminAsync();
            var teams = new TeamRepository(_fixture.SessionDatabase, _fixture.Session, _fixture.Store.Clock);
            await teams.InsertAsync(new Team { Name = "Ženy A", IsActive = true });
            _fixture.Auth.Logout();

            var recovery = await _fixture.Auth.RecoverAsync(code, "admin@admin.cz", "Obnovene-heslo-3");

            Assert.True(recovery.Succeeded, recovery.Error);
            Assert.False(_fixture.Session.IsAuthenticated);   // obnova nikoho nepřihlašuje

            Assert.Equal(LoginStatus.InvalidCredentials, (await _fixture.LoginAdminAsync()).Status);   // zapomenuté heslo už neplatí
            Assert.True((await _fixture.Auth.LoginAsync("admin@admin.cz", "Obnovene-heslo-3")).IsSuccess);
            Assert.Equal("Ženy A", (await teams.GetAllAsync()).Single().Name);
        }

        [Fact]
        public async Task Recovery_code_is_accepted_in_any_letter_case_and_grouping()
        {
            var code = await _fixture.SetUpAdminAsync();
            _fixture.Auth.Logout();
            var typed = "  " + code.Replace("-", " ").ToLowerInvariant() + " ";

            var recovery = await _fixture.Auth.RecoverAsync(typed, "ADMIN@admin.cz", "Obnovene-heslo-3");

            Assert.True(recovery.Succeeded, recovery.Error);
        }

        [Fact]
        public async Task Recovery_is_refused_for_a_wrong_code_and_nothing_changes()
        {
            await _fixture.SetUpAdminAsync();
            _fixture.Auth.Logout();
            var keysBefore = File.ReadAllBytes(_fixture.Store.Paths.KeysFile);

            var wrongCode = await _fixture.Auth.RecoverAsync(RecoveryCode.Generate(), "admin@admin.cz", "Obnovene-heslo-3");
            var malformed = await _fixture.Auth.RecoverAsync("ABCD-1234", "admin@admin.cz", "Obnovene-heslo-3");

            Assert.Equal("Záchranný kód není platný.", wrongCode.Error);
            Assert.StartsWith("Záchranný kód nemá platný tvar", malformed.Error);
            Assert.Equal(keysBefore, File.ReadAllBytes(_fixture.Store.Paths.KeysFile));
            Assert.True((await _fixture.LoginAdminAsync()).IsSuccess);
        }

        [Fact]
        public async Task Recovery_works_only_for_an_active_admin_and_a_valid_password()
        {
            var code = await _fixture.SetUpAdminAsync();
            await _fixture.CreateUserAsync("trener@example.cz", "Docasne-heslo-1");
            _fixture.Auth.Logout();

            var ordinaryUser = await _fixture.Auth.RecoverAsync(code, "trener@example.cz", "Obnovene-heslo-3");
            var unknown = await _fixture.Auth.RecoverAsync(code, "nikdo@example.cz", "Obnovene-heslo-3");
            var noEmail = await _fixture.Auth.RecoverAsync(code, " ", "Obnovene-heslo-3");
            var weak = await _fixture.Auth.RecoverAsync(code, "admin@admin.cz", "admin");

            Assert.Equal("Zadaný e-mail nepatří aktivnímu správci.", ordinaryUser.Error);
            Assert.Equal("Zadaný e-mail nepatří aktivnímu správci.", unknown.Error);
            Assert.Equal("Zadejte e-mail správce.", noEmail.Error);
            Assert.Equal("Heslo nesmí být „admin“.", weak.Error);

            // Dočasné heslo běžného uživatele zůstalo v platnosti.
            Assert.True((await _fixture.Auth.LoginAsync("trener@example.cz", "Docasne-heslo-1")).IsSuccess);
        }

        [Fact]
        public async Task Recovery_is_impossible_before_any_code_was_issued()
        {
            await _fixture.Auth.InitializeAsync();

            var recovery = await _fixture.Auth.RecoverAsync(RecoveryCode.Generate(), "admin@admin.cz", "Obnovene-heslo-3");

            Assert.Equal("Záchranný kód není platný.", recovery.Error);
        }

        [Fact]
        public async Task Recovery_can_restore_access_for_another_admin_and_clears_the_forced_change()
        {
            var code = await _fixture.SetUpAdminAsync();
            await _fixture.CreateUserAsync("druhy@example.cz", "Docasne-heslo-1", UserRole.Admin);
            _fixture.Auth.Logout();

            var recovery = await _fixture.Auth.RecoverAsync(code, "druhy@example.cz", "Obnovene-heslo-3");
            var login = await _fixture.Auth.LoginAsync("druhy@example.cz", "Obnovene-heslo-3");

            Assert.True(recovery.Succeeded, recovery.Error);
            Assert.True(login.IsSuccess);
            Assert.False(login.User.MustChangePassword);
            Assert.Equal(LoginStatus.InvalidCredentials, (await _fixture.Auth.LoginAsync("druhy@example.cz", "Docasne-heslo-1")).Status);
        }

        // --- nesoulad úložiště, migrace -------------------------------------------------------------------------

        [Fact]
        public async Task Login_fails_loudly_when_keys_belong_to_another_database()
        {
            await _fixture.Auth.InitializeAsync();
            using (var other = TestStore.CreateUnlocked())
            {
                File.Copy(other.Paths.KeysFile, _fixture.Store.Paths.KeysFile, overwrite: true);
            }

            var databaseBefore = File.ReadAllBytes(_fixture.Store.Paths.DatabaseFile);

            await Assert.ThrowsAsync<DataStoreMismatchException>(() => _fixture.Auth.LoginAsync("admin@admin.cz", "admin"));

            Assert.False(_fixture.Session.IsAuthenticated);
            Assert.Equal(databaseBefore, File.ReadAllBytes(_fixture.Store.Paths.DatabaseFile));
        }

        [Fact]
        public async Task Login_with_a_newer_application_migrates_the_schema_after_a_backup()
        {
            await _fixture.SetUpAdminAsync();
            _fixture.Auth.Logout();

            var newer = new Migrator(Migrator.LoadEmbedded().Concat(new[]
            {
                new Migration(2, "Test", "CREATE TABLE TestMigrace (Id INTEGER PRIMARY KEY);"),
            }));
            var newerAuth = _fixture.CreateAuthService(_fixture.Store.CreateDataStore(newer));

            var login = await newerAuth.LoginAsync("admin@admin.cz", AuthFixture.AdminPassword);

            Assert.True(login.IsSuccess);
            var backup = Assert.Single(Directory.GetDirectories(_fixture.Store.Paths.BackupDirectory));
            Assert.True(File.Exists(Path.Combine(backup, "volejbal.db")));
            Assert.True(File.Exists(Path.Combine(backup, "keys.json")));
        }

        // --- nekonzistentní záznamy v keys.json ---------------------------------------------------------------------

        [Fact]
        public async Task Key_entry_without_a_user_in_the_database_is_treated_as_invalid_credentials()
        {
            await _fixture.SetUpAdminAsync();
            var stray = KeyEnvelope.Wrap(
                _fixture.Session.GetKey(), "Heslo-ducha-1", TestStore.FastIterations, KeyBinding.ForUser("duch@example.cz"));
            _fixture.Store.KeyStore.SetUser("duch@example.cz", stray);
            _fixture.Auth.Logout();

            var result = await _fixture.Auth.LoginAsync("duch@example.cz", "Heslo-ducha-1");

            Assert.Equal(LoginStatus.InvalidCredentials, result.Status);
            Assert.False(_fixture.Session.IsAuthenticated);
        }

        /// <summary>
        /// Běžný uživatel si v keys.json (Poznámkovým blokem) zkopíruje svůj záznam pod e-mail správce.
        /// Záznam je svázaný s e-mailem, takže se neověří – správcem se tím nestane.
        /// </summary>
        [Fact]
        public async Task Own_key_record_copied_under_the_admin_email_does_not_log_in_as_admin()
        {
            await _fixture.SetUpAdminAsync();
            await _fixture.CreateUserAsync("trener@example.cz", "Docasne-heslo-1");
            _fixture.Auth.Logout();

            var ownRecord = _fixture.Store.KeyStore.FindUser("trener@example.cz");
            _fixture.Store.KeyStore.SetUser("admin@admin.cz", ownRecord);

            var result = await _fixture.Auth.LoginAsync("admin@admin.cz", "Docasne-heslo-1");

            Assert.Equal(LoginStatus.InvalidCredentials, result.Status);
            Assert.False(_fixture.Session.IsAuthenticated);
        }

        /// <summary>Záznam uživatele vložený do bloku „recovery“ nefunguje jako záchranný kód.</summary>
        [Fact]
        public async Task User_key_record_pasted_as_recovery_does_not_work_as_a_recovery_code()
        {
            await _fixture.SetUpAdminAsync();
            const string codeLikePassword = "ABCDEFGHJKLMNPQRSTUVWXYZ";
            await _fixture.CreateUserAsync("trener@example.cz", codeLikePassword);
            _fixture.Auth.Logout();

            _fixture.Store.KeyStore.SetRecovery(_fixture.Store.KeyStore.FindUser("trener@example.cz"));

            var recovery = await _fixture.Auth.RecoverAsync("ABCD-EFGH-JKLM-NPQR-STUV-WXYZ", "admin@admin.cz", "Obnovene-heslo-3");

            Assert.Equal("Záchranný kód není platný.", recovery.Error);
            Assert.True((await _fixture.LoginAdminAsync()).IsSuccess);
        }

        // --- pojistka: správce bez záchranného kódu --------------------------------------------------------------

        /// <summary>
        /// Aplikace spadla po změně výchozího hesla, ale před vydáním záchranného kódu. Při příštím
        /// přihlášení správce se kód vydá dodatečně – instalace nesmí zůstat bez záchranného kódu.
        /// </summary>
        [Fact]
        public async Task Admin_without_a_recovery_code_receives_one_at_the_next_login()
        {
            await _fixture.Auth.InitializeAsync();
            await _fixture.Auth.LoginAsync(DefaultAdmin.Email, DefaultAdmin.Password);

            // Stav „po pádu“: heslo už je změněné a příznak smazaný, záchranný kód neexistuje.
            var admin = _fixture.Session.User;
            _fixture.Store.KeyStore.SetUser(admin.Email, KeyEnvelope.Wrap(
                _fixture.Session.GetKey(), AuthFixture.AdminPassword, TestStore.FastIterations, KeyBinding.ForUser(admin.Email)));
            admin.MustChangePassword = false;
            await _fixture.UserRepository.UpdateAsync(admin);
            _fixture.Auth.Logout();
            Assert.Null(_fixture.Store.KeyStore.GetRecovery());

            await _fixture.LoginAdminAsync();
            var code = await _fixture.Auth.IssueMissingRecoveryCodeAsync();
            var again = await _fixture.Auth.IssueMissingRecoveryCodeAsync();

            Assert.Matches("^([A-HJ-NP-Z2-9]{4}-){5}[A-HJ-NP-Z2-9]{4}$", code);
            Assert.Null(again);   // kód už existuje, podruhé se nic nevydává

            _fixture.Auth.Logout();
            Assert.True((await _fixture.Auth.RecoverAsync(code, admin.Email, "Obnovene-heslo-3")).Succeeded);
        }

        [Fact]
        public async Task Missing_recovery_code_is_never_issued_to_an_ordinary_user_or_before_the_password_change()
        {
            await _fixture.Auth.InitializeAsync();

            Assert.Null(await _fixture.Auth.IssueMissingRecoveryCodeAsync());   // nikdo není přihlášen

            await _fixture.Auth.LoginAsync(DefaultAdmin.Email, DefaultAdmin.Password);
            Assert.Null(await _fixture.Auth.IssueMissingRecoveryCodeAsync());   // správce ještě nezměnil výchozí heslo

            await _fixture.UserRepository.InsertAsync(new AppUser { Email = "trener@example.cz", DisplayName = "Trenér", Role = UserRole.User, IsActive = true });
            _fixture.Store.KeyStore.SetUser("trener@example.cz", KeyEnvelope.Wrap(
                _fixture.Session.GetKey(), "Vlastni-heslo-9", TestStore.FastIterations, KeyBinding.ForUser("trener@example.cz")));
            _fixture.Auth.Logout();

            await _fixture.Auth.LoginAsync("trener@example.cz", "Vlastni-heslo-9");
            Assert.Null(await _fixture.Auth.IssueMissingRecoveryCodeAsync());   // běžný uživatel kód nikdy nedostane
            Assert.Null(_fixture.Store.KeyStore.GetRecovery());
        }

        [Fact]
        public async Task Deactivated_user_cannot_log_in_even_with_a_restored_key_entry()
        {
            await _fixture.SetUpAdminAsync();
            var user = await _fixture.CreateUserAsync("trener@example.cz", "Docasne-heslo-1");
            var oldEntry = _fixture.Store.KeyStore.FindUser("trener@example.cz");
            Assert.True((await _fixture.Users.DeactivateAsync(user.Id)).Succeeded);

            // Někdo vrátí do keys.json starý záznam (např. ze zálohy) – účet je v databázi stále neaktivní.
            _fixture.Store.KeyStore.SetUser("trener@example.cz", oldEntry);
            _fixture.Auth.Logout();

            var result = await _fixture.Auth.LoginAsync("trener@example.cz", "Docasne-heslo-1");

            Assert.Equal(LoginStatus.AccountInactive, result.Status);
            Assert.False(_fixture.Session.IsAuthenticated);
            Assert.Throws<InvalidOperationException>(() => _fixture.Session.GetKey());
        }
    }
}
