using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using VolejbalVrsovice.Core;
using VolejbalVrsovice.Core.Entities;
using VolejbalVrsovice.Core.Models;
using VolejbalVrsovice.Core.Repositories;
using VolejbalVrsovice.Core.Services;
using VolejbalVrsovice.Data.Db;

namespace VolejbalVrsovice.Data.Repositories
{
    /// <summary>Klíče tabulky <c>Meta</c>.</summary>
    public static class MetaKeys
    {
        /// <summary>GUID databáze – musí se shodovat s <c>dbId</c> v <c>keys.json</c>.</summary>
        public const string DbId = "DbId";

        /// <summary>Okamžik vytvoření databáze (UTC).</summary>
        public const string CreatedAt = "CreatedAt";
    }

    internal static class MetaSql
    {
        public const string TableExists = "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = 'Meta';";

        public const string Select = "SELECT Value FROM Meta WHERE \"Key\" = @key;";

        public const string Upsert = @"
INSERT INTO Meta (""Key"", Value) VALUES (@key, @value)
ON CONFLICT (""Key"") DO UPDATE SET Value = excluded.Value;";
    }

    public sealed class CountryRepository : ICountryRepository
    {
        private readonly IDatabase _database;

        public CountryRepository(IDatabase database)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
        }

        public Task<IReadOnlyList<Country>> GetAllAsync()
        {
            return _database.ReadAsync<IReadOnlyList<Country>>(async connection =>
                (await connection.QueryAsync<Country>("SELECT Code, Name FROM Country;").ConfigureAwait(false))
                .OrderBy(country => country.Name, CzechText.Comparer)
                .ToList());
        }
    }

    public sealed class AuditLogRepository : RepositoryBase, IAuditLogRepository
    {
        private const string InsertSql = @"
INSERT INTO AuditLog (Timestamp, UserId, Entity, EntityId, Action, OldValues, NewValues)
VALUES (@Timestamp, @UserId, @Entity, @EntityId, @Action, @OldValues, @NewValues);
SELECT last_insert_rowid();";

        private const string SelectSql = @"
SELECT Id, Timestamp, UserId, Entity, EntityId, Action, OldValues, NewValues
FROM AuditLog";

        public AuditLogRepository(IDatabase database, ICurrentUser currentUser, IClock clock)
            : base(database, currentUser, clock)
        {
        }

        public async Task AddAsync(AuditLogEntry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));

            entry.Timestamp = UtcNow();
            entry.UserId = CurrentUserId;
            entry.Id = await Db.WriteAsync((connection, transaction) => InsertAsync(connection, transaction, entry))
                .ConfigureAwait(false);
        }

        public Task<IReadOnlyList<AuditLogEntry>> GetByEntityAsync(string entity, int entityId)
        {
            return ListAsync<AuditLogEntry>(
                SelectSql + " WHERE Entity = @entity AND EntityId = @entityId ORDER BY Id;", new { entity, entityId });
        }

        /// <summary>
        /// Zápis auditu uvnitř cizí transakce – aby změna finančního záznamu a její audit byly
        /// uloženy vždy společně. <paramref name="entry"/> musí mít vyplněný okamžik i uživatele.
        /// </summary>
        internal static Task<int> InsertAsync(IDbConnection connection, IDbTransaction transaction, AuditLogEntry entry)
        {
            return InsertAndGetIdAsync(connection, transaction, InsertSql, entry);
        }
    }

    public sealed class BalanceQueries : IBalanceQueries
    {
        // Saldo je definované jediným pohledem PlayerBalance (viz 001_Init.sql).
        private const string SelectSql = "SELECT b.PlayerId, b.ContributionTotal, b.CostTotal, b.Balance FROM PlayerBalance b";

        private const string CurrentMembersOf = "SELECT m.PlayerId FROM TeamMembership m WHERE m.LeftOn IS NULL AND m.TeamId";

        private readonly IDatabase _database;

        public BalanceQueries(IDatabase database)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
        }

        public Task<PlayerBalance> GetPlayerBalanceAsync(int playerId)
        {
            return _database.ReadAsync(connection => connection.QuerySingleOrDefaultAsync<PlayerBalance>(
                SelectSql + " WHERE b.PlayerId = @playerId;", new { playerId }));
        }

        public Task<IReadOnlyList<PlayerBalance>> GetAllAsync()
        {
            return ListAsync(SelectSql + " ORDER BY b.PlayerId;", null);
        }

        public Task<IReadOnlyList<PlayerBalance>> GetByTeamAsync(int teamId)
        {
            return ListAsync(
                SelectSql + " WHERE b.PlayerId IN (" + CurrentMembersOf + " = @teamId) ORDER BY b.PlayerId;", new { teamId });
        }

        public Task<DebtSummary> GetDebtSummaryAsync(IReadOnlyCollection<int> teamIds = null)
        {
            // IN (poddotaz) vrací každého hráče nejvýše jednou – hráč ve dvou družstvech se nezapočte dvakrát.
            const string summary =
                "SELECT count(*) AS PlayersInDebt, COALESCE(SUM(b.Balance), 0) AS TotalDebt FROM PlayerBalance b WHERE b.Balance < 0";

            if (teamIds == null)
            {
                return _database.ReadAsync(connection => connection.QuerySingleAsync<DebtSummary>(summary + ";"));
            }

            var ids = teamIds.Distinct().ToArray();
            return _database.ReadAsync(connection => connection.QuerySingleAsync<DebtSummary>(
                summary + " AND b.PlayerId IN (" + CurrentMembersOf + " IN @ids);", new { ids }));
        }

        private Task<IReadOnlyList<PlayerBalance>> ListAsync(string sql, object parameters)
        {
            return _database.ReadAsync<IReadOnlyList<PlayerBalance>>(async connection =>
                (await connection.QueryAsync<PlayerBalance>(sql, parameters).ConfigureAwait(false)).AsList());
        }
    }
}
