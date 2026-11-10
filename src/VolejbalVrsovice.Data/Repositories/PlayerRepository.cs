using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VolejbalVrsovice.Core;
using VolejbalVrsovice.Core.Entities;
using VolejbalVrsovice.Core.Repositories;
using VolejbalVrsovice.Core.Services;
using VolejbalVrsovice.Data.Db;

namespace VolejbalVrsovice.Data.Repositories
{
    public sealed class PlayerRepository : RepositoryBase, IPlayerRepository
    {
        private const string Columns =
            "p.Id, p.FirstName, p.LastName, p.Gender, p.BirthDate, p.NationalityCode, p.Email, p.Phone, p.Address, "
            + "p.JoinedOn, p.IsActive, p.HeightCm, p.Note, p.CreatedAt, p.CreatedBy, p.ModifiedAt, p.ModifiedBy";

        private const string SelectSql = "SELECT " + Columns + " FROM Player p";

        private const string InsertSql = @"
INSERT INTO Player (FirstName, LastName, Gender, BirthDate, NationalityCode, Email, Phone, Address, JoinedOn, IsActive,
                    HeightCm, Note, CreatedAt, CreatedBy, ModifiedAt, ModifiedBy)
VALUES (@FirstName, @LastName, @Gender, @BirthDate, @NationalityCode, @Email, @Phone, @Address, @JoinedOn, @IsActive,
        @HeightCm, @Note, @CreatedAt, @CreatedBy, @ModifiedAt, @ModifiedBy);
SELECT last_insert_rowid();";

        private const string UpdateSql = @"
UPDATE Player
SET FirstName = @FirstName, LastName = @LastName, Gender = @Gender, BirthDate = @BirthDate,
    NationalityCode = @NationalityCode, Email = @Email, Phone = @Phone, Address = @Address, JoinedOn = @JoinedOn,
    IsActive = @IsActive, HeightCm = @HeightCm, Note = @Note, ModifiedAt = @ModifiedAt, ModifiedBy = @ModifiedBy
WHERE Id = @Id;";

        public PlayerRepository(IDatabase database, ICurrentUser currentUser, IClock clock)
            : base(database, currentUser, clock)
        {
        }

        public Task<Player> GetByIdAsync(int id)
        {
            return GetAsync<Player>(SelectSql + " WHERE p.Id = @id;", new { id });
        }

        public async Task<IReadOnlyList<Player>> GetAllAsync()
        {
            return Sort(await ListAsync<Player>(SelectSql + ";").ConfigureAwait(false));
        }

        public async Task<IReadOnlyList<Player>> GetByTeamAsync(int teamId)
        {
            const string sql = SelectSql + @"
WHERE EXISTS (SELECT 1 FROM TeamMembership m WHERE m.PlayerId = p.Id AND m.TeamId = @teamId AND m.LeftOn IS NULL);";

            return Sort(await ListAsync<Player>(sql, new { teamId }).ConfigureAwait(false));
        }

        public async Task InsertAsync(Player player, IReadOnlyCollection<TeamMembership> memberships)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));
            if (memberships == null || memberships.Count == 0)
            {
                throw new ArgumentException("Hráč musí mít při založení alespoň jedno členství v družstvu.", nameof(memberships));
            }

            Normalize(player);
            StampCreated(player);
            foreach (var membership in memberships)
            {
                StampCreated(membership);
            }

            await Db.WriteAsync(async (connection, transaction) =>
            {
                player.Id = await InsertAndGetIdAsync(connection, transaction, InsertSql, player).ConfigureAwait(false);

                foreach (var membership in memberships)
                {
                    membership.PlayerId = player.Id;
                    membership.Id = await InsertAndGetIdAsync(connection, transaction, TeamMembershipRepository.InsertSql, membership)
                        .ConfigureAwait(false);
                }
            }).ConfigureAwait(false);
        }

        public Task UpdateAsync(Player player)
        {
            Normalize(player);
            return UpdateEntityAsync(UpdateSql, player);
        }

        private static IReadOnlyList<Player> Sort(IEnumerable<Player> players)
        {
            return players
                .OrderBy(player => player.LastName, CzechText.Comparer)
                .ThenBy(player => player.FirstName, CzechText.Comparer)
                .ThenBy(player => player.Id)
                .ToList();
        }

        private static void Normalize(Player player)
        {
            player.FirstName = Clean(player.FirstName);
            player.LastName = Clean(player.LastName);
            player.NationalityCode = (Clean(player.NationalityCode) ?? Country.DefaultCode).ToUpperInvariant();
            player.Email = Clean(player.Email);
            player.Phone = Clean(player.Phone);
            player.Address = Clean(player.Address);
            player.Note = Clean(player.Note);
        }
    }

    public sealed class TeamMembershipRepository : RepositoryBase, ITeamMembershipRepository
    {
        internal const string InsertSql = @"
INSERT INTO TeamMembership (PlayerId, TeamId, JoinedOn, LeftOn, CreatedAt, CreatedBy, ModifiedAt, ModifiedBy)
VALUES (@PlayerId, @TeamId, @JoinedOn, @LeftOn, @CreatedAt, @CreatedBy, @ModifiedAt, @ModifiedBy);
SELECT last_insert_rowid();";

        private const string SelectSql = @"
SELECT Id, PlayerId, TeamId, JoinedOn, LeftOn, CreatedAt, CreatedBy, ModifiedAt, ModifiedBy
FROM TeamMembership";

        private const string UpdateSql = @"
UPDATE TeamMembership
SET JoinedOn = @JoinedOn, LeftOn = @LeftOn, ModifiedAt = @ModifiedAt, ModifiedBy = @ModifiedBy
WHERE Id = @Id;";

        public TeamMembershipRepository(IDatabase database, ICurrentUser currentUser, IClock clock)
            : base(database, currentUser, clock)
        {
        }

        public Task<TeamMembership> GetByIdAsync(int id)
        {
            return GetAsync<TeamMembership>(SelectSql + " WHERE Id = @id;", new { id });
        }

        public Task<IReadOnlyList<TeamMembership>> GetByPlayerAsync(int playerId)
        {
            return ListAsync<TeamMembership>(SelectSql + " WHERE PlayerId = @playerId ORDER BY JoinedOn, Id;", new { playerId });
        }

        public Task<IReadOnlyList<TeamMembership>> GetByTeamAsync(int teamId)
        {
            return ListAsync<TeamMembership>(SelectSql + " WHERE TeamId = @teamId ORDER BY JoinedOn, Id;", new { teamId });
        }

        public Task InsertAsync(TeamMembership membership)
        {
            return InsertEntityAsync(InsertSql, membership);
        }

        public Task UpdateAsync(TeamMembership membership)
        {
            return UpdateEntityAsync(UpdateSql, membership);
        }
    }
}
