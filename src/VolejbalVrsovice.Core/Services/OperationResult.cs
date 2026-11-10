namespace VolejbalVrsovice.Core.Services
{
    /// <summary>
    /// Výsledek operace, která může skončit očekávaným odmítnutím (špatné heslo, obsazený e-mail…).
    /// <see cref="Error"/> je česká hláška určená přímo uživateli. Neočekávané chyby se hlásí výjimkou.
    /// </summary>
    public class OperationResult
    {
        protected OperationResult(string error)
        {
            Error = error;
        }

        public bool Succeeded
        {
            get { return Error == null; }
        }

        /// <summary>Důvod odmítnutí; <c>null</c> při úspěchu.</summary>
        public string Error { get; }

        public static OperationResult Success()
        {
            return new OperationResult(null);
        }

        public static OperationResult Failure(string error)
        {
            return new OperationResult(error ?? "Operaci se nepodařilo provést.");
        }
    }

    /// <summary>Výsledek změny hesla; při prvním nastavení hesla nese i nově vygenerovaný záchranný kód.</summary>
    public sealed class PasswordChangeResult : OperationResult
    {
        private PasswordChangeResult(string error, string recoveryCode)
            : base(error)
        {
            RecoveryCode = recoveryCode;
        }

        /// <summary>
        /// Záchranný kód k jednorázovému zobrazení (formát <c>XXXX-XXXX-…</c>), nebo <c>null</c>,
        /// pokud se při této změně hesla negeneroval.
        /// </summary>
        public string RecoveryCode { get; }

        public static PasswordChangeResult Changed(string recoveryCode = null)
        {
            return new PasswordChangeResult(null, recoveryCode);
        }

        public static new PasswordChangeResult Failure(string error)
        {
            return new PasswordChangeResult(error ?? "Heslo se nepodařilo změnit.", null);
        }
    }
}
