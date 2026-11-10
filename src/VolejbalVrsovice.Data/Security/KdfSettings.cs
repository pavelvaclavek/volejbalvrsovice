using System;

namespace VolejbalVrsovice.Data.Security
{
    /// <summary>
    /// Nastavení odvození klíče z hesla. Aplikace vždy používá <see cref="Default"/>
    /// (600 000 iterací); nižší počet je určen jen pro rychlé automatické testy.
    /// </summary>
    public sealed class KdfSettings
    {
        public KdfSettings(int iterations)
        {
            if (iterations < 1) throw new ArgumentOutOfRangeException(nameof(iterations));

            Iterations = iterations;
        }

        public static KdfSettings Default
        {
            get { return new KdfSettings(KeyEnvelope.DefaultIterations); }
        }

        /// <summary>Počet iterací PBKDF2 pro nově zapisované záznamy.</summary>
        public int Iterations { get; }
    }
}
