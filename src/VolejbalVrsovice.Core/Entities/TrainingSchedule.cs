using System;

namespace VolejbalVrsovice.Core.Entities
{
    /// <summary>
    /// Pravidelný trénink družstva. Družstvo může mít více tréninků i ve stejný den
    /// (různý čas nebo adresa).
    /// </summary>
    public sealed class TrainingSchedule : EntityBase
    {
        public int TeamId { get; set; }

        public DayOfWeek DayOfWeek { get; set; }

        /// <summary>Začátek (čas dne, celé minuty).</summary>
        public TimeSpan TimeFrom { get; set; }

        /// <summary>Konec (čas dne, celé minuty) – musí být později než <see cref="TimeFrom"/>.</summary>
        public TimeSpan TimeTo { get; set; }

        /// <summary>Název haly a adresa.</summary>
        public string Address { get; set; }

        public string Note { get; set; }
    }
}
