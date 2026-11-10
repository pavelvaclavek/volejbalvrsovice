using System;

namespace VolejbalVrsovice.Core.Entities
{
    /// <summary>
    /// Společný základ entit. Auditní sloupce vyplňuje repozitář při vložení a úpravě,
    /// nikdy volající kód.
    /// </summary>
    public abstract class EntityBase
    {
        public int Id { get; set; }

        /// <summary>Okamžik vytvoření (UTC).</summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>Id uživatele aplikace (<see cref="AppUser"/>), který záznam vytvořil; <c>null</c> = systém.</summary>
        public int? CreatedBy { get; set; }

        /// <summary>Okamžik poslední změny (UTC).</summary>
        public DateTime ModifiedAt { get; set; }

        /// <summary>Id uživatele aplikace, který záznam naposledy změnil; <c>null</c> = systém.</summary>
        public int? ModifiedBy { get; set; }
    }
}
