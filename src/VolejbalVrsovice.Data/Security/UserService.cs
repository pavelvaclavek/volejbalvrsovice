using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Serilog;
using VolejbalVrsovice.Core;
using VolejbalVrsovice.Core.Entities;
using VolejbalVrsovice.Core.Repositories;
using VolejbalVrsovice.Core.Security;
using VolejbalVrsovice.Core.Services;

namespace VolejbalVrsovice.Data.Security
{
    /// <summary>
    /// Správa uživatelů aplikace (zadání, sekce 5 a 5a). Správce je přihlášen, má tedy klíč databáze
    /// a může ho zabalit dočasným heslem jiného uživatele – přešifrování databáze není potřeba.
    /// <para>
    /// Zabalení klíče trvá několik sekund a běží mimo UI vlákno: pracuje se s kopií klíče a před
    /// zápisem do <c>keys.json</c> se znovu ověří, že je správce pořád přihlášen.
    /// </para>
    /// </summary>
    public sealed class UserService : IUserService
    {
        private const int MaxDisplayNameLength = 100;

        private readonly AppSession _session;
        private readonly IAppUserRepository _users;
        private readonly KeyStore _keyStore;
        private readonly KdfSettings _kdf;
        private readonly ILogger _logger;

        public UserService(AppSession session, IAppUserRepository users, KeyStore keyStore, KdfSettings kdf, ILogger logger)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _users = users ?? throw new ArgumentNullException(nameof(users));
            _keyStore = keyStore ?? throw new ArgumentNullException(nameof(keyStore));
            _kdf = kdf ?? throw new ArgumentNullException(nameof(kdf));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task<IReadOnlyList<AppUser>> GetUsersAsync()
        {
            RequireAdmin();
            return _users.GetAllAsync();
        }

        public async Task<CreateUserResult> CreateUserAsync(string email, string displayName, UserRole role, string temporaryPassword)
        {
            var admin = RequireAdmin();

            var normalizedEmail = EmailAddress.Normalize(email);
            if (!EmailAddress.IsValid(normalizedEmail))
            {
                return CreateUserResult.Failure("Zadejte platný e-mail.");
            }

            var name = (displayName ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                return CreateUserResult.Failure("Zadejte jméno uživatele.");
            }

            if (name.Length > MaxDisplayNameLength)
            {
                return CreateUserResult.Failure("Jméno může mít nejvýše " + MaxDisplayNameLength + " znaků.");
            }

            if (role != UserRole.Admin && role != UserRole.User)
            {
                return CreateUserResult.Failure("Vyberte roli uživatele.");
            }

            var policyError = PasswordPolicy.Validate(temporaryPassword);
            if (policyError != null)
            {
                return CreateUserResult.Failure(policyError);
            }

            var existing = await _users.GetByEmailAsync(normalizedEmail).ConfigureAwait(false);
            if (existing != null)
            {
                return CreateUserResult.Failure(existing.IsActive
                    ? "Uživatel s tímto e-mailem už existuje."
                    : "Uživatel s tímto e-mailem už existuje (je deaktivovaný).");
            }

            var wrapped = await WrapSessionKeyAsync(admin, temporaryPassword, normalizedEmail).ConfigureAwait(false);

            var user = new AppUser
            {
                Email = normalizedEmail,
                DisplayName = name,
                Role = role,
                IsActive = true,
                MustChangePassword = true,
            };

            // Nejdřív databáze (hlídá unikátní e-mail), potom soubor klíčů. Kdyby zápis klíče selhal,
            // uživatel existuje, ale nepřihlásí se – správce to napraví resetem hesla.
            await _users.InsertAsync(user).ConfigureAwait(false);
            _keyStore.SetUser(user.Email, wrapped);

            _logger.Information("Správce {AdminId} založil uživatele {UserId} (role {Role})", admin.Id, user.Id, user.Role);
            return CreateUserResult.Created(user);
        }

        public async Task<OperationResult> ResetPasswordAsync(int userId, string temporaryPassword)
        {
            var admin = RequireAdmin();

            if (userId == admin.Id)
            {
                // Vlastní heslo se mění přes „Změnit heslo“ (se znalostí současného hesla). Reset sebe sama
                // by přihlášeného správce uprostřed práce převedl do stavu „musí změnit heslo“.
                return OperationResult.Failure("Vlastní heslo nelze resetovat – použijte „Změnit heslo“.");
            }

            var user = await _users.GetByIdAsync(userId).ConfigureAwait(false);
            if (user == null)
            {
                return OperationResult.Failure("Uživatel neexistuje.");
            }

            if (!user.IsActive)
            {
                return OperationResult.Failure("Deaktivovanému uživateli nelze nastavit heslo.");
            }

            var policyError = PasswordPolicy.Validate(temporaryPassword);
            if (policyError != null)
            {
                return OperationResult.Failure(policyError);
            }

            var wrapped = await WrapSessionKeyAsync(admin, temporaryPassword, user.Email).ConfigureAwait(false);

            // Nejdřív příznak v databázi, potom nový klíč: dokud nové heslo neplatí, je vynucená změna
            // u starého hesla neškodná; opačné pořadí by při pádu nechalo dočasné heslo bez vynucené změny.
            user.MustChangePassword = true;
            await _users.UpdateAsync(user).ConfigureAwait(false);
            _keyStore.SetUser(user.Email, wrapped);

            _logger.Information("Správce {AdminId} nastavil dočasné heslo uživateli {UserId}", admin.Id, user.Id);
            return OperationResult.Success();
        }

        public async Task<OperationResult> DeactivateAsync(int userId)
        {
            var admin = RequireAdmin();

            var user = await _users.GetByIdAsync(userId).ConfigureAwait(false);
            if (user == null)
            {
                return OperationResult.Failure("Uživatel neexistuje.");
            }

            if (!user.IsActive)
            {
                // Už deaktivovaný: jen pro jistotu uklidit případný zbylý klíč.
                _keyStore.RemoveUser(user.Email);
                return OperationResult.Success();
            }

            if (user.IsAdmin && await _users.CountActiveAdminsAsync().ConfigureAwait(false) <= 1)
            {
                return OperationResult.Failure("Posledního aktivního správce nelze deaktivovat.");
            }

            // Nejdřív odebrat klíč (tím okamžitě končí možnost přihlášení), potom příznak v databázi.
            _keyStore.RemoveUser(user.Email);

            user.IsActive = false;
            await _users.UpdateAsync(user).ConfigureAwait(false);

            _logger.Information("Správce {AdminId} deaktivoval uživatele {UserId}", admin.Id, user.Id);
            return OperationResult.Success();
        }

        public async Task<string> GenerateRecoveryCodeAsync()
        {
            var admin = RequireAdmin();

            var key = _session.GetKey();
            try
            {
                var recovery = await AuthService.CreateRecoveryCodeAsync(key, _kdf).ConfigureAwait(false);

                // Dosavadní kód se smí nahradit jen tehdy, když je správce pořád přihlášen a nový kód uvidí.
                EnsureStillLoggedIn(admin);
                _keyStore.SetRecovery(recovery.Item2);

                _logger.Information("Vygenerován nový záchranný kód (správce {AdminId})", admin.Id);
                return recovery.Item1;
            }
            finally
            {
                KeyEnvelope.Clear(key);
            }
        }

        /// <summary>Zabalí klíč databáze dočasným heslem uživatele s daným e-mailem.</summary>
        private async Task<WrappedKey> WrapSessionKeyAsync(AppUser admin, string password, string email)
        {
            var key = _session.GetKey();
            try
            {
                var wrapped = await Task.Run(
                    () => KeyEnvelope.Wrap(key, password, _kdf.Iterations, KeyBinding.ForUser(email))).ConfigureAwait(false);

                // Zabalení trvá několik sekund – správce se mezitím mohl odhlásit. Pak se nesmí nic uložit.
                EnsureStillLoggedIn(admin);
                return wrapped;
            }
            finally
            {
                KeyEnvelope.Clear(key);
            }
        }

        private void EnsureStillLoggedIn(AppUser admin)
        {
            if (_session.UserId != admin.Id)
            {
                throw new InvalidOperationException("Relace byla mezitím ukončena – změna nebyla uložena.");
            }
        }

        private AppUser RequireAdmin()
        {
            var user = _session.User;
            if (user == null || !user.IsActive || !user.IsAdmin)
            {
                throw new UnauthorizedAccessException("Správu uživatelů smí provádět jen přihlášený správce.");
            }

            if (user.MustChangePassword)
            {
                // Dokud správce nezmění dočasné (výchozí) heslo, není přístupné nic jiného.
                throw new UnauthorizedAccessException("Před správou uživatelů je nutné změnit heslo.");
            }

            return user;
        }
    }
}
