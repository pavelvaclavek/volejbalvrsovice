using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using VolejbalVrsovice.Core.Entities;
using VolejbalVrsovice.Core.Services;
using VolejbalVrsovice.Data.Db;

namespace VolejbalVrsovice.Data.Repositories
{
    /// <summary>
    /// Společný základ repozitářů: přístup k databázi a vyplňování auditních sloupců
    /// (kdo a kdy záznam vytvořil a změnil) – to nikdy nedělá volající kód.
    /// </summary>
    public abstract class RepositoryBase
    {
        private readonly ICurrentUser _currentUser;
        private readonly IClock _clock;

        protected RepositoryBase(IDatabase database, ICurrentUser currentUser, IClock clock)
        {
            Db = database ?? throw new ArgumentNullException(nameof(database));
            _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        protected IDatabase Db { get; }

        protected int? CurrentUserId
        {
            get { return _currentUser.UserId; }
        }

        /// <summary>Aktuální okamžik v UTC zaokrouhlený dolů na sekundy (tak se ukládá).</summary>
        protected DateTime UtcNow()
        {
            return DbFormats.TruncateToSeconds(_clock.UtcNow.ToUniversalTime());
        }

        protected void StampCreated(EntityBase entity)
        {
            var now = UtcNow();
            entity.CreatedAt = now;
            entity.CreatedBy = CurrentUserId;
            entity.ModifiedAt = now;
            entity.ModifiedBy = CurrentUserId;
        }

        protected void StampModified(EntityBase entity)
        {
            entity.ModifiedAt = UtcNow();
            entity.ModifiedBy = CurrentUserId;
        }

        protected Task<T> GetAsync<T>(string sql, object parameters)
        {
            return Db.ReadAsync(connection => connection.QuerySingleOrDefaultAsync<T>(sql, parameters));
        }

        protected Task<IReadOnlyList<T>> ListAsync<T>(string sql, object parameters = null)
        {
            return Db.ReadAsync<IReadOnlyList<T>>(async connection =>
                (await connection.QueryAsync<T>(sql, parameters).ConfigureAwait(false)).AsList());
        }

        /// <summary>Vloží entitu (SQL musí končit <c>SELECT last_insert_rowid();</c>) a doplní jí <c>Id</c>.</summary>
        protected async Task InsertEntityAsync<TEntity>(string sql, TEntity entity)
            where TEntity : EntityBase
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            StampCreated(entity);
            entity.Id = await Db.WriteAsync((connection, transaction) => InsertAndGetIdAsync(connection, transaction, sql, entity))
                .ConfigureAwait(false);
        }

        protected async Task UpdateEntityAsync<TEntity>(string sql, TEntity entity)
            where TEntity : EntityBase
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            StampModified(entity);
            var affected = await Db.WriteAsync((connection, transaction) => connection.ExecuteAsync(sql, entity, transaction))
                .ConfigureAwait(false);
            EnsureFound(affected, typeof(TEntity).Name, entity.Id);
        }

        protected async Task DeleteEntityAsync(string sql, string entityName, int id)
        {
            var affected = await Db.WriteAsync((connection, transaction) => connection.ExecuteAsync(sql, new { id }, transaction))
                .ConfigureAwait(false);
            EnsureFound(affected, entityName, id);
        }

        internal static async Task<int> InsertAndGetIdAsync(IDbConnection connection, IDbTransaction transaction, string sql, object entity)
        {
            var id = await connection.ExecuteScalarAsync<long>(sql, entity, transaction).ConfigureAwait(false);
            return checked((int)id);
        }

        /// <summary>Ořízne mezery; prázdný text převede na <c>null</c> (nepovinná pole).</summary>
        protected static string Clean(string value)
        {
            if (value == null)
            {
                return null;
            }

            var trimmed = value.Trim();
            return trimmed.Length == 0 ? null : trimmed;
        }

        private static void EnsureFound(int affectedRows, string entityName, int id)
        {
            if (affectedRows == 0)
            {
                throw new InvalidOperationException("Záznam nenalezen: " + entityName + " Id " + id + ".");
            }
        }
    }
}
