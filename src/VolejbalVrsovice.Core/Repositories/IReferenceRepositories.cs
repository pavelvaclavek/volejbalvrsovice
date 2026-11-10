using System.Collections.Generic;
using System.Threading.Tasks;
using VolejbalVrsovice.Core.Entities;
using VolejbalVrsovice.Core.Models;

namespace VolejbalVrsovice.Core.Repositories
{
    public interface ICountryRepository
    {
        /// <summary>Číselník zemí seřazený česky podle názvu.</summary>
        Task<IReadOnlyList<Country>> GetAllAsync();
    }

    public interface IAuditLogRepository
    {
        /// <summary>Přidá záznam; doplní <c>Id</c>, okamžik a přihlášeného uživatele.</summary>
        Task AddAsync(AuditLogEntry entry);

        /// <summary>Historie jedné entity od nejstaršího záznamu.</summary>
        Task<IReadOnlyList<AuditLogEntry>> GetByEntityAsync(string entity, int entityId);
    }

    /// <summary>
    /// Výpočet salda: <c>Saldo(hráč) = Σ Contribution.Amount − Σ EventParticipant.Cost</c>.
    /// Počítá se agregací v databázi, ne načítáním záznamů do paměti.
    /// </summary>
    public interface IBalanceQueries
    {
        /// <summary>Saldo jednoho hráče; <c>null</c>, pokud hráč neexistuje.</summary>
        Task<PlayerBalance> GetPlayerBalanceAsync(int playerId);

        /// <summary>Salda všech hráčů.</summary>
        Task<IReadOnlyList<PlayerBalance>> GetAllAsync();

        /// <summary>
        /// Salda aktuálních členů družstva. Jde vždy o celkové saldo hráče – včetně akcí,
        /// na které šel s jiným družstvem.
        /// </summary>
        Task<IReadOnlyList<PlayerBalance>> GetByTeamAsync(int teamId);

        /// <summary>
        /// Souhrn dlužníků mezi aktuálními členy zadaných družstev; <c>null</c> = všichni hráči klubu.
        /// Počítá se přes různé hráče, takže hráč ve dvou družstvech se započte jen jednou.
        /// </summary>
        Task<DebtSummary> GetDebtSummaryAsync(IReadOnlyCollection<int> teamIds = null);
    }
}
