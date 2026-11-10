using System;

namespace VolejbalVrsovice.Core.Entities
{
    /// <summary>
    /// Členství hráče v družstvu (M:N). Hráč může být ve více družstvech; dvojice hráč–družstvo je unikátní.
    /// Ukončené členství se nemaže, jen se vyplní <see cref="LeftOn"/>.
    /// </summary>
    public sealed class TeamMembership : EntityBase
    {
        public int PlayerId { get; set; }

        public int TeamId { get; set; }

        /// <summary>Datum nástupu do družstva (jen datum).</summary>
        public DateTime JoinedOn { get; set; }

        /// <summary>Datum odchodu z družstva; <c>null</c> = hráč v družstvu stále je.</summary>
        public DateTime? LeftOn { get; set; }

        public bool IsCurrent
        {
            get { return !LeftOn.HasValue; }
        }
    }
}
