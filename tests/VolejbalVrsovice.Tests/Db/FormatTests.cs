using System;
using VolejbalVrsovice.Core;
using VolejbalVrsovice.Data.Db;
using Xunit;

namespace VolejbalVrsovice.Tests.Db
{
    public class FormatTests
    {
        [Theory]
        [InlineData("0", 0)]
        [InlineData("1", 100)]
        [InlineData("1250.50", 125050)]
        [InlineData("0.01", 1)]
        [InlineData("-450", -45000)]
        [InlineData("-0.05", -5)]
        public void Money_converts_between_koruny_and_halere(string koruny, long halere)
        {
            var amount = decimal.Parse(koruny, System.Globalization.CultureInfo.InvariantCulture);

            Assert.Equal(halere, Money.ToHalere(amount));
            Assert.Equal(amount, Money.FromHalere(halere));
        }

        [Fact]
        public void Money_refuses_fractions_of_a_haler()
        {
            Assert.Throws<ArgumentException>(() => Money.ToHalere(10.001m));
        }

        [Fact]
        public void Date_without_time_is_stored_as_iso_date()
        {
            Assert.Equal("2026-03-05", DbFormats.FormatDateTime(new DateTime(2026, 3, 5)));
            Assert.Equal("2026-03-05", DbFormats.FormatDateTime(new DateTime(2026, 3, 5, 0, 0, 0, DateTimeKind.Local)));

            var parsed = DbFormats.ParseDateTime("2026-03-05");
            Assert.Equal(new DateTime(2026, 3, 5), parsed);
            Assert.Equal(DateTimeKind.Unspecified, parsed.Kind);
        }

        [Fact]
        public void Utc_instant_is_stored_as_iso_timestamp()
        {
            var instant = new DateTime(2026, 3, 5, 0, 0, 0, DateTimeKind.Utc);   // i půlnoc zůstává okamžikem

            Assert.Equal("2026-03-05T00:00:00Z", DbFormats.FormatDateTime(instant));
            Assert.Equal("2026-12-31T23:59:58Z", DbFormats.FormatDateTime(new DateTime(2026, 12, 31, 23, 59, 58, DateTimeKind.Utc)));

            var parsed = DbFormats.ParseDateTime("2026-12-31T23:59:58Z");
            Assert.Equal(new DateTime(2026, 12, 31, 23, 59, 58), parsed);
            Assert.Equal(DateTimeKind.Utc, parsed.Kind);
        }

        [Fact]
        public void Local_time_of_day_is_refused_instead_of_being_silently_dropped()
        {
            Assert.Throws<ArgumentException>(() => DbFormats.FormatDateTime(new DateTime(2026, 3, 5, 14, 30, 0)));
            Assert.Throws<ArgumentException>(() => DbFormats.FormatDateTime(new DateTime(2026, 3, 5, 14, 30, 0, DateTimeKind.Local)));
        }

        [Fact]
        public void Time_of_day_is_stored_as_hours_and_minutes()
        {
            Assert.Equal("07:05", DbFormats.FormatTimeOfDay(new TimeSpan(7, 5, 0)));
            Assert.Equal("23:59", DbFormats.FormatTimeOfDay(new TimeSpan(23, 59, 0)));
            Assert.Equal(new TimeSpan(19, 30, 0), DbFormats.ParseTimeOfDay("19:30"));
        }

        [Theory]
        [InlineData(24, 0, 0)]
        [InlineData(-1, 0, 0)]
        [InlineData(10, 0, 30)]
        public void Invalid_time_of_day_is_refused(int hours, int minutes, int seconds)
        {
            Assert.Throws<ArgumentException>(() => DbFormats.FormatTimeOfDay(new TimeSpan(hours, minutes, seconds)));
        }

        [Fact]
        public void Emails_are_normalized_to_lower_case()
        {
            Assert.Equal("jana.novakova@example.cz", EmailAddress.Normalize("  Jana.Novakova@Example.CZ "));
            Assert.Equal(string.Empty, EmailAddress.Normalize(null));
        }
    }
}
