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
    public sealed class TeamRepository : RepositoryBase, ITeamRepository
    {
        private const string SelectSql = @"
SELECT Id, Name, Description, ContactPerson, ContactEmail, ContactPhone, IsActive, CreatedAt, CreatedBy, ModifiedAt, ModifiedBy
FROM Team";

        private const string InsertSql = @"
INSERT INTO Team (Name, Description, ContactPerson, ContactEmail, ContactPhone, IsActive, CreatedAt, CreatedBy, ModifiedAt, ModifiedBy)
VALUES (@Name, @Description, @ContactPerson, @ContactEmail, @ContactPhone, @IsActive, @CreatedAt, @CreatedBy, @ModifiedAt, @ModifiedBy);
SELECT last_insert_rowid();";

        private const string UpdateSql = @"
UPDATE Team
SET Name = @Name, Description = @Description, ContactPerson = @ContactPerson, ContactEmail = @ContactEmail,
    ContactPhone = @ContactPhone, IsActive = @IsActive, ModifiedAt = @ModifiedAt, ModifiedBy = @ModifiedBy
WHERE Id = @Id;";

        public TeamRepository(IDatabase database, ICurrentUser currentUser, IClock clock)
            : base(database, currentUser, clock)
        {
        }

        public Task<Team> GetByIdAsync(int id)
        {
            return GetAsync<Team>(SelectSql + " WHERE Id = @id;", new { id });
        }

        public async Task<IReadOnlyList<Team>> GetAllAsync(bool includeInactive = true)
        {
            var teams = await ListAsync<Team>(SelectSql + (includeInactive ? ";" : " WHERE IsActive = 1;")).ConfigureAwait(false);
            return teams.OrderBy(team => team.Name, CzechText.Comparer).ThenBy(team => team.Id).ToList();
        }

        public Task InsertAsync(Team team)
        {
            Normalize(team);
            return InsertEntityAsync(InsertSql, team);
        }

        public Task UpdateAsync(Team team)
        {
            Normalize(team);
            return UpdateEntityAsync(UpdateSql, team);
        }

        private static void Normalize(Team team)
        {
            team.Name = Clean(team.Name);
            team.Description = Clean(team.Description);
            team.ContactPerson = Clean(team.ContactPerson);
            team.ContactEmail = Clean(team.ContactEmail);
            team.ContactPhone = Clean(team.ContactPhone);
        }
    }

    public sealed class TrainingScheduleRepository : RepositoryBase, ITrainingScheduleRepository
    {
        private const string SelectSql = @"
SELECT Id, TeamId, DayOfWeek, TimeFrom, TimeTo, Address, Note, CreatedAt, CreatedBy, ModifiedAt, ModifiedBy
FROM TrainingSchedule";

        private const string InsertSql = @"
INSERT INTO TrainingSchedule (TeamId, DayOfWeek, TimeFrom, TimeTo, Address, Note, CreatedAt, CreatedBy, ModifiedAt, ModifiedBy)
VALUES (@TeamId, @DayOfWeek, @TimeFrom, @TimeTo, @Address, @Note, @CreatedAt, @CreatedBy, @ModifiedAt, @ModifiedBy);
SELECT last_insert_rowid();";

        private const string UpdateSql = @"
UPDATE TrainingSchedule
SET TeamId = @TeamId, DayOfWeek = @DayOfWeek, TimeFrom = @TimeFrom, TimeTo = @TimeTo, Address = @Address,
    Note = @Note, ModifiedAt = @ModifiedAt, ModifiedBy = @ModifiedBy
WHERE Id = @Id;";

        public TrainingScheduleRepository(IDatabase database, ICurrentUser currentUser, IClock clock)
            : base(database, currentUser, clock)
        {
        }

        public Task<TrainingSchedule> GetByIdAsync(int id)
        {
            return GetAsync<TrainingSchedule>(SelectSql + " WHERE Id = @id;", new { id });
        }

        public Task<IReadOnlyList<TrainingSchedule>> GetByTeamAsync(int teamId)
        {
            // (DayOfWeek + 6) % 7 – týden začíná pondělím, neděle (0) je poslední.
            return ListAsync<TrainingSchedule>(
                SelectSql + " WHERE TeamId = @teamId ORDER BY (DayOfWeek + 6) % 7, TimeFrom, Id;", new { teamId });
        }

        public Task InsertAsync(TrainingSchedule schedule)
        {
            Normalize(schedule);
            return InsertEntityAsync(InsertSql, schedule);
        }

        public Task UpdateAsync(TrainingSchedule schedule)
        {
            Normalize(schedule);
            return UpdateEntityAsync(UpdateSql, schedule);
        }

        public Task DeleteAsync(int id)
        {
            return DeleteEntityAsync("DELETE FROM TrainingSchedule WHERE Id = @id;", nameof(TrainingSchedule), id);
        }

        private static void Normalize(TrainingSchedule schedule)
        {
            schedule.Address = Clean(schedule.Address);
            schedule.Note = Clean(schedule.Note);
        }
    }
}
