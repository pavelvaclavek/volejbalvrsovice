using System.Security.Cryptography;
using System.Text;

namespace VolejbalVrsovice.Core.Security
{
    /// <summary>
    /// Záchranný kód: 24 znaků z abecedy bez zaměnitelných znaků (chybí I, O, 0, 1), zobrazovaný jako
    /// <c>XXXX-XXXX-XXXX-XXXX-XXXX-XXXX</c>. Abeceda má 32 znaků, kód tedy nese 120 bitů náhody.
    /// Kód se nikam neukládá – ukládá se jen klíč databáze, který je jím zabalený.
    /// </summary>
    public static class RecoveryCode
    {
        public const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        public const int Length = 24;
        public const int GroupSize = 4;

        /// <summary>Vygeneruje nový kód ve formátu pro zobrazení (s pomlčkami).</summary>
        public static string Generate()
        {
            var bytes = new byte[Length];
            using (var random = RandomNumberGenerator.Create())
            {
                random.GetBytes(bytes);
            }

            var code = new StringBuilder(Length);
            foreach (var value in bytes)
            {
                // 256 je násobek 32, takže zbytek po dělení je rovnoměrně rozložený.
                code.Append(Alphabet[value % Alphabet.Length]);
            }

            return Format(code.ToString());
        }

        /// <summary>Rozdělí 24 znaků do skupin po čtyřech.</summary>
        public static string Format(string normalizedCode)
        {
            var formatted = new StringBuilder(Length + (Length / GroupSize) - 1);
            for (var i = 0; i < normalizedCode.Length; i++)
            {
                if (i > 0 && i % GroupSize == 0)
                {
                    formatted.Append('-');
                }

                formatted.Append(normalizedCode[i]);
            }

            return formatted.ToString();
        }

        /// <summary>
        /// Převede kód zadaný uživatelem na kanonický tvar (24 velkých znaků bez oddělovačů) – právě
        /// ten se používá jako tajemství. Vrátí <c>null</c>, pokud vstup platný kód není.
        /// </summary>
        public static string Normalize(string input)
        {
            if (input == null)
            {
                return null;
            }

            var code = new StringBuilder(Length);
            foreach (var character in input)
            {
                if (character == '-' || char.IsWhiteSpace(character))
                {
                    continue;
                }

                var upper = char.ToUpperInvariant(character);
                if (Alphabet.IndexOf(upper) < 0)
                {
                    return null;
                }

                code.Append(upper);
            }

            return code.Length == Length ? code.ToString() : null;
        }
    }
}
