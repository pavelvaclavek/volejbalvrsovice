using System.Collections.Generic;
using System.Threading.Tasks;
using VolejbalVrsovice.Core.Entities;

namespace VolejbalVrsovice.Core.Repositories
{
    public interface IAppUserRepository
    {
        /// <summary>Vrátí uživatele, nebo <c>null</c>.</summary>
        Task<AppUser> GetByIdAsync(int id);

        /// <summary>Vrátí uživatele podle e-mailu (porovnání bez ohledu na velikost písmen), nebo <c>null</c>.</summary>
        Task<AppUser> GetByEmailAsync(string email);

        Task<IReadOnlyList<AppUser>> GetAllAsync();

        /// <summary>Počet aktivních správců – posledního nelze deaktivovat.</summary>
        Task<int> CountActiveAdminsAsync();

        /// <summary>Vloží uživatele; doplní <c>Id</c> a auditní sloupce.</summary>
        Task InsertAsync(AppUser user);

        Task UpdateAsync(AppUser user);

        /// <summary>Zapíše okamžik úspěšného přihlášení.</summary>
        Task RecordLoginAsync(int userId);
    }
}
