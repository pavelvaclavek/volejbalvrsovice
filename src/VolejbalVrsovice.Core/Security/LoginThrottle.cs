using System;
using VolejbalVrsovice.Core.Services;

namespace VolejbalVrsovice.Core.Security
{
    /// <summary>
    /// Blokace přihlášení: po 5 neúspěšných pokusech se přihlášení na 5 minut zablokuje.
    /// Stav je jen v paměti běžící aplikace (zadání, sekce 5).
    /// </summary>
    public sealed class LoginThrottle
    {
        public const int MaxFailedAttempts = 5;

        public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(5);

        private readonly IClock _clock;
        private readonly object _gate = new object();
        private int _failedAttempts;
        private DateTime? _lockedUntilUtc;

        public LoginThrottle(IClock clock)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        /// <summary>Počet neúspěšných pokusů od posledního úspěchu nebo konce blokace.</summary>
        public int FailedAttempts
        {
            get
            {
                lock (_gate)
                {
                    return _failedAttempts;
                }
            }
        }

        /// <summary>Jak dlouho ještě blokace potrvá; <c>null</c>, pokud přihlášení blokované není.</summary>
        public TimeSpan? GetRemainingLockout()
        {
            lock (_gate)
            {
                if (!_lockedUntilUtc.HasValue)
                {
                    return null;
                }

                var remaining = _lockedUntilUtc.Value - _clock.UtcNow;
                if (remaining > TimeSpan.Zero)
                {
                    return remaining;
                }

                _lockedUntilUtc = null;
                return null;
            }
        }

        /// <summary>Zaznamená neúspěšný pokus. Vrací <c>true</c>, pokud jím právě začala blokace.</summary>
        public bool RegisterFailure()
        {
            lock (_gate)
            {
                _failedAttempts++;
                if (_failedAttempts < MaxFailedAttempts)
                {
                    return false;
                }

                _failedAttempts = 0;
                _lockedUntilUtc = _clock.UtcNow + LockoutDuration;
                return true;
            }
        }

        public void RegisterSuccess()
        {
            lock (_gate)
            {
                _failedAttempts = 0;
                _lockedUntilUtc = null;
            }
        }
    }
}
