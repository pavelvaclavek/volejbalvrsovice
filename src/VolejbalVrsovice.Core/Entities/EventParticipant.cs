namespace VolejbalVrsovice.Core.Entities
{
    /// <summary>
    /// Účast hráče na akci (M:N). Družstvo se neukládá – účast je vazba akce–hráč;
    /// dvojice akce–hráč je unikátní.
    /// </summary>
    public sealed class EventParticipant : EntityBase
    {
        public int EventId { get; set; }

        public int PlayerId { get; set; }

        /// <summary>Náklad účastníka v Kč (≥ 0). Při přidání = výchozí náklad akce, dále jde upravit.</summary>
        public decimal Cost { get; set; }

        /// <summary>
        /// <c>true</c>, jakmile uživatel náklad ručně změnil. Takový náklad se při změně výchozího
        /// nákladu akce nikdy nepřepisuje automaticky.
        /// </summary>
        public bool IsCostOverridden { get; set; }

        /// <summary>Důvod úpravy (např. „jen večeře“).</summary>
        public string Note { get; set; }
    }
}
