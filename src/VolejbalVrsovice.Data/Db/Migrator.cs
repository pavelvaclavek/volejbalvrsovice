using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using VolejbalVrsovice.Core.Errors;

namespace VolejbalVrsovice.Data.Db
{
    /// <summary>Jeden migrační skript; <see cref="Version"/> = hodnota <c>PRAGMA user_version</c> po jeho aplikaci.</summary>
    public sealed class Migration
    {
        public Migration(int version, string name, string sql)
        {
            if (version < 1) throw new ArgumentOutOfRangeException(nameof(version));
            if (string.IsNullOrWhiteSpace(sql)) throw new ArgumentException("Migrační skript nesmí být prázdný.", nameof(sql));

            Version = version;
            Name = name;
            Sql = sql;
        }

        public int Version { get; }

        public string Name { get; }

        public string Sql { get; }
    }

    /// <summary>
    /// Migrace schématu. Skripty jsou embedded resources <c>Migrations/NNN_Nazev.sql</c>, verze schématu
    /// je v <c>PRAGMA user_version</c>. Každý skript běží ve vlastní transakci spolu se zvýšením verze –
    /// buď se provede celý, nebo vůbec. Opakované spuštění nic nemění.
    /// </summary>
    public sealed class Migrator
    {
        private const string ResourcePrefix = "Migrations/";
        private static readonly Regex ResourceName = new Regex(@"^(\d{3})_(.+)\.sql$", RegexOptions.CultureInvariant);

        private readonly IReadOnlyList<Migration> _migrations;

        /// <summary>Migrátor se skripty zabalenými v této knihovně.</summary>
        public Migrator()
            : this(LoadEmbedded())
        {
        }

        /// <summary>Migrátor s vlastní sadou skriptů (pro testy).</summary>
        public Migrator(IEnumerable<Migration> migrations)
        {
            if (migrations == null) throw new ArgumentNullException(nameof(migrations));

            var ordered = migrations.OrderBy(migration => migration.Version).ToList();
            for (var i = 0; i < ordered.Count; i++)
            {
                if (ordered[i].Version != i + 1)
                {
                    throw new ArgumentException("Migrace musí být číslované souvisle od 001.", nameof(migrations));
                }
            }

            _migrations = ordered;
        }

        /// <summary>Verze schématu, kterou tato verze aplikace očekává.</summary>
        public int LatestVersion
        {
            get { return _migrations.Count; }
        }

        public static IReadOnlyList<Migration> LoadEmbedded()
        {
            var assembly = typeof(Migrator).Assembly;
            var result = new List<Migration>();

            foreach (var resource in assembly.GetManifestResourceNames())
            {
                if (!resource.StartsWith(ResourcePrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                var match = ResourceName.Match(resource.Substring(ResourcePrefix.Length));
                if (!match.Success)
                {
                    throw new InvalidOperationException("Migrační skript má neplatný název: " + resource);
                }

                using (var stream = assembly.GetManifestResourceStream(resource))
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    result.Add(new Migration(
                        int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                        match.Groups[2].Value,
                        reader.ReadToEnd()));
                }
            }

            return result;
        }

        public static int GetVersion(SqliteConnection connection)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA user_version;";
                return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
        }

        /// <summary><c>true</c>, pokud na databázi čekají neaplikované migrace.</summary>
        /// <exception cref="DatabaseNewerThanAppException">Databáze má novější schéma, než aplikace zná.</exception>
        public bool HasPending(SqliteConnection connection)
        {
            return GetCheckedVersion(connection) < LatestVersion;
        }

        /// <summary>Aplikuje čekající migrace. Vrací počet aplikovaných skriptů (0 = nebylo co dělat).</summary>
        /// <exception cref="DatabaseNewerThanAppException">Databáze má novější schéma, než aplikace zná.</exception>
        public int Apply(SqliteConnection connection)
        {
            var current = GetCheckedVersion(connection);
            var applied = 0;

            foreach (var migration in _migrations.Where(candidate => candidate.Version > current))
            {
                using (var transaction = connection.BeginTransaction())
                {
                    Execute(connection, transaction, migration.Sql);
                    Execute(connection, transaction,
                        "PRAGMA user_version = " + migration.Version.ToString(CultureInfo.InvariantCulture) + ";");
                    transaction.Commit();
                }

                applied++;
            }

            return applied;
        }

        private int GetCheckedVersion(SqliteConnection connection)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            var current = GetVersion(connection);
            if (current > LatestVersion)
            {
                throw new DatabaseNewerThanAppException(current, LatestVersion);
            }

            return current;
        }

        private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
        }
    }
}
