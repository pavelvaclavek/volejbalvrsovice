using System.Text.RegularExpressions;

namespace VolejbalVrsovice.Core
{
    /// <summary>E-maily se porovnávají bez ohledu na velikost písmen – ukládá se normalizovaný tvar.</summary>
    public static class EmailAddress
    {
        public const int MaxLength = 256;

        // Záměrně jen hrubá kontrola tvaru „něco@něco.něco“ bez mezer.
        private static readonly Regex Shape = new Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.CultureInvariant);

        /// <summary>Ořízne mezery a převede na malá písmena. Pro <c>null</c> vrací prázdný řetězec.</summary>
        public static string Normalize(string email)
        {
            return email == null ? string.Empty : email.Trim().ToLowerInvariant();
        }

        /// <summary><c>true</c>, pokud text vypadá jako e-mailová adresa a vejde se do 256 znaků.</summary>
        public static bool IsValid(string email)
        {
            var normalized = Normalize(email);
            return normalized.Length <= MaxLength && Shape.IsMatch(normalized);
        }
    }
}
