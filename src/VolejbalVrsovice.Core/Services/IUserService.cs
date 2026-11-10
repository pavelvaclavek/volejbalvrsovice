using System.Collections.Generic;
using System.Threading.Tasks;
using VolejbalVrsovice.Core.Entities;

namespace VolejbalVrsovice.Core.Services
{
    /// <summary>Výsledek založení uživatele.</summary>
    public sealed class CreateUserResult : OperationResult
    {
        private CreateUserResult(string error, AppUser user)
            : base(error)
        {
            User = user;
        }

        public AppUser User { get; }

        public static CreateUserResult Created(AppUser user)
        {
            return new CreateUserResult(null, user);
        }

        public static new CreateUserResult Failure(string error)
        {
            return new CreateUserResult(error ?? "Uživatele se nepodařilo založit.", null);
        }
    }

    /// <summary>
    /// Správa uživatelů aplikace – smí ji provádět jen přihlášený aktivní Správce
    /// (jinak <see cref="System.UnauthorizedAccessException"/>).
    /// </summary>
    public interface IUserService
    {
        Task<IReadOnlyList<AppUser>> GetUsersAsync();

        /// <summary>
        /// Založí uživatele s dočasným heslem; při prvním přihlášení si ho musí změnit.
        /// </summary>
        Task<CreateUserResult> CreateUserAsync(string email, string displayName, UserRole role, string temporaryPassword);

        /// <summary>Nastaví uživateli dočasné heslo; při příštím přihlášení si ho musí změnit.</summary>
        Task<OperationResult> ResetPasswordAsync(int userId, string temporaryPassword);

        /// <summary>
        /// Deaktivuje uživatele a odstraní jeho záznam ze souboru klíčů. Posledního aktivního
        /// správce deaktivovat nelze.
        /// </summary>
        Task<OperationResult> DeactivateAsync(int userId);

        /// <summary>
        /// Vygeneruje nový záchranný kód (dosavadní přestává platit) a vrátí ho k jednorázovému zobrazení.
        /// </summary>
        Task<string> GenerateRecoveryCodeAsync();
    }
}
