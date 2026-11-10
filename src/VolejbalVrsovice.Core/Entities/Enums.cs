namespace VolejbalVrsovice.Core.Entities
{
    // Hodnoty výčtů se ukládají do databáze jako čísla – neměnit je, jen přidávat nové.

    /// <summary>Role uživatele aplikace.</summary>
    public enum UserRole
    {
        /// <summary>Uživatel – vše kromě správy uživatelů.</summary>
        User = 0,

        /// <summary>Správce – vše včetně správy uživatelů.</summary>
        Admin = 1,
    }

    /// <summary>Pohlaví hráče („hráči/hráčky“).</summary>
    public enum Gender
    {
        Male = 1,
        Female = 2,
    }

    /// <summary>Způsob úhrady příspěvku.</summary>
    public enum PaymentMethod
    {
        /// <summary>Bankovní převod.</summary>
        BankTransfer = 1,

        /// <summary>Hotovost.</summary>
        Cash = 2,

        /// <summary>
        /// Převod salda – počáteční zůstatek z dosavadní evidence. Nejde o skutečný příjem peněz,
        /// částka může být i záporná (dluh) a hráč smí mít nejvýše jeden takový záznam.
        /// </summary>
        BalanceTransfer = 3,
    }
}
