using System;
using System.Globalization;

namespace VolejbalVrsovice.Core
{
    /// <summary>České řazení textu (Č za C, „ch“ za H…) – SQLite samo česky řadit neumí.</summary>
    public static class CzechText
    {
        public static readonly StringComparer Comparer =
            StringComparer.Create(new CultureInfo("cs-CZ", false), ignoreCase: true);
    }
}
