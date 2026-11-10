using System;
using System.IO;
using Dapper;
using Microsoft.Data.Sqlite;
using Serilog;
using VolejbalVrsovice.Core.Entities;
using VolejbalVrsovice.Core.Errors;
using VolejbalVrsovice.Core.Security;
using VolejbalVrsovice.Core.Services;
using VolejbalVrsovice.Data.Repositories;
using VolejbalVrsovice.Data.Security;
using VolejbalVrsovice.Data.Storage;

namespace VolejbalVrsovice.Data.Db
{
    /// <summary>Stav úložiště zjištěný při startu – jen podle existence souborů (bez hesla nic víc zjistit nejde).</summary>
    public enum DataStoreState
    {
        /// <summary>Chybí databáze i soubor klíčů – první spuštění, obojí se vytvoří.</summary>
        FirstRun,

        /// <summary>Existuje databáze i soubor klíčů. Že k sobě patří, se ověří až po odemčení.</summary>
        Ready,

        /// <summary>Existuje jen jeden ze souborů – aplikace nesmí nic vytvořit ani přepsat.</summary>
        Inconsistent,
    }

    /// <summary>
    /// Úložiště dat = šifrovaná databáze <c>volejbal.db</c> + soubor klíčů <c>keys.json</c>.
    /// Řeší rozhodovací tabulku při startu (zadání, sekce 5a), první vytvoření a přípravu po odemčení
    /// (kontrola, že soubory patří k sobě, záloha a migrace schématu).
    /// </summary>
    public sealed class DataStore
    {
        private readonly AppPaths _paths;
        private readonly KeyStore _keyStore;
        private readonly SqliteConnectionFactory _connections;
        private readonly Migrator _migrator;
        private readonly BackupService _backups;
        private readonly IClock _clock;
        private readonly KdfSettings _kdf;
        private readonly ILogger _logger;

        public DataStore(
            AppPaths paths,
            KeyStore keyStore,
            SqliteConnectionFactory connections,
            Migrator migrator,
            BackupService backups,
            IClock clock,
            KdfSettings kdf,
            ILogger logger)
        {
            _paths = paths ?? throw new ArgumentNullException(nameof(paths));
            _keyStore = keyStore ?? throw new ArgumentNullException(nameof(keyStore));
            _connections = connections ?? throw new ArgumentNullException(nameof(connections));
            _migrator = migrator ?? throw new ArgumentNullException(nameof(migrator));
            _backups = backups ?? throw new ArgumentNullException(nameof(backups));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _kdf = kdf ?? throw new ArgumentNullException(nameof(kdf));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>Databáze během prvního vytváření – než je hotová i se souborem klíčů.</summary>
        private string TemporaryDatabaseFile
        {
            get { return _paths.DatabaseFile + ".new"; }
        }

        public DataStoreState Inspect()
        {
            CompleteInterruptedFirstRun();

            var hasDatabase = File.Exists(_paths.DatabaseFile);
            var hasKeys = _keyStore.Exists;

            if (!hasDatabase && !hasKeys)
            {
                return DataStoreState.FirstRun;
            }

            return hasDatabase && hasKeys ? DataStoreState.Ready : DataStoreState.Inconsistent;
        }

        /// <summary>
        /// První spuštění: vygeneruje klíč databáze, vytvoří šifrovanou databázi (migrace, číselníky,
        /// výchozí správce) a soubor klíčů se záznamem výchozího správce. Klíč se po zabalení heslem
        /// z paměti smaže – k databázi se dál dostane jen ten, kdo zná heslo.
        /// </summary>
        /// <exception cref="DataStoreMismatchException">Některý ze souborů už existuje – nic se nepřepíše.</exception>
        public void CreateNew()
        {
            if (Inspect() != DataStoreState.FirstRun)
            {
                throw new DataStoreMismatchException();
            }

            Directory.CreateDirectory(_paths.DataDirectory);

            // Databáze vzniká pod dočasným jménem. Ať proces skončí v kterémkoli okamžiku (uživatel zavře
            // okno během přípravy, výpadek proudu), příští start to umí dotáhnout nebo začít znovu:
            //   jen volejbal.db.new               → první spuštění se zopakuje (soubor se zahodí),
            //   keys.json + volejbal.db.new       → chybí už jen přejmenování, viz CompleteInterruptedFirstRun.
            var temporaryFile = TemporaryDatabaseFile;
            DeleteIfExists(temporaryFile);
            DeleteIfExists(temporaryFile + "-journal");
            DeleteIfExists(_keyStore.FilePath + ".tmp");

            var key = KeyEnvelope.GenerateKey();
            try
            {
                var dbId = Guid.NewGuid();
                var now = DbFormats.TruncateToSeconds(_clock.UtcNow.ToUniversalTime());

                using (var connection = SqliteConnectionFactory.OpenFile(temporaryFile, key, createIfMissing: true))
                {
                    _migrator.Apply(connection);
                    Seed(connection, dbId, now);
                }

                var wrapped = KeyEnvelope.Wrap(
                    key, DefaultAdmin.Password, _kdf.Iterations, KeyBinding.ForUser(DefaultAdmin.Email));

                // Pořadí: 1) soubor klíčů (databáze je v tu chvíli už celá a zavřená), 2) přejmenování databáze.
                _keyStore.Create(dbId, DefaultAdmin.Email, wrapped);
                File.Move(temporaryFile, _paths.DatabaseFile);

                _logger.Information("Vytvořena nová databáze (schéma verze {Version})", _migrator.LatestVersion);
            }
            finally
            {
                KeyEnvelope.Clear(key);
            }
        }

        /// <summary>
        /// Dotáhne první spuštění přerušené mezi posledními dvěma kroky: soubor klíčů už existuje,
        /// hotová databáze ještě čeká pod dočasným jménem. Soubor klíčů vzniká až po dokončení
        /// a zavření databáze, takže ji stačí přejmenovat – nic se tím nepřepisuje.
        /// </summary>
        private void CompleteInterruptedFirstRun()
        {
            if (_keyStore.Exists && !File.Exists(_paths.DatabaseFile) && File.Exists(TemporaryDatabaseFile))
            {
                File.Move(TemporaryDatabaseFile, _paths.DatabaseFile);
                _logger.Information("Dokončeno první spuštění přerušené před přejmenováním databáze");
            }
        }

        /// <summary>
        /// Příprava po odemčení: ověří, že klíč databázi otevře a že <c>dbId</c> v <c>keys.json</c>
        /// odpovídá tabulce <c>Meta</c>; čekají-li migrace, nejdřív udělá zálohu a pak je aplikuje.
        /// </summary>
        /// <exception cref="DataStoreMismatchException">Databáze a soubor klíčů k sobě nepatří nebo jeden chybí.</exception>
        /// <exception cref="DatabaseNewerThanAppException">Databázi vytvořila novější verze aplikace.</exception>
        public void Prepare(byte[] key)
        {
            if (Inspect() != DataStoreState.Ready)
            {
                throw new DataStoreMismatchException();
            }

            var expectedDbId = _keyStore.GetDbId();
            bool hasPendingMigrations;

            try
            {
                using (var connection = _connections.Open(key))
                {
                    var actualDbId = ReadDbId(connection);
                    if (!actualDbId.HasValue || actualDbId.Value != expectedDbId)
                    {
                        throw new DataStoreMismatchException();
                    }

                    hasPendingMigrations = _migrator.HasPending(connection);
                }
            }
            catch (DatabaseKeyException ex)
            {
                throw new DataStoreMismatchException(ex);
            }

            if (!hasPendingMigrations)
            {
                return;
            }

            // Žádné spojení teď není otevřené – soubor databáze je konzistentní a lze ho zkopírovat.
            var backup = _backups.CreateBackup();
            _logger.Information("Záloha před migrací schématu: {Backup}", Path.GetFileName(backup));

            using (var connection = _connections.Open(key))
            {
                var applied = _migrator.Apply(connection);
                _logger.Information("Aplikováno migrací: {Count}, schéma verze {Version}", applied, _migrator.LatestVersion);
            }
        }

        private static void Seed(SqliteConnection connection, Guid dbId, DateTime now)
        {
            var admin = new AppUser
            {
                Email = DefaultAdmin.Email,
                DisplayName = DefaultAdmin.DisplayName,
                Role = UserRole.Admin,
                IsActive = true,
                MustChangePassword = true,
                CreatedAt = now,
                ModifiedAt = now,
            };

            using (var transaction = connection.BeginTransaction())
            {
                connection.Execute(MetaSql.Upsert, new { key = MetaKeys.DbId, value = dbId.ToString("D") }, transaction);
                connection.Execute(MetaSql.Upsert, new { key = MetaKeys.CreatedAt, value = DbFormats.FormatDateTime(now) }, transaction);
                connection.ExecuteScalar<long>(AppUserRepository.InsertSql, admin, transaction);
                transaction.Commit();
            }
        }

        /// <summary>Přečte <c>DbId</c> z tabulky <c>Meta</c>; <c>null</c>, pokud tam (platný) není.</summary>
        private static Guid? ReadDbId(SqliteConnection connection)
        {
            if (connection.ExecuteScalar<long>(MetaSql.TableExists) == 0)
            {
                return null;
            }

            var value = connection.ExecuteScalar<string>(MetaSql.Select, new { key = MetaKeys.DbId });
            return Guid.TryParseExact(value, "D", out var dbId) ? dbId : (Guid?)null;
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
