namespace VolejbalVrsovice.Core
{
    /// <summary>E-maily se porovnávají bez ohledu na velikost písmen – ukládá se normalizovaný tvar.</summary>
    public static class EmailAddress
    {
        /// <summary>Ořízne mezery a převede na malá písmena. Pro <c>null</c> vrací prázdný řetězec.</summary>
        public static string Normalize(string email)
        {
            return email == null ? string.Empty : email.Trim().ToLowerInvariant();
        }
    }
}
