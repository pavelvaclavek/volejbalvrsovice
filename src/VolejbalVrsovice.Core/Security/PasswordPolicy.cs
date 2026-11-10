using System;

namespace VolejbalVrsovice.Core.Security
{
    /// <summary>
    /// Pravidla pro hesla zadávaná přes UI: alespoň 8 znaků a nesmí to být výchozí heslo „admin“.
    /// (Výchozí heslo správce je jediná výjimka – vzniká jen seedem.)
    /// </summary>
    public static class PasswordPolicy
    {
        public const int MinLength = 8;

        /// <summary>Vrátí českou chybovou hlášku, nebo <c>null</c>, pokud heslo vyhovuje.</summary>
        public static string Validate(string password)
        {
            if (string.IsNullOrEmpty(password))
            {
                return "Zadejte heslo.";
            }

            if (string.Equals(password, DefaultAdmin.Password, StringComparison.OrdinalIgnoreCase))
            {
                return "Heslo nesmí být „admin“.";
            }

            if (password.Length < MinLength)
            {
                return "Heslo musí mít alespoň " + MinLength + " znaků.";
            }

            return null;
        }
    }
}
