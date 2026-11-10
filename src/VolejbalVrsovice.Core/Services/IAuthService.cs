using System;
using System.Threading.Tasks;
using VolejbalVrsovice.Core.Entities;

namespace VolejbalVrsovice.Core.Services
{
    public enum LoginStatus
    {
        Success,

        /// <summary>Neznámý e-mail nebo špatné heslo – navenek se nerozlišuje.</summary>
        InvalidCredentials,

        /// <summary>Heslo bylo správné, ale účet je deaktivovaný.</summary>
        AccountInactive,

        /// <summary>Přihlášení je po 5 neúspěšných pokusech dočasně zablokované.</summary>
        LockedOut,
    }

    public sealed class LoginResult
    {
        private LoginResult(LoginStatus status, AppUser user, TimeSpan? lockoutRemaining)
        {
            Status = status;
            User = user;
            LockoutRemaining = lockoutRemaining;
        }

        public LoginStatus Status { get; }

        /// <summary>Přihlášený uživatel (jen při úspěchu).</summary>
        public AppUser User { get; }

        /// <summary>Zbývající doba blokace (jen u <see cref="LoginStatus.LockedOut"/>).</summary>
        public TimeSpan? LockoutRemaining { get; }

        public bool IsSuccess
        {
            get { return Status == LoginStatus.Success; }
        }

        public static LoginResult Success(AppUser user)
        {
            return new LoginResult(LoginStatus.Success, user, null);
        }

        public static LoginResult InvalidCredentials()
        {
            return new LoginResult(LoginStatus.InvalidCredentials, null, null);
        }

        public static LoginResult AccountInactive()
        {
            return new LoginResult(LoginStatus.AccountInactive, null, null);
        }

        public static LoginResult LockedOut(TimeSpan remaining)
        {
            return new LoginResult(LoginStatus.LockedOut, null, remaining);
        }
    }

    /// <summary>Přihlášení, odhlášení, hesla a obnova přístupu záchranným kódem (zadání, sekce 5 a 5a).</summary>
    public interface IAuthService
    {
        /// <summary>
        /// První spuštění: vytvoří šifrovanou databázi, soubor klíčů a výchozího správce.
        /// Pokud úložiště už existuje, nedělá nic.
        /// </summary>
        Task InitializeAsync();

        /// <summary>
        /// Ověří heslo, odemkne databázi (včetně čekajících migrací) a zahájí relaci.
        /// </summary>
        /// <exception cref="Errors.DataStoreException">Databáze a soubor klíčů k sobě nepatří apod.</exception>
        Task<LoginResult> LoginAsync(string email, string password);

        /// <summary>Ukončí relaci a přepíše klíč databáze v paměti nulami.</summary>
        void Logout();

        /// <summary>
        /// Dokončí vynucenou změnu hesla přihlášeného uživatele (<c>MustChangePassword</c>).
        /// Pokud ještě neexistuje záchranný kód, vygeneruje ho a vrátí k jednorázovému zobrazení.
        /// </summary>
        Task<PasswordChangeResult> CompleteForcedPasswordChangeAsync(string newPassword);

        /// <summary>Dobrovolná změna vlastního hesla – vyžaduje současné heslo.</summary>
        Task<PasswordChangeResult> ChangeOwnPasswordAsync(string currentPassword, string newPassword);

        /// <summary>
        /// Pojistka pro případ, že vydání záchranného kódu při první změně hesla nedoběhlo (pád aplikace):
        /// je-li přihlášen správce s vlastním heslem a žádný záchranný kód neexistuje, vygeneruje ho
        /// a vrátí k jednorázovému zobrazení. Jinak vrací <c>null</c> a nic nemění.
        /// </summary>
        Task<string> IssueMissingRecoveryCodeAsync();

        /// <summary>
        /// „Zapomenuté heslo“: záchranným kódem odemkne klíč databáze a znovu ho zabalí novým heslem
        /// zadaného aktivního správce. Nikoho nepřihlašuje.
        /// </summary>
        Task<OperationResult> RecoverAsync(string recoveryCode, string adminEmail, string newPassword);
    }
}
