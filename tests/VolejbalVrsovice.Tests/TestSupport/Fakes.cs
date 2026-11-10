using System;
using VolejbalVrsovice.Core.Services;
using VolejbalVrsovice.Data.Db;

namespace VolejbalVrsovice.Tests.TestSupport
{
    /// <summary>Hodiny, které jdou ručně posouvat.</summary>
    internal sealed class FakeClock : IClock
    {
        public FakeClock()
            : this(new DateTime(2026, 3, 5, 9, 30, 15, DateTimeKind.Utc))
        {
        }

        public FakeClock(DateTime utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTime UtcNow { get; set; }

        public DateTime Now
        {
            get { return UtcNow.ToLocalTime(); }
        }

        public DateTime Today
        {
            get { return DateTime.SpecifyKind(Now.Date, DateTimeKind.Unspecified); }
        }

        public void Advance(TimeSpan by)
        {
            UtcNow = UtcNow.Add(by);
        }
    }

    internal sealed class FakeCurrentUser : ICurrentUser
    {
        public int? UserId { get; set; }
    }

    internal sealed class FixedKeySource : IDatabaseKeySource
    {
        private readonly byte[] _key;

        public FixedKeySource(byte[] key)
        {
            _key = key;
        }

        // Stejně jako relace vrací kopii – volající ji po použití maže.
        public byte[] GetKey()
        {
            return (byte[])_key.Clone();
        }
    }
}
