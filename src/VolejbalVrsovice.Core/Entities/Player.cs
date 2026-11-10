using System;

namespace VolejbalVrsovice.Core.Entities
{
    /// <summary>
    /// Hráč nebo hráčka. Saldo („konto“) se neukládá – vždy se počítá z příspěvků a nákladů na akce.
    /// </summary>
    public sealed class Player : EntityBase
    {
        public string FirstName { get; set; }

        public string LastName { get; set; }

        public Gender? Gender { get; set; }

        /// <summary>Datum narození (jen datum).</summary>
        public DateTime BirthDate { get; set; }

        /// <summary>Státní příslušnost – kód ISO 3166-1 alpha-2 z číselníku zemí, výchozí „CZ“.</summary>
        public string NationalityCode { get; set; }

        /// <summary>E-mail; alespoň jeden z kontaktů (e-mail, telefon) je povinný.</summary>
        public string Email { get; set; }

        public string Phone { get; set; }

        public string Address { get; set; }

        /// <summary>Datum nástupu do klubu (jen datum).</summary>
        public DateTime JoinedOn { get; set; }

        /// <summary>Aktivní v klubu. Neaktivní hráč se v družstvech zobrazuje jako neaktivní bez ohledu na členství.</summary>
        public bool IsActive { get; set; }

        /// <summary>Výška v cm (100–250).</summary>
        public int? HeightCm { get; set; }

        public string Note { get; set; }
    }
}
