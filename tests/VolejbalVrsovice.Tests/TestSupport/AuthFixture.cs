using System;
using System.Threading.Tasks;
using Serilog.Core;
using VolejbalVrsovice.Core.Entities;
using VolejbalVrsovice.Core.Security;
using VolejbalVrsovice.Core.Services;
using VolejbalVrsovice.Data.Db;
using VolejbalVrsovice.Data.Repositories;
using VolejbalVrsovice.Data.Security;
using Xunit;

namespace VolejbalVrsovice.Tests.TestSupport
{
    /// <summary>Služby přihlášení a správy uživatelů nad dočasným úložištěm – zapojené stejně jako v aplikaci.</summary>
    internal sealed class AuthFixture : IDisposable
    {
        public const string AdminPassword = "Spravce-heslo-1";

        public AuthFixture(int iterations = TestStore.FastIterations)
        {
            Store = new TestStore(iterations);
            Session = new AppSession();
            Throttle = new LoginThrottle(Store.Clock);
            SessionDatabase = new Database(Store.Connections, Session);
            UserRepository = new AppUserRepository(SessionDatabase, Session, Store.Clock);
            Auth = CreateAuthService(Store.DataStore);
            Users = new UserService(Session, UserRepository, Store.KeyStore, Store.Kdf, Logger.None);
        }

        public TestStore Store { get; }

        public AppSession Session { get; }

        public LoginThrottle Throttle { get; }

        /// <summary>Databáze odemykaná klíčem relace – funguje jen po přihlášení.</summary>
        public Database SessionDatabase { get; }

        public AppUserRepository UserRepository { get; }

        public AuthService Auth { get; }

        public UserService Users { get; }

        public AuthService CreateAuthService(DataStore dataStore)
        {
            return new AuthService(
                dataStore, Store.KeyStore, Store.Connections, Session, UserRepository, Throttle, Store.Kdf, Store.Clock, Logger.None);
        }

        /// <summary>První spuštění + přihlášení výchozího správce + vynucená změna hesla. Vrací záchranný kód.</summary>
        public async Task<string> SetUpAdminAsync()
        {
            await Auth.InitializeAsync();

            var login = await Auth.LoginAsync(DefaultAdmin.Email, DefaultAdmin.Password);
            Assert.True(login.IsSuccess);

            var change = await Auth.CompleteForcedPasswordChangeAsync(AdminPassword);
            Assert.True(change.Succeeded, change.Error);
            Assert.NotNull(change.RecoveryCode);

            return change.RecoveryCode;
        }

        public Task<LoginResult> LoginAdminAsync()
        {
            return Auth.LoginAsync(DefaultAdmin.Email, AdminPassword);
        }

        /// <summary>Správce (musí být přihlášen) založí uživatele s dočasným heslem.</summary>
        public async Task<AppUser> CreateUserAsync(string email, string temporaryPassword, UserRole role = UserRole.User)
        {
            var result = await Users.CreateUserAsync(email, "Uživatel " + email, role, temporaryPassword);
            Assert.True(result.Succeeded, result.Error);
            return result.User;
        }

        /// <summary>Uživatel se přihlásí dočasným heslem a nastaví si vlastní; skončí přihlášený.</summary>
        public async Task ActivateUserAsync(string email, string temporaryPassword, string ownPassword)
        {
            Auth.Logout();

            var login = await Auth.LoginAsync(email, temporaryPassword);
            Assert.True(login.IsSuccess);

            var change = await Auth.CompleteForcedPasswordChangeAsync(ownPassword);
            Assert.True(change.Succeeded, change.Error);
        }

        public void Dispose()
        {
            Session.End();
            Store.Dispose();
        }
    }
}
