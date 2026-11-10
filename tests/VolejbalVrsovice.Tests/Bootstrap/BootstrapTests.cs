using System;
using VolejbalVrsovice.App;
using VolejbalVrsovice.App.Bootstrap;
using VolejbalVrsovice.Data.Storage;
using Xunit;

namespace VolejbalVrsovice.Tests.Bootstrap
{
    public class BootstrapTests
    {
        [Theory]
        [InlineData(528040, true)]   // .NET Framework 4.8
        [InlineData(533320, true)]   // 4.8.1
        [InlineData(528039, false)]
        [InlineData(461808, false)]  // 4.7.2
        public void Framework_check_requires_release_key_of_net48(int releaseKey, bool expected)
        {
            Assert.Equal(expected, FrameworkCheck.IsSupported(releaseKey));
        }

        [Fact]
        public void Framework_check_fails_when_release_key_is_missing()
        {
            Assert.False(FrameworkCheck.IsSupported(null));
        }

        [Fact]
        public void Framework_check_reads_the_installed_release_key()
        {
            // Testy samy běží na .NET Frameworku 4.8, takže hodnota v registru musí vyhovět.
            Assert.True(FrameworkCheck.IsSupported(FrameworkCheck.ReadInstalledReleaseKey()));
        }

        [Fact]
        public void Already_running_message_names_user_machine_and_time()
        {
            var localStart = new DateTime(2026, 3, 5, 14, 7, 0);
            var holder = new LockHolderInfo("PC-POKLADNA", "jnovak",
                new DateTimeOffset(localStart, TimeZoneInfo.Local.GetUtcOffset(localStart)));

            var message = StartupMessages.AlreadyRunning(holder);

            Assert.StartsWith("Aplikaci právě používá jnovak na PC-POKLADNA od 5. 3. 2026 14:07.", message);
        }

        [Fact]
        public void Already_running_message_works_without_holder_info()
        {
            var message = StartupMessages.AlreadyRunning(null);

            Assert.StartsWith("Aplikaci právě používá jiný uživatel.", message);
        }

        [Fact]
        public void Ui_culture_formats_money_and_dates_the_czech_way()
        {
            var culture = UiCulture.Create();

            // Oddělovač tisíců je nedělitelná mezera (U+00A0), před „Kč“ je mezera obyčejná.
            Assert.Equal("1 250,00 Kč", 1250m.ToString("C", culture));
            Assert.Equal("-300,00 Kč", (-300m).ToString("C", culture));
            Assert.Equal("5. 3. 2026", new DateTime(2026, 3, 5).ToString("d", culture));
        }
    }
}
