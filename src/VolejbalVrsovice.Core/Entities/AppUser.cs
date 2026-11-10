using System;

namespace VolejbalVrsovice.Core.Entities
{
    /// <summary>Uživatel aplikace (pokladník, trenér). Není totéž co hráč.</summary>
    public sealed class AppUser : EntityBase
    {
        /// <summary>Přihlašovací e-mail – unikátní, ukládá se normalizovaný (malá písmena).</summary>
        public string Email { get; set; }

        public string DisplayName { get; set; }

        public UserRole Role { get; set; }

        public bool IsActive { get; set; }

        /// <summary>Uživatel musí po přihlášení změnit heslo (výchozí správce, nový uživatel, reset hesla).</summary>
        public bool MustChangePassword { get; set; }

        /// <summary>Okamžik posledního úspěšného přihlášení (UTC).</summary>
        public DateTime? LastLoginAt { get; set; }

        public bool IsAdmin
        {
            get { return Role == UserRole.Admin; }
        }
    }
}
