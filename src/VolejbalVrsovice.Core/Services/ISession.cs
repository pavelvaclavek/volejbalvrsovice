using VolejbalVrsovice.Core.Entities;

namespace VolejbalVrsovice.Core.Services
{
    /// <summary>Relace přihlášeného uživatele. Mimo přihlášení není dostupné nic jiného než přihlašovací okno.</summary>
    public interface ISession : ICurrentUser
    {
        /// <summary>Přihlášený uživatel, nebo <c>null</c>.</summary>
        AppUser User { get; }

        bool IsAuthenticated { get; }
    }
}
