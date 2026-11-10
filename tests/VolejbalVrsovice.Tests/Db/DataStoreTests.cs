using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using VolejbalVrsovice.Core.Entities;
using VolejbalVrsovice.Core.Errors;
using VolejbalVrsovice.Core.Security;
using VolejbalVrsovice.Data.Db;
using VolejbalVrsovice.Data.Repositories;
using VolejbalVrsovice.Data.Security;
using VolejbalVrsovice.Tests.TestSupport;
using Xunit;

namespace VolejbalVrsovice.Tests.Db
{
    public class DataStoreTests
    {
        [Fact]
        public void Empty_folder_means_first_run()
        {
            using (var store = new TestStore())
            {
                Assert.Equal(DataStoreState.FirstRun, store.DataStore.Inspect());
            }
        }

        [Fact]
        public void First_run_creates_the_database_and_the_key_store()
        {
            using (var store = new TestStore())
            {
                store.DataStore.CreateNew();

                Assert.True(File.Exists(store.Paths.DatabaseFile));
                Assert.True(File.Exists(store.Paths.KeysFile));
                Assert.False(File.Exists(store.Paths.DatabaseFile + ".new"));
                Assert.Equal(DataStoreState.Ready, store.DataStore.Inspect());
                Assert.Equal(new[] { DefaultAdmin.Email }, store.KeyStore.GetUserEmails());
                Assert.Null(store.KeyStore.GetRecovery());
            }
        }

        [Fact]
        public async Task Default_admin_is_seeded_and_must_change_the_password()
        {
            using (var store = TestStore.CreateUnlocked())
            {
                var admin = await store.Users.GetByEmailAsync("admin@admin.cz");

                Assert.NotNull(admin);
                Assert.Equal(UserRole.Admin, admin.Role);
                Assert.True(admin.IsActive);
                Assert.True(admin.MustChangePassword);
                Assert.Null(admin.LastLoginAt);
                Assert.Null(admin.CreatedBy);
                Assert.Single(await store.Users.GetAllAsync());
            }
        }

        [Fact]
        public async Task Database_id_matches_the_key_store()
        {
            using (var store = TestStore.CreateUnlocked())
            {
                var dbId = await store.ScalarAsync<string>("SELECT Value FROM Meta WHERE \"Key\" = @key;", new { key = MetaKeys.DbId });
                var createdAt = await store.ScalarAsync<string>("SELECT Value FROM Meta WHERE \"Key\" = @key;", new { key = MetaKeys.CreatedAt });

                Assert.Equal(store.KeyStore.GetDbId(), Guid.Parse(dbId));
                Assert.Equal("2026-03-05T09:30:15Z", createdAt);
            }
        }

        /// <summary>Jediný test s produkčním počtem iterací PBKDF2 – proto trvá několik sekund.</summary>
        [Fact]
        public void Default_admin_key_is_wrapped_with_600_000_iterations()
        {
            using (var store = new TestStore(KdfSettings.Default.Iterations))
            {
                store.DataStore.CreateNew();

                var wrapped = store.KeyStore.FindUser(DefaultAdmin.Email);

                Assert.Equal(600000, wrapped.Iterations);
                Assert.Contains("\"iterations\": 600000", File.ReadAllText(store.Paths.KeysFile));
                Assert.True(KeyEnvelope.TryUnwrap(wrapped, "admin", KeyBinding.ForUser(DefaultAdmin.Email), out var key));
                store.Connections.Open(key).Dispose();
            }
        }

        [Fact]
        public void Database_cannot_be_read_without_the_key()
        {
            using (var store = TestStore.CreateUnlocked())
            {
                var connectionString = new SqliteConnectionStringBuilder
                {
                    DataSource = store.Paths.DatabaseFile,
                    Mode = SqliteOpenMode.ReadOnly,
                    Pooling = false,
                }.ToString();

                using (var connection = new SqliteConnection(connectionString))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText = "SELECT count(*) FROM sqlite_master;";

                    var error = Assert.Throws<SqliteException>(() => command.ExecuteScalar());
                    Assert.Equal(26, error.SqliteErrorCode);   // SQLITE_NOTADB – „file is not a database“
                }
            }
        }

        [Fact]
        public void Database_cannot_be_opened_with_a_different_key()
        {
            using (var store = TestStore.CreateUnlocked())
            {
                Assert.Throws<DatabaseKeyException>(() => store.Connections.Open(KeyEnvelope.GenerateKey()));
            }
        }

        [Fact]
        public async Task Database_file_contains_no_readable_data()
        {
            using (var store = TestStore.CreateUnlocked())
            {
                var team = await store.AddTeamAsync("Ženy A");
                await store.AddPlayerAsync("Bohumila", "Zelenáková", team);

                var bytes = File.ReadAllBytes(store.Paths.DatabaseFile);

                Assert.False(StartsWith(bytes, Encoding.ASCII.GetBytes("SQLite format 3")));
                foreach (var text in new[] { "Zelenáková", "Bohumila", "Ženy A", "admin@admin.cz", "CREATE TABLE", "Česko" })
                {
                    Assert.False(Contains(bytes, Encoding.UTF8.GetBytes(text)), "V souboru databáze je čitelný text: " + text);
                }
            }
        }

        [Theory]
        [InlineData(true)]    // existuje jen databáze
        [InlineData(false)]   // existuje jen soubor klíčů
        public void Missing_half_of_the_store_is_inconsistent_and_nothing_is_created_or_overwritten(bool keepDatabase)
        {
            using (var store = TestStore.CreateUnlocked())
            {
                var removed = keepDatabase ? store.Paths.KeysFile : store.Paths.DatabaseFile;
                var kept = keepDatabase ? store.Paths.DatabaseFile : store.Paths.KeysFile;
                File.Delete(removed);
                File.Delete(store.Paths.KeysFile + ".bak");
                var before = File.ReadAllBytes(kept);
                var filesBefore = ListDataFiles(store);

                Assert.Equal(DataStoreState.Inconsistent, store.DataStore.Inspect());
                Assert.Throws<DataStoreMismatchException>(() => store.DataStore.CreateNew());
                Assert.Throws<DataStoreMismatchException>(() => store.DataStore.Prepare(store.Key));

                Assert.False(File.Exists(removed));
                Assert.Equal(before, File.ReadAllBytes(kept));
                Assert.Equal(filesBefore, ListDataFiles(store));
            }
        }

        [Fact]
        public void Key_store_of_another_database_is_rejected_and_nothing_is_overwritten()
        {
            using (var store = TestStore.CreateUnlocked())
            using (var other = TestStore.CreateUnlocked())
            {
                // keys.json z jiné instalace: heslo „admin“ sedí, ale rozbalený klíč tuhle databázi neotevře.
                File.Copy(other.Paths.KeysFile, store.Paths.KeysFile, overwrite: true);
                var databaseBefore = File.ReadAllBytes(store.Paths.DatabaseFile);
                var keysBefore = File.ReadAllBytes(store.Paths.KeysFile);

                var foreignKey = store.Unlock();
                var error = Assert.Throws<DataStoreMismatchException>(() => store.DataStore.Prepare(foreignKey));

                Assert.Equal("Databáze a soubor klíčů k sobě nepatří nebo jeden chybí – obnovte je ze zálohy.", error.Message);
                Assert.Equal(databaseBefore, File.ReadAllBytes(store.Paths.DatabaseFile));
                Assert.Equal(keysBefore, File.ReadAllBytes(store.Paths.KeysFile));
                Assert.False(Directory.Exists(store.Paths.BackupDirectory));
            }
        }

        [Fact]
        public void Different_db_id_in_the_key_store_is_rejected_and_nothing_is_overwritten()
        {
            using (var store = TestStore.CreateUnlocked())
            {
                // Klíč databázi otevře, ale dbId v keys.json neodpovídá tabulce Meta.
                var json = File.ReadAllText(store.Paths.KeysFile)
                    .Replace(store.KeyStore.GetDbId().ToString("D"), "11111111-2222-3333-4444-555555555555");
                File.WriteAllText(store.Paths.KeysFile, json);
                var databaseBefore = File.ReadAllBytes(store.Paths.DatabaseFile);
                var keysBefore = File.ReadAllBytes(store.Paths.KeysFile);

                Assert.Throws<DataStoreMismatchException>(() => store.DataStore.Prepare(store.Key));

                Assert.Equal(databaseBefore, File.ReadAllBytes(store.Paths.DatabaseFile));
                Assert.Equal(keysBefore, File.ReadAllBytes(store.Paths.KeysFile));
                Assert.False(Directory.Exists(store.Paths.BackupDirectory));
            }
        }

        [Fact]
        public void Up_to_date_database_is_prepared_without_backup()
        {
            using (var store = TestStore.CreateUnlocked())
            {
                store.DataStore.Prepare(store.Key);

                Assert.False(Directory.Exists(store.Paths.BackupDirectory));
            }
        }

        [Fact]
        public async Task Pending_migration_is_applied_after_a_backup_of_database_and_keys()
        {
            using (var store = TestStore.CreateUnlocked())
            {
                await store.AddTeamAsync("Ženy A");
                var databaseBefore = File.ReadAllBytes(store.Paths.DatabaseFile);
                var keysBefore = File.ReadAllBytes(store.Paths.KeysFile);

                // „Novější verze aplikace“ = stejné migrace + jedna další.
                var newer = new Migrator(Migrator.LoadEmbedded().Concat(new[]
                {
                    new Migration(2, "Test", "CREATE TABLE TestMigrace (Id INTEGER PRIMARY KEY);"),
                }));
                store.CreateDataStore(newer).Prepare(store.Key);

                var backup = Assert.Single(Directory.GetDirectories(store.Paths.BackupDirectory));
                Assert.Matches(@"^\d{8}_\d{6}$", Path.GetFileName(backup));
                Assert.Equal(databaseBefore, File.ReadAllBytes(Path.Combine(backup, "volejbal.db")));
                Assert.Equal(keysBefore, File.ReadAllBytes(Path.Combine(backup, "keys.json")));

                Assert.Equal(2, await store.ScalarAsync<int>("PRAGMA user_version;"));
                Assert.Equal(1, await store.ScalarAsync<int>("SELECT count(*) FROM sqlite_master WHERE name = 'TestMigrace';"));
                Assert.Equal(1, await store.ScalarAsync<int>("SELECT count(*) FROM Team;"));   // data zůstala
            }
        }

        [Fact]
        public async Task Database_created_by_a_newer_application_is_rejected_untouched()
        {
            using (var store = TestStore.CreateUnlocked())
            {
                await store.ExecuteAsync("PRAGMA user_version = 99;");
                var databaseBefore = File.ReadAllBytes(store.Paths.DatabaseFile);

                var error = Assert.Throws<DatabaseNewerThanAppException>(() => store.DataStore.Prepare(store.Key));

                Assert.Equal(99, error.DatabaseVersion);
                Assert.Equal(databaseBefore, File.ReadAllBytes(store.Paths.DatabaseFile));
                Assert.False(Directory.Exists(store.Paths.BackupDirectory));
            }
        }

        [Fact]
        public void Leftover_of_an_interrupted_first_run_does_not_block_the_next_one()
        {
            using (var store = new TestStore())
            {
                Directory.CreateDirectory(store.Paths.DataDirectory);
                File.WriteAllText(store.Paths.DatabaseFile + ".new", "nedokončená databáze");

                Assert.Equal(DataStoreState.FirstRun, store.DataStore.Inspect());
                store.DataStore.CreateNew();

                store.Unlock();
                store.DataStore.Prepare(store.Key);
            }
        }

        /// <summary>
        /// Proces skončil mezi posledními dvěma kroky prvního spuštění: soubor klíčů už existuje,
        /// hotová databáze je ještě pod dočasným jménem. Příští start to má dotáhnout, ne odmítnout.
        /// </summary>
        [Fact]
        public async Task First_run_interrupted_before_the_final_rename_is_completed_on_the_next_start()
        {
            using (var store = new TestStore())
            {
                store.DataStore.CreateNew();
                File.Move(store.Paths.DatabaseFile, store.Paths.DatabaseFile + ".new");   // stav těsně před přejmenováním

                Assert.Equal(DataStoreState.Ready, store.DataStore.Inspect());

                Assert.True(File.Exists(store.Paths.DatabaseFile));
                Assert.False(File.Exists(store.Paths.DatabaseFile + ".new"));
                store.Unlock();
                store.DataStore.Prepare(store.Key);
                Assert.NotNull(await store.Users.GetByEmailAsync(DefaultAdmin.Email));
            }
        }

        [Fact]
        public void Leftover_temporary_database_never_replaces_an_existing_one()
        {
            using (var store = TestStore.CreateUnlocked())
            {
                var databaseBefore = File.ReadAllBytes(store.Paths.DatabaseFile);
                File.WriteAllText(store.Paths.DatabaseFile + ".new", "cizí soubor");

                Assert.Equal(DataStoreState.Ready, store.DataStore.Inspect());

                Assert.Equal(databaseBefore, File.ReadAllBytes(store.Paths.DatabaseFile));
                Assert.True(File.Exists(store.Paths.DatabaseFile + ".new"));
            }
        }

        private static string[] ListDataFiles(TestStore store)
        {
            return Directory.GetFileSystemEntries(store.Paths.DataDirectory, "*", SearchOption.AllDirectories)
                .Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
        }

        private static bool StartsWith(byte[] bytes, byte[] prefix)
        {
            return bytes.Length >= prefix.Length && bytes.Take(prefix.Length).SequenceEqual(prefix);
        }

        private static bool Contains(byte[] haystack, byte[] needle)
        {
            for (var i = 0; i <= haystack.Length - needle.Length; i++)
            {
                var match = true;
                for (var j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
