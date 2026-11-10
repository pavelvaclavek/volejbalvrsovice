namespace VolejbalVrsovice.Core.Services
{
    /// <summary>Kdo právě pracuje – repozitáře podle toho vyplňují auditní sloupce.</summary>
    public interface ICurrentUser
    {
        /// <summary>Id přihlášeného uživatele aplikace; <c>null</c>, pokud nikdo přihlášen není (systémové operace).</summary>
        int? UserId { get; }
    }
}
