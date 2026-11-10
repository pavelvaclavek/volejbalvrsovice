using System;
using System.IO;
using Serilog;

namespace VolejbalVrsovice.App.Bootstrap
{
    /// <summary>Protokolování do <c>Data\logs\app-yyyyMMdd.log</c> (denní soubory, uchovává se 14 dní).</summary>
    public static class LoggingSetup
    {
        public const int RetainedDays = 14;

        public static ILogger CreateLogger(string logsDirectory)
        {
            Directory.CreateDirectory(logsDirectory);

            // Pozor: do protokolu nikdy nepatří osobní údaje hráčů, hesla ani klíče – jen ID entit.
            return new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File(
                    path: Path.Combine(logsDirectory, "app-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: RetainedDays,
                    retainedFileTimeLimit: TimeSpan.FromDays(RetainedDays),
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();
        }
    }
}
