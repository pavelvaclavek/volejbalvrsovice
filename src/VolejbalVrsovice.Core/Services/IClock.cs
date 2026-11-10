using System;

namespace VolejbalVrsovice.Core.Services
{
    /// <summary>Zdroj času – v testech nahraditelný.</summary>
    public interface IClock
    {
        /// <summary>Aktuální okamžik v UTC (<see cref="DateTimeKind.Utc"/>) – pro auditní sloupce.</summary>
        DateTime UtcNow { get; }

        /// <summary>Aktuální místní čas – jen pro zobrazení a názvy záloh.</summary>
        DateTime Now { get; }

        /// <summary>Dnešní datum bez času (<see cref="DateTimeKind.Unspecified"/>).</summary>
        DateTime Today { get; }
    }

    public sealed class SystemClock : IClock
    {
        public DateTime UtcNow
        {
            get { return DateTime.UtcNow; }
        }

        public DateTime Now
        {
            get { return DateTime.Now; }
        }

        public DateTime Today
        {
            get { return DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Unspecified); }
        }
    }
}
