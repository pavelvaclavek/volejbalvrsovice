using System;
using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using VolejbalVrsovice.Data.Security;

namespace VolejbalVrsovice.Data.Db
{
    /// <summary>Databázi nelze daným klíčem otevřít (špatný klíč, nebo soubor není šifrovaná databáze).</summary>
    public sealed class DatabaseKeyException : Exception
    {
        public DatabaseKeyException(Exception innerException)
            : base("Databázi nelze tímto klíčem otevřít.", innerException)
        {
        }
    }

    /// <summary>
    /// Otevírá spojení na šifrovanou databázi (SQLCipher, AES-256). Hned po <c>Open()</c> nastaví klíč
    /// příkazem <c>PRAGMA key = "x'&lt;hex&gt;'"</c> (surový 256bitový klíč – SQLCipher z něj už nic
    /// neodvozuje, takže otevření je rychlé).
    /// <para>
    /// Kvůli provozu na síťovém disku: žurnál v režimu DELETE (WAL přes SMB nefunguje), bez poolingu
    /// a spojení se po každé operaci zavírá.
    /// </para>
    /// </summary>
    public sealed class SqliteConnectionFactory
    {
        private const int SqliteNotADatabase = 26;

        private readonly string _databaseFile;

        public SqliteConnectionFactory(string databaseFile)
        {
            if (string.IsNullOrWhiteSpace(databaseFile)) throw new ArgumentException("Cesta k databázi musí být zadána.", nameof(databaseFile));

            _databaseFile = databaseFile;
        }

        public string DatabaseFile
        {
            get { return _databaseFile; }
        }

        /// <summary>Otevře existující databázi. Volající spojení po dokončení operace zavře.</summary>
        /// <exception cref="DatabaseKeyException">Klíč k databázi nepatří.</exception>
        public SqliteConnection Open(byte[] key)
        {
            return OpenFile(_databaseFile, key, createIfMissing: false);
        }

        public static SqliteConnection OpenFile(string file, byte[] key, bool createIfMissing)
        {
            DapperSetup.EnsureConfigured();

            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = file,
                Mode = createIfMissing ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadWrite,
                Pooling = false,
            }.ToString();

            var connection = new SqliteConnection(connectionString);
            try
            {
                connection.Open();

                // Klíč musí být úplně první příkaz na spojení.
                Execute(connection, BuildKeyPragma(key));

                // První čtení ověří klíč – se špatným klíčem skončí chybou „file is not a database“.
                Execute(connection, "SELECT count(*) FROM sqlite_master;");

                Execute(connection, "PRAGMA foreign_keys = ON;");
                Execute(connection, "PRAGMA journal_mode = DELETE;");

                return connection;
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteNotADatabase)
            {
                connection.Dispose();
                throw new DatabaseKeyException(ex);
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }

        /// <summary>Sestaví <c>PRAGMA key</c> se surovým klíčem. Výsledek obsahuje klíč – nikdy ho nelogovat.</summary>
        private static string BuildKeyPragma(byte[] key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (key.Length != KeyEnvelope.KeySize) throw new ArgumentException("Klíč databáze musí mít 32 bajtů.", nameof(key));

            var sql = new StringBuilder("PRAGMA key = \"x'", 16 + (key.Length * 2) + 4);
            foreach (var value in key)
            {
                sql.Append(value.ToString("X2", CultureInfo.InvariantCulture));
            }

            return sql.Append("'\";").ToString();
        }

        private static void Execute(SqliteConnection connection, string sql)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
        }
    }
}
