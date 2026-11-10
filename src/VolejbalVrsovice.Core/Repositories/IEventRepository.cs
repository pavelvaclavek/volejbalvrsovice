using System.Collections.Generic;
using System.Threading.Tasks;
using VolejbalVrsovice.Core.Entities;

namespace VolejbalVrsovice.Core.Repositories
{
    public interface IEventRepository
    {
        /// <summary>Vrátí akci, nebo <c>null</c>.</summary>
        Task<Event> GetByIdAsync(int id);

        /// <summary>Akce od nejnovější.</summary>
        Task<IReadOnlyList<Event>> GetAllAsync();

        Task InsertAsync(Event evt);

        Task UpdateAsync(Event evt);
    }

    public interface IEventParticipantRepository
    {
        Task<EventParticipant> GetByIdAsync(int id);

        Task<IReadOnlyList<EventParticipant>> GetByEventAsync(int eventId);

        Task<IReadOnlyList<EventParticipant>> GetByPlayerAsync(int playerId);

        Task InsertAsync(EventParticipant participant);

        Task UpdateAsync(EventParticipant participant);

        Task DeleteAsync(int id);
    }
}
