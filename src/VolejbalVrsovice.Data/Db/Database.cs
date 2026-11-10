using System;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using VolejbalVrsovice.Data.Security;

namespace VolejbalVrsovice.Data.Db
{
    /// <summary>Zdroj klíče databáze (DEK) – po přihlášení ho drží relace.</summary>
    public interface IDatabaseKeySource
    {
        /// <summary>
        /// Vrátí KOPII klíče databáze. Volající ji hned po použití přepíše nulami
        /// (<c>KeyEnvelope.Clear</c>) a nikam ji neukládá.
        /// Vyhodí <see cref="InvalidOperationException"/>, pokud databáze není odemčená.
        /// </summary>
        byte[] GetKey();
    }

    /// <summary>
    /// Přístup k databázi pro repozitáře. Každá operace si otevře vlastní spojení a po dokončení ho
    /// zavře; běží na vlákně z poolu, takže UI nezamrzá (SQLite žádné skutečně asynchronní I/O nemá).
    /// </summary>
    public interface IDatabase
    {
        /// <summary>Čtení bez transakce.</summary>
        Task<T> ReadAsync<T>(Func<IDbConnection, Task<T>> query);

        /// <summary>Zápis v transakci – potvrdí se po úspěšném doběhnutí, při výjimce se vrátí zpět.</summary>
        Task<T> WriteAsync<T>(Func<IDbConnection, IDbTransaction, Task<T>> command);

        /// <summary>Zápis v transakci bez návratové hodnoty.</summary>
        Task WriteAsync(Func<IDbConnection, IDbTransaction, Task> command);
    }

    public sealed class Database : IDatabase
    {
        private readonly SqliteConnectionFactory _connections;
        private readonly IDatabaseKeySource _keySource;

        public Database(SqliteConnectionFactory connections, IDatabaseKeySource keySource)
        {
            _connections = connections ?? throw new ArgumentNullException(nameof(connections));
            _keySource = keySource ?? throw new ArgumentNullException(nameof(keySource));
        }

        public Task<T> ReadAsync<T>(Func<IDbConnection, Task<T>> query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));

            return Task.Run(async () =>
            {
                using (var connection = OpenConnection())
                {
                    return await query(connection).ConfigureAwait(false);
                }
            });
        }

        public Task<T> WriteAsync<T>(Func<IDbConnection, IDbTransaction, Task<T>> command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));

            return Task.Run(async () =>
            {
                using (var connection = OpenConnection())
                using (var transaction = connection.BeginTransaction())
                {
                    var result = await command(connection, transaction).ConfigureAwait(false);
                    transaction.Commit();
                    return result;
                }
            });
        }

        public Task WriteAsync(Func<IDbConnection, IDbTransaction, Task> command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));

            return WriteAsync<object>(async (connection, transaction) =>
            {
                await command(connection, transaction).ConfigureAwait(false);
                return null;
            });
        }

        private SqliteConnection OpenConnection()
        {
            // Klíč je jen kopie pro tuto jednu operaci – po otevření spojení ji smazat.
            var key = _keySource.GetKey();
            try
            {
                return _connections.Open(key);
            }
            finally
            {
                KeyEnvelope.Clear(key);
            }
        }
    }
}
