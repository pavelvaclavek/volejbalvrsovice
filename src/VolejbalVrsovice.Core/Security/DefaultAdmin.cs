namespace VolejbalVrsovice.Core.Security
{
    /// <summary>
    /// Výchozí správce, který se založí při prvním spuštění. Po prvním přihlášení musí heslo změnit.
    /// Na přihlašovací obrazovce se tyto údaje nezobrazují – jsou jen v návodu.
    /// </summary>
    public static class DefaultAdmin
    {
        public const string Email = "admin@admin.cz";

        /// <summary>Jediná výjimka z pravidla „heslo min. 8 znaků“ – vzniká jen seedem, nikdy přes UI.</summary>
        public const string Password = "admin";

        public const string DisplayName = "Správce";
    }
}
