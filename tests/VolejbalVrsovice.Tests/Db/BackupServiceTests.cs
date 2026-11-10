using System;
using System.IO;
using System.Linq;
using VolejbalVrsovice.Data.Db;
using VolejbalVrsovice.Tests.TestSupport;
using Xunit;

namespace VolejbalVrsovice.Tests.Db
{
    public class BackupServiceTests
    {
        [Fact]
        public void Backup_contains_the_database_together_with_the_key_store()
        {
            using (var store = TestStore.CreateUnlocked())
            {
                var backup = store.Backups.CreateBackup();

                Assert.Equal(store.Paths.BackupDirectory, Path.GetDirectoryName(backup));
                Assert.Equal(store.Clock.Now.ToString("yyyyMMdd_HHmmss"), Path.GetFileName(backup));
                Assert.Equal(
                    new[] { "keys.json", "volejbal.db" },
                    Directory.GetFiles(backup).Select(Path.GetFileName).OrderBy(name => name).ToArray());
                Assert.Equal(File.ReadAllBytes(store.Paths.DatabaseFile), File.ReadAllBytes(Path.Combine(backup, "volejbal.db")));
                Assert.Equal(File.ReadAllBytes(store.Paths.KeysFile), File.ReadAllBytes(Path.Combine(backup, "keys.json")));
            }
        }

        [Fact]
        public void Backed_up_database_stays_encrypted_with_the_same_key()
        {
            using (var store = TestStore.CreateUnlocked())
            {
                var backup = store.Backups.CreateBackup();

                var restored = new SqliteConnectionFactory(Path.Combine(backup, "volejbal.db"));
                restored.Open(store.Key).Dispose();
            }
        }

        [Fact]
        public void Only_the_last_ten_backups_are_kept()
        {
            using (var store = TestStore.CreateUnlocked())
            {
                for (var i = 0; i < BackupService.KeepCount + 3; i++)
                {
                    store.Backups.CreateBackup();
                    store.Clock.Advance(TimeSpan.FromMinutes(1));
                }

                var names = Directory.GetDirectories(store.Paths.BackupDirectory)
                    .Select(Path.GetFileName)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToList();

                Assert.Equal(10, names.Count);

                // Zůstaly ty nejnovější – tři nejstarší jsou pryč.
                var first = new FakeClock().Now;
                Assert.Equal(first.AddMinutes(3).ToString("yyyyMMdd_HHmmss"), names.First());
                Assert.Equal(first.AddMinutes(12).ToString("yyyyMMdd_HHmmss"), names.Last());
            }
        }

        [Fact]
        public void Two_backups_in_the_same_second_do_not_collide()
        {
            using (var store = TestStore.CreateUnlocked())
            {
                var first = store.Backups.CreateBackup();
                var second = store.Backups.CreateBackup();

                Assert.NotEqual(first, second);
                Assert.True(File.Exists(Path.Combine(second, "volejbal.db")));
            }
        }

        [Fact]
        public void Unrelated_folders_in_the_backup_directory_are_left_alone()
        {
            using (var store = TestStore.CreateUnlocked())
            {
                var manual = Path.Combine(store.Paths.BackupDirectory, "ruční záloha před sezónou");
                Directory.CreateDirectory(manual);

                for (var i = 0; i < BackupService.KeepCount + 2; i++)
                {
                    store.Backups.CreateBackup();
                    store.Clock.Advance(TimeSpan.FromSeconds(1));
                }

                Assert.True(Directory.Exists(manual));
            }
        }
    }
}
