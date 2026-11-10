using System;
using System.Globalization;

namespace VolejbalVrsovice.Data.Db
{
    /// <summary>
    /// Textové formáty, ve kterých se v SQLite ukládá datum a čas.
    /// <para>
    /// Typ <see cref="DateTime"/> má v aplikaci dva významy a rozlišuje je <see cref="DateTime.Kind"/>:
    /// <list type="bullet">
    /// <item><b>datum bez času</b> (narození, datum příspěvku…) – <see cref="DateTimeKind.Unspecified"/>
    /// nebo <c>Local</c> s nulovým časem, ukládá se jako <c>yyyy-MM-dd</c>;</item>
    /// <item><b>okamžik</b> (auditní sloupce, poslední přihlášení) – vždy <see cref="DateTimeKind.Utc"/>,
    /// ukládá se jako <c>yyyy-MM-ddTHH:mm:ssZ</c>.</item>
    /// </list>
    /// </para>
    /// </summary>
    public static class DbFormats
    {
        public const string DateFormat = "yyyy-MM-dd";
        public const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";
        public const string TimeOfDayFormat = @"hh\:mm";

        public static string FormatDateTime(DateTime value)
        {
            if (value.Kind == DateTimeKind.Utc)
            {
                return value.ToString(TimestampFormat, CultureInfo.InvariantCulture);
            }

            if (value.TimeOfDay != TimeSpan.Zero)
            {
                throw new ArgumentException(
                    "Datum bez času nesmí obsahovat čas; okamžik musí být v UTC (DateTimeKind.Utc).", nameof(value));
            }

            return value.ToString(DateFormat, CultureInfo.InvariantCulture);
        }

        public static DateTime ParseDateTime(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));

            if (text.Length == DateFormat.Length)
            {
                return DateTime.ParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None);
            }

            return DateTime.ParseExact(text, TimestampFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        }

        /// <summary>Čas dne jako <c>HH:mm</c> – jen celé minuty v rozsahu 00:00–23:59.</summary>
        public static string FormatTimeOfDay(TimeSpan value)
        {
            if (value < TimeSpan.Zero || value >= TimeSpan.FromDays(1) || value.Ticks % TimeSpan.TicksPerMinute != 0)
            {
                throw new ArgumentException("Čas dne musí být v rozsahu 00:00–23:59 a v celých minutách.", nameof(value));
            }

            return value.ToString(TimeOfDayFormat, CultureInfo.InvariantCulture);
        }

        public static TimeSpan ParseTimeOfDay(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));

            return TimeSpan.ParseExact(text, TimeOfDayFormat, CultureInfo.InvariantCulture);
        }

        /// <summary>Okamžiky se ukládají s přesností na sekundy – takhle se shoduje hodnota v paměti a v DB.</summary>
        public static DateTime TruncateToSeconds(DateTime value)
        {
            return new DateTime(value.Ticks - (value.Ticks % TimeSpan.TicksPerSecond), value.Kind);
        }
    }
}
