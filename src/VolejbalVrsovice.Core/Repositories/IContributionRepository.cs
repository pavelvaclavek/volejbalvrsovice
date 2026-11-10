using System.Collections.Generic;
using System.Threading.Tasks;
using VolejbalVrsovice.Core.Entities;

namespace VolejbalVrsovice.Core.Repositories
{
    public interface IContributionRepository
    {
        /// <summary>Vrátí příspěvek, nebo <c>null</c>.</summary>
        Task<Contribution> GetByIdAsync(int id);

        /// <summary>Příspěvky hráče od nejnovějšího.</summary>
        Task<IReadOnlyList<Contribution>> GetByPlayerAsync(int playerId);

        /// <summary><c>true</c>, pokud už hráč má záznam „Převod salda“ (smí mít nejvýše jeden).</summary>
        Task<bool> HasBalanceTransferAsync(int playerId);

        Task InsertAsync(Contribution contribution);

        Task UpdateAsync(Contribution contribution);

        Task DeleteAsync(int id);
    }
}
