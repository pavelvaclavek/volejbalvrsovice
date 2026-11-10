using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;
using VolejbalVrsovice.Core.Errors;
using VolejbalVrsovice.Data.Db;
using VolejbalVrsovice.Tests.TestSupport;
using Xunit;

namespace VolejbalVrsovice.Tests.Db
{
    public class MigratorTests
    {
        [Fact]
        public void Embedded_migrations_are_numbered_contiguously_from_one()
        {
            var migrations = Migrator.LoadEmbedded().OrderBy(migration => migration.Version).ToList();

            Assert.NotEmpty(migrations);
            Assert.Equal(Enumerable.Range(1, migrations.Count), migrations.Select(migration => migration.Version));
            Assert.Equal("Init", migrations[0].Name);
            Assert.Equal(migrations.Count, new Migrator().LatestVersion);
        }

        [Fact]
        public void Gaps_in_numbering_are_rejected()
        {
            Assert.Throws<ArgumentException>(() => new Migrator(new[]
            {
                new Migration(1, "A", "SELECT 1;"),
                new Migration(3, "C", "SELECT 1;"),
            }));
        }

        [Fact]
        public async Task New_database_has_the_whole_schema()
        {
            using (var store = TestStore.CreateUnlocked())
            {
                var objects = await store.Database.ReadAsync(async connection =>
                    (await connection.QueryAsync<string>(
                        "SELECT name FROM sqlite_master WHERE type IN ('table', 'view') AND name NOT LIKE 'sqlite_%' ORDER BY name;"))
                    .ToList());

                Assert.Equal(
                    new[]
                    {
                        "AppUser", "AuditLog", "Contribution", "Country", "Event", "EventParticipant",
                        "Meta", "Player", "PlayerBalance", "Team", "TeamMembership", "TrainingSchedule",
                    },
                    objects);
                Assert.Equal(new Migrator().LatestVersion, await store.ScalarAsync<int>("PRAGMA user_version;"));
            }
        }

        [Fact]
        public async Task Applying_migrations_again_changes_nothing()
        {
            using (var store = TestStore.CreateUnlocked())
            {
                var schemaBefore = await DumpSchemaAsync(store);
                var usersBefore = await store.ScalarAsync<int>("SELECT count(*) FROM AppUser;");
                int appliedSecondTime;
                int appliedThirdTime;

                using (var connection = store.Connections.Open(store.Key))
                {
                    appliedSecondTime = store.Migrator.Apply(connection);
                    appliedThirdTime = store.Migrator.Apply(connection);
                    Assert.False(store.Migrator.HasPending(connection));
                }

                Assert.Equal(0, appliedSecondTime);
                Assert.Equal(0, appliedThirdTime);
                Assert.Equal(schemaBefore, await DumpSchemaAsync(store));
                Assert.Equal(usersBefore, await store.ScalarAsync<int>("SELECT count(*) FROM AppUser;"));
                Assert.Equal(249, await store.ScalarAsync<int>("SELECT count(*) FROM Country;"));
            }
        }

        [Fact]
        public async Task Failed_migration_is_rolled_back_completely()
        {
            using (var store = TestStore.CreateUnlocked())
            {
                var broken = new Migrator(Migrator.LoadEmbedded().Concat(new[]
                {
                    new Migration(2, "Rozbita", "CREATE TABLE Pulka (Id INTEGER PRIMARY KEY); INSERT INTO NeexistujiciTabulka VALUES (1);"),
                }));

                using (var connection = store.Connections.Open(store.Key))
                {
                    Assert.Throws<SqliteException>(() => broken.Apply(connection));
                }

                Assert.Equal(1, await store.ScalarAsync<int>("PRAGMA user_version;"));
                Assert.Equal(0, await store.ScalarAsync<int>("SELECT count(*) FROM sqlite_master WHERE name = 'Pulka';"));
            }
        }

        [Fact]
        public async Task Migrations_are_applied_in_order_and_each_bumps_the_version()
        {
            using (var store = TestStore.CreateUnlocked())
            {
                var newer = new Migrator(Migrator.LoadEmbedded().Concat(new[]
                {
                    new Migration(3, "Treti", "INSERT INTO Druha (Id) VALUES (3);"),
                    new Migration(2, "Druha", "CREATE TABLE Druha (Id INTEGER PRIMARY KEY);"),
                }));

                using (var connection = store.Connections.Open(store.Key))
                {
                    Assert.True(newer.HasPending(connection));
                    Assert.Equal(2, newer.Apply(connection));
                }

                Assert.Equal(3, await store.ScalarAsync<int>("PRAGMA user_version;"));
                Assert.Equal(3, await store.ScalarAsync<int>("SELECT Id FROM Druha;"));
            }
        }

        [Fact]
        public void Newer_schema_than_the_application_knows_is_refused()
        {
            using (var store = TestStore.CreateUnlocked())
            using (var connection = store.Connections.Open(store.Key))
            {
                connection.Execute("PRAGMA user_version = 7;");

                Assert.Throws<DatabaseNewerThanAppException>(() => store.Migrator.Apply(connection));
                Assert.Throws<DatabaseNewerThanAppException>(() => store.Migrator.HasPending(connection));
            }
        }

        [Fact]
        public async Task Connection_uses_delete_journal_and_enforces_foreign_keys()
        {
            using (var store = TestStore.CreateUnlocked())
            {
                Assert.Equal("delete", await store.ScalarAsync<string>("PRAGMA journal_mode;"));
                Assert.Equal(1, await store.ScalarAsync<int>("PRAGMA foreign_keys;"));
                Assert.False(File.Exists(store.Paths.DatabaseFile + "-wal"));
            }
        }

        [Fact]
        public async Task Country_list_is_seeded_in_czech()
        {
            using (var store = TestStore.CreateUnlocked())
            {
                var countries = await store.Countries.GetAllAsync();
                var names = countries.Select(country => country.Name).ToList();

                Assert.Equal(249, countries.Count);
                Assert.Equal(249, countries.Select(country => country.Code).Distinct().Count());
                Assert.Equal("Česko", countries.Single(country => country.Code == "CZ").Name);
                Assert.Equal("Slovensko", countries.Single(country => country.Code == "SK").Name);
                Assert.All(countries, country => Assert.Matches("^[A-Z]{2}$", country.Code));

                // České řazení: Č je za C a „Ch“ až za H.
                Assert.True(names.IndexOf("Curaçao") < names.IndexOf("Čad"));
                Assert.True(names.IndexOf("Čína") < names.IndexOf("Dánsko"));
                Assert.True(names.IndexOf("Hongkong") < names.IndexOf("Chile"));
                Assert.True(names.IndexOf("Chorvatsko") < names.IndexOf("Indie"));
            }
        }

        private static Task<List<string>> DumpSchemaAsync(TestStore store)
        {
            return store.Database.ReadAsync(async connection =>
                (await connection.QueryAsync<string>(
                    "SELECT type || ':' || name || ':' || COALESCE(sql, '') FROM sqlite_master ORDER BY type, name;"))
                .ToList());
        }
    }
}
