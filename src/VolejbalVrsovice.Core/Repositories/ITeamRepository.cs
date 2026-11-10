using System.Collections.Generic;
using System.Threading.Tasks;
using VolejbalVrsovice.Core.Entities;

namespace VolejbalVrsovice.Core.Repositories
{
    public interface ITeamRepository
    {
        /// <summary>Vrátí družstvo, nebo <c>null</c>.</summary>
        Task<Team> GetByIdAsync(int id);

        /// <summary>Družstva seřazená podle názvu.</summary>
        Task<IReadOnlyList<Team>> GetAllAsync(bool includeInactive = true);

        /// <summary>Vloží družstvo; doplní <c>Id</c> a auditní sloupce.</summary>
        Task InsertAsync(Team team);

        Task UpdateAsync(Team team);
    }

    public interface ITrainingScheduleRepository
    {
        Task<TrainingSchedule> GetByIdAsync(int id);

        /// <summary>Tréninky družstva seřazené podle dne v týdnu (od pondělí) a času.</summary>
        Task<IReadOnlyList<TrainingSchedule>> GetByTeamAsync(int teamId);

        Task InsertAsync(TrainingSchedule schedule);

        Task UpdateAsync(TrainingSchedule schedule);

        Task DeleteAsync(int id);
    }
}
