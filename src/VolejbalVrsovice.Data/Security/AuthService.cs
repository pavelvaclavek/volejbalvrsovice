using System;
using System.Threading.Tasks;
using Serilog;
using VolejbalVrsovice.Core;
using VolejbalVrsovice.Core.Entities;
using VolejbalVrsovice.Core.Errors;
using VolejbalVrsovice.Core.Repositories;
using VolejbalVrsovice.Core.Security;
using VolejbalVrsovice.Core.Services;
using VolejbalVrsovice.Data.Db;
using VolejbalVrsovice.Data.Repositories;

namespace VolejbalVrsovice.Data.Security
{
    /// <summary>
    /// Přihlášení a správa vlastního hesla podle zadání, sekce 5 a 5a.
    /// <para>
    /// Heslo se nikde neukládá: odemyká klíč databáze (DEK) uložený v <c>keys.json</c> zabalený klíčem
    /// odvozeným z hesla. Do protokolu se zapisují jen ID uživatelů – nikdy e-maily, hesla ani klíče.
    /// </para>
    /// <para>
    /// Zabalení klíče trvá několik sekund a běží mimo UI vlákno. Proto každá operace pracuje s vlastní
    /// kopií klíče a před zápisem do <c>keys.json</c> znovu ověří, že relace mezitím neskončila.
    /// </para>
    /// </summary>
    public sealed class AuthService : IAuthService
    {
        private readonly DataStore _dataStore;
        private readonly KeyStore _keyStore;
        private readonly SqliteConnectionFactory _connections;
        private readonly AppSession _session;
        private readonly IAppUserRepository _users;
        private readonly LoginThrottle _throttle;
        private readonly KdfSettings _kdf;
        private readonly IClock _clock;
        private readonly ILogger _logger;
        private readonly WrappedKey _decoy;
        private readonly KeyBinding _decoyBinding;

        public AuthService(
            DataStore dataStore,
            KeyStore keyStore,
            SqliteConnectionFactory connections,
            AppSession session,
            IAppUserRepository users,
            LoginThrottle throttle,
            KdfSettings kdf,
            IClock clock,
            ILogger logger)
        {
            _dataStore = dataStore ?? throw new ArgumentNullException(nameof(dataStore));
            _keyStore = keyStore ?? throw new ArgumentNullException(nameof(keyStore));
            _connections = connections ?? throw new ArgumentNullException(nameof(connections));
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _users = users ?? throw new ArgumentNullException(nameof(users));
            _throttle = throttle ?? throw new ArgumentNullException(nameof(throttle));
            _kdf = kdf ?? throw new ArgumentNullException(nameof(kdf));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Náhodný záznam pro neznámé e-maily – viz TryUnlock. Nejde jím nic odemknout.
            _decoyBinding = KeyBinding.ForUser(Guid.NewGuid().ToString("N") + "@invalid");
            var decoy = KeyEnvelope.Wrap(KeyEnvelope.GenerateKey(), Guid.NewGuid().ToString("N"), 1, _decoyBinding);
            _decoy = new WrappedKey(decoy.Salt, _kdf.Iterations, decoy.Iv, decoy.CipherText, decoy.Mac);
        }

        public Task InitializeAsync()
        {
            return Task.Run(() =>
            {
                switch (_dataStore.Inspect())
                {
                    case DataStoreState.FirstRun:
                        _dataStore.CreateNew();
                        break;

                    case DataStoreState.Inconsistent:
                        throw new DataStoreMismatchException();
                }
            });
        }

        public async Task<LoginResult> LoginAsync(string email, string password)
        {
            var lockout = _throttle.GetRemainingLockout();
            if (lockout.HasValue)
            {
                return LoginResult.LockedOut(lockout.Value);
            }

            var normalizedEmail = EmailAddress.Normalize(email);

            // 1 + 2) Najít záznam, odvodit KEK, ověřit HMAC a dešifrovat klíč databáze.
            var key = await Task.Run(() => TryUnlock(normalizedEmail, password)).ConfigureAwait(false);
            if (key == null)
            {
                return RegisterFailedLogin();
            }

            try
            {
                // 3) Otevřít databázi: patří k sobě s keys.json? Případně záloha a migrace schématu.
                await Task.Run(() => _dataStore.Prepare(key)).ConfigureAwait(false);

                var users = CreateUserRepository(key, actingUserId: null);
                var user = await users.GetByEmailAsync(normalizedEmail).ConfigureAwait(false);
                if (user == null)
                {
                    // Záznam v keys.json bez uživatele v databázi – navenek stejné jako špatné heslo.
                    return RegisterFailedLogin();
                }

                if (!user.IsActive)
                {
                    _logger.Warning("Odmítnuto přihlášení deaktivovaného uživatele {UserId}", user.Id);
                    return LoginResult.AccountInactive();
                }

                await users.RecordLoginAsync(user.Id).ConfigureAwait(false);
                user = await users.GetByIdAsync(user.Id).ConfigureAwait(false);

                // 4) Klíč od teď drží relace (v paměti, do odhlášení).
                _session.Start(user, key);
                key = null;

                _throttle.RegisterSuccess();
                _logger.Information("Uživatel {UserId} přihlášen", user.Id);
                return LoginResult.Success(user);
            }
            finally
            {
                KeyEnvelope.Clear(key);
            }
        }

        public void Logout()
        {
            var userId = _session.UserId;
            _session.End();

            if (userId.HasValue)
            {
                _logger.Information("Uživatel {UserId} odhlášen", userId.Value);
            }
        }

        public async Task<PasswordChangeResult> CompleteForcedPasswordChangeAsync(string newPassword)
        {
            var user = RequireUser();

            var policyError = PasswordPolicy.Validate(newPassword);
            if (policyError != null)
            {
                return PasswordChangeResult.Failure(policyError);
            }

            // Dosavadní (dočasné) heslo tu k dispozici není a nemá být – jestli je nové heslo stejné,
            // se pozná podle toho, že rozbalí dosavadní záznam.
            var current = _keyStore.FindUser(user.Email);
            if (current != null && await Task.Run(() => Matches(current, newPassword, user.Email)).ConfigureAwait(false))
            {
                return PasswordChangeResult.Failure("Nové heslo se musí lišit od dosavadního.");
            }

            return await SetOwnPasswordAsync(user, newPassword, issueRecoveryCodeIfMissing: true).ConfigureAwait(false);
        }

        public async Task<PasswordChangeResult> ChangeOwnPasswordAsync(string currentPassword, string newPassword)
        {
            var user = RequireUser();

            var policyError = PasswordPolicy.Validate(newPassword);
            if (policyError != null)
            {
                return PasswordChangeResult.Failure(policyError);
            }

            if (string.Equals(currentPassword, newPassword, StringComparison.Ordinal))
            {
                return PasswordChangeResult.Failure("Nové heslo se musí lišit od současného.");
            }

            var current = _keyStore.FindUser(user.Email);
            if (current == null || !await Task.Run(() => Matches(current, currentPassword, user.Email)).ConfigureAwait(false))
            {
                _logger.Warning("Uživatel {UserId}: změna hesla odmítnuta, současné heslo nesouhlasí", user.Id);
                return PasswordChangeResult.Failure("Současné heslo není správné.");
            }

            return await SetOwnPasswordAsync(user, newPassword, issueRecoveryCodeIfMissing: false).ConfigureAwait(false);
        }

        public async Task<string> IssueMissingRecoveryCodeAsync()
        {
            var user = _session.User;

            // Kód smí dostat jen správce, který už má vlastní heslo – kdo kód má, může nastavit heslo správci.
            if (user == null || !user.IsAdmin || !user.IsActive || user.MustChangePassword || _keyStore.GetRecovery() != null)
            {
                return null;
            }

            var key = _session.GetKey();
            try
            {
                return await IssueRecoveryCodeAsync(user, key).ConfigureAwait(false);
            }
            finally
            {
                KeyEnvelope.Clear(key);
            }
        }

        public async Task<OperationResult> RecoverAsync(string recoveryCode, string adminEmail, string newPassword)
        {
            var code = RecoveryCode.Normalize(recoveryCode);
            if (code == null)
            {
                return OperationResult.Failure("Záchranný kód nemá platný tvar – má 24 znaků ve skupinách po čtyřech.");
            }

            var email = EmailAddress.Normalize(adminEmail);
            if (email.Length == 0)
            {
                return OperationResult.Failure("Zadejte e-mail správce.");
            }

            var policyError = PasswordPolicy.Validate(newPassword);
            if (policyError != null)
            {
                return OperationResult.Failure(policyError);
            }

            var recovery = _keyStore.GetRecovery();
            byte[] key = null;
            if (recovery == null
                || !await Task.Run(() => KeyEnvelope.TryUnwrap(recovery, code, KeyBinding.Recovery, out key)).ConfigureAwait(false))
            {
                _logger.Warning("Obnova přístupu odmítnuta: záchranný kód nesouhlasí");
                return OperationResult.Failure("Záchranný kód není platný.");
            }

            try
            {
                await Task.Run(() => _dataStore.Prepare(key)).ConfigureAwait(false);

                var admin = await CreateUserRepository(key, actingUserId: null).GetByEmailAsync(email).ConfigureAwait(false);
                if (admin == null || !admin.IsActive || !admin.IsAdmin)
                {
                    _logger.Warning("Obnova přístupu odmítnuta: zadaný e-mail nepatří aktivnímu správci");
                    return OperationResult.Failure("Zadaný e-mail nepatří aktivnímu správci.");
                }

                var wrapped = await Task.Run(
                    () => KeyEnvelope.Wrap(key, newPassword, _kdf.Iterations, KeyBinding.ForUser(admin.Email))).ConfigureAwait(false);
                _keyStore.SetUser(admin.Email, wrapped);

                if (admin.MustChangePassword)
                {
                    admin.MustChangePassword = false;
                    await CreateUserRepository(key, admin.Id).UpdateAsync(admin).ConfigureAwait(false);
                }

                _throttle.RegisterSuccess();
                _logger.Information("Heslo správce {UserId} bylo nastaveno pomocí záchranného kódu", admin.Id);
                return OperationResult.Success();
            }
            finally
            {
                KeyEnvelope.Clear(key);
            }
        }

        /// <summary>
        /// Vygeneruje záchranný kód a zabalí jím klíč databáze. Výsledek ještě nikam neukládá –
        /// volající nejdřív ověří, že relace trvá, a teprve potom záznam zapíše.
        /// </summary>
        internal static async Task<Tuple<string, WrappedKey>> CreateRecoveryCodeAsync(byte[] key, KdfSettings kdf)
        {
            var code = RecoveryCode.Generate();
            var secret = RecoveryCode.Normalize(code);
            var wrapped = await Task.Run(() => KeyEnvelope.Wrap(key, secret, kdf.Iterations, KeyBinding.Recovery)).ConfigureAwait(false);

            return Tuple.Create(code, wrapped);
        }

        /// <summary>Vrátí kód k jednorázovému zobrazení – nikde jinde se neukládá.</summary>
        private async Task<string> IssueRecoveryCodeAsync(AppUser admin, byte[] key)
        {
            var recovery = await CreateRecoveryCodeAsync(key, _kdf).ConfigureAwait(false);

            EnsureStillLoggedIn(admin);
            _keyStore.SetRecovery(recovery.Item2);

            _logger.Information("Vygenerován záchranný kód (správce {UserId})", admin.Id);
            return recovery.Item1;
        }

        private async Task<PasswordChangeResult> SetOwnPasswordAsync(AppUser user, string newPassword, bool issueRecoveryCodeIfMissing)
        {
            var key = _session.GetKey();
            try
            {
                var wrapped = await Task.Run(
                    () => KeyEnvelope.Wrap(key, newPassword, _kdf.Iterations, KeyBinding.ForUser(user.Email))).ConfigureAwait(false);

                // Zabalení trvá několik sekund – relace mezitím mohla skončit. Pak se nesmí nic uložit.
                EnsureStillLoggedIn(user);

                // Pořadí je důležité: nejdřív soubor klíčů, potom příznak v databázi. Kdyby aplikace mezi kroky
                // spadla, bude uživatel nanejvýš znovu vyzván ke změně hesla. Opačné pořadí by nechalo
                // v platnosti dočasné heslo bez vynucené změny.
                _keyStore.SetUser(user.Email, wrapped);

                if (user.MustChangePassword)
                {
                    user.MustChangePassword = false;
                    await _users.UpdateAsync(user).ConfigureAwait(false);
                    _session.Refresh(user);
                }

                _logger.Information("Uživatel {UserId} si změnil heslo", user.Id);

                // Záchranný kód vzniká při první (vynucené) změně výchozího hesla. Smí ho dostat jen správce –
                // kdo ho má, může nastavit heslo správci. Kdyby aplikace spadla právě tady, vydá se kód
                // při příštím přihlášení správce (IssueMissingRecoveryCodeAsync).
                string recoveryCode = null;
                if (issueRecoveryCodeIfMissing && user.IsAdmin && _keyStore.GetRecovery() == null)
                {
                    recoveryCode = await IssueRecoveryCodeAsync(user, key).ConfigureAwait(false);
                }

                return PasswordChangeResult.Changed(recoveryCode);
            }
            finally
            {
                KeyEnvelope.Clear(key);
            }
        }

        /// <summary>Vrátí rozbalený klíč databáze, nebo <c>null</c> při neznámém e-mailu či špatném hesle.</summary>
        private byte[] TryUnlock(string normalizedEmail, string password)
        {
            var wrapped = normalizedEmail.Length == 0 ? null : _keyStore.FindUser(normalizedEmail);
            if (wrapped == null)
            {
                // Neznámý e-mail: provést stejně drahý výpočet jako u existujícího účtu, aby se podle doby
                // odpovědi nedalo poznat, zda e-mail v aplikaci existuje.
                KeyEnvelope.TryUnwrap(_decoy, password, _decoyBinding, out _);
                return null;
            }

            return KeyEnvelope.TryUnwrap(wrapped, password, KeyBinding.ForUser(normalizedEmail), out var key) ? key : null;
        }

        private static bool Matches(WrappedKey wrapped, string password, string email)
        {
            var matches = KeyEnvelope.TryUnwrap(wrapped, password, KeyBinding.ForUser(email), out var key);
            KeyEnvelope.Clear(key);
            return matches;
        }

        private LoginResult RegisterFailedLogin()
        {
            if (_throttle.RegisterFailure())
            {
                _logger.Warning(
                    "Přihlášení zablokováno na {Minutes} minut po {Attempts} neúspěšných pokusech",
                    LoginThrottle.LockoutDuration.TotalMinutes, LoginThrottle.MaxFailedAttempts);
                return LoginResult.LockedOut(LoginThrottle.LockoutDuration);
            }

            _logger.Warning(
                "Neúspěšný pokus o přihlášení ({Attempt}/{Max})", _throttle.FailedAttempts, LoginThrottle.MaxFailedAttempts);
            return LoginResult.InvalidCredentials();
        }

        private AppUser RequireUser()
        {
            var user = _session.User;
            if (user == null)
            {
                throw new InvalidOperationException("Nikdo není přihlášen.");
            }

            return user;
        }

        /// <summary>Po dlouhém výpočtu ověří, že je pořád přihlášen tentýž uživatel.</summary>
        private void EnsureStillLoggedIn(AppUser user)
        {
            if (_session.UserId != user.Id)
            {
                throw new InvalidOperationException("Relace byla mezitím ukončena – změna nebyla uložena.");
            }
        }

        /// <summary>Repozitář uživatelů nad databází odemčenou daným klíčem – ještě mimo relaci.</summary>
        private AppUserRepository CreateUserRepository(byte[] key, int? actingUserId)
        {
            return new AppUserRepository(
                new Database(_connections, new FixedKeySource(key)), new FixedCurrentUser(actingUserId), _clock);
        }
    }
}
