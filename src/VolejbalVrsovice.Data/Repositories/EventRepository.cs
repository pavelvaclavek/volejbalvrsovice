using System.Collections.Generic;
using System.Threading.Tasks;
using VolejbalVrsovice.Core.Entities;
using VolejbalVrsovice.Core.Repositories;
using VolejbalVrsovice.Core.Services;
using VolejbalVrsovice.Data.Db;

namespace VolejbalVrsovice.Data.Repositories
{
    public sealed class EventRepository : RepositoryBase, IEventRepository
    {
        private const string SelectSql = @"
SELECT Id, Name, Date, DateTo, Location, DefaultCost, Description, IsClosed, CreatedAt, CreatedBy, ModifiedAt, ModifiedBy
FROM Event";

        private const string InsertSql = @"
INSERT INTO Event (Name, Date, DateTo, Location, DefaultCost, Description, IsClosed, CreatedAt, CreatedBy, ModifiedAt, ModifiedBy)
VALUES (@Name, @Date, @DateTo, @Location, @DefaultCost, @Description, @IsClosed, @CreatedAt, @CreatedBy, @ModifiedAt, @ModifiedBy);
SELECT last_insert_rowid();";

        private const string UpdateSql = @"
UPDATE Event
SET Name = @Name, Date = @Date, DateTo = @DateTo, Location = @Location, DefaultCost = @DefaultCost,
    Description = @Description, IsClosed = @IsClosed, ModifiedAt = @ModifiedAt, ModifiedBy = @ModifiedBy
WHERE Id = @Id;";

        public EventRepository(IDatabase database, ICurrentUser currentUser, IClock clock)
            : base(database, currentUser, clock)
        {
        }

        public Task<Event> GetByIdAsync(int id)
        {
            return GetAsync<Event>(SelectSql + " WHERE Id = @id;", new { id });
        }

        public Task<IReadOnlyList<Event>> GetAllAsync()
        {
            return ListAsync<Event>(SelectSql + " ORDER BY Date DESC, Id DESC;");
        }

        public Task InsertAsync(Event evt)
        {
            Normalize(evt);
            return InsertEntityAsync(InsertSql, evt);
        }

        public Task UpdateAsync(Event evt)
        {
            Normalize(evt);
            return UpdateEntityAsync(UpdateSql, evt);
        }

        private static void Normalize(Event evt)
        {
            evt.Name = Clean(evt.Name);
            evt.Location = Clean(evt.Location);
            evt.Description = Clean(evt.Description);
        }
    }

    public sealed class EventParticipantRepository : RepositoryBase, IEventParticipantRepository
    {
        private const string SelectSql = @"
SELECT Id, EventId, PlayerId, Cost, IsCostOverridden, Note, CreatedAt, CreatedBy, ModifiedAt, ModifiedBy
FROM EventParticipant";

        private const string InsertSql = @"
INSERT INTO EventParticipant (EventId, PlayerId, Cost, IsCostOverridden, Note, CreatedAt, CreatedBy, ModifiedAt, ModifiedBy)
VALUES (@EventId, @PlayerId, @Cost, @IsCostOverridden, @Note, @CreatedAt, @CreatedBy, @ModifiedAt, @ModifiedBy);
SELECT last_insert_rowid();";

        private const string UpdateSql = @"
UPDATE EventParticipant
SET Cost = @Cost, IsCostOverridden = @IsCostOverridden, Note = @Note, ModifiedAt = @ModifiedAt, ModifiedBy = @ModifiedBy
WHERE Id = @Id;";

        public EventParticipantRepository(IDatabase database, ICurrentUser currentUser, IClock clock)
            : base(database, currentUser, clock)
        {
        }

        public Task<EventParticipant> GetByIdAsync(int id)
        {
            return GetAsync<EventParticipant>(SelectSql + " WHERE Id = @id;", new { id });
        }

        public Task<IReadOnlyList<EventParticipant>> GetByEventAsync(int eventId)
        {
            return ListAsync<EventParticipant>(SelectSql + " WHERE EventId = @eventId ORDER BY Id;", new { eventId });
        }

        public Task<IReadOnlyList<EventParticipant>> GetByPlayerAsync(int playerId)
        {
            return ListAsync<EventParticipant>(SelectSql + " WHERE PlayerId = @playerId ORDER BY Id;", new { playerId });
        }

        public Task InsertAsync(EventParticipant participant)
        {
            participant.Note = Clean(participant.Note);
            return InsertEntityAsync(InsertSql, participant);
        }

        public Task UpdateAsync(EventParticipant participant)
        {
            participant.Note = Clean(participant.Note);
            return UpdateEntityAsync(UpdateSql, participant);
        }

        public Task DeleteAsync(int id)
        {
            return DeleteEntityAsync("DELETE FROM EventParticipant WHERE Id = @id;", nameof(EventParticipant), id);
        }
    }
}
