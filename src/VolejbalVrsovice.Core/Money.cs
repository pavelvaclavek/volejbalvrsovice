using System;

namespace VolejbalVrsovice.Core
{
    /// <summary>
    /// Peníze: v kódu <see cref="decimal"/> v Kč, v databázi celé číslo v haléřích. Nikdy <c>double</c>.
    /// </summary>
    public static class Money
    {
        private const decimal HalerePerKoruna = 100m;

        /// <summary>Převede částku v Kč na haléře. Částka smí mít nejvýše dvě desetinná místa.</summary>
        public static long ToHalere(decimal amount)
        {
            var halere = amount * HalerePerKoruna;
            if (halere != decimal.Truncate(halere))
            {
                throw new ArgumentException("Částka smí mít nejvýše dvě desetinná místa.", nameof(amount));
            }

            return decimal.ToInt64(halere);
        }

        /// <summary>Převede haléře na částku v Kč.</summary>
        public static decimal FromHalere(long halere)
        {
            return halere / HalerePerKoruna;
        }
    }
}
