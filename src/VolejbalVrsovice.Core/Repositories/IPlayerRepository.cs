using System.Collections.Generic;
using System.Threading.Tasks;
using VolejbalVrsovice.Core.Entities;

namespace VolejbalVrsovice.Core.Repositories
{
    public interface IPlayerRepository
    {
        /// <summary>Vrátí hráče, nebo <c>null</c>.</summary>
        Task<Player> GetByIdAsync(int id);

        /// <summary>Všichni hráči seřazení podle příjmení a jména.</summary>
        Task<IReadOnlyList<Player>> GetAllAsync();

        /// <summary>Aktuální členové družstva (bez ukončených členství), včetně hráčů neaktivních v klubu.</summary>
        Task<IReadOnlyList<Player>> GetByTeamAsync(int teamId);

        /// <summary>
        /// Vloží hráče i s jeho členstvími v jedné transakci. Hráč musí mít při založení alespoň
        /// jedno členství. Doplní <c>Id</c> hráče i členství a auditní sloupce.
        /// </summary>
        Task InsertAsync(Player player, IReadOnlyCollection<TeamMembership> memberships);

        Task UpdateAsync(Player player);
    }

    public interface ITeamMembershipRepository
    {
        Task<TeamMembership> GetByIdAsync(int id);

        /// <summary>Všechna členství hráče včetně ukončených (historie).</summary>
        Task<IReadOnlyList<TeamMembership>> GetByPlayerAsync(int playerId);

        /// <summary>Všechna členství v družstvu včetně ukončených.</summary>
        Task<IReadOnlyList<TeamMembership>> GetByTeamAsync(int teamId);

        Task InsertAsync(TeamMembership membership);

        /// <summary>Upraví data nástupu a odchodu (ukončení členství = vyplnění <c>LeftOn</c>).</summary>
        Task UpdateAsync(TeamMembership membership);
    }
}
