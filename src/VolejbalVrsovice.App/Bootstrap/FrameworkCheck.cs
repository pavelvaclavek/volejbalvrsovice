using System;
using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;

namespace VolejbalVrsovice.App.Bootstrap
{
    /// <summary>
    /// Pojistka pro staré počítače: ověří, že je nainstalován .NET Framework 4.8.
    /// Záměrně používá jen API dostupná ve všech verzích .NET Frameworku 4.x a žádnou další knihovnu,
    /// aby proběhla i tam, kde by zbytek aplikace nenaběhl.
    /// </summary>
    public static class FrameworkCheck
    {
        /// <summary>Hodnota <c>Release</c> odpovídající .NET Frameworku 4.8.</summary>
        public const int Net48ReleaseKey = 528040;

        public const string DownloadUrl = "https://dotnet.microsoft.com/download/dotnet-framework/net48";

        private const string RegistryPath = @"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full";

        public static bool IsSupported(int? releaseKey)
        {
            return releaseKey.HasValue && releaseKey.Value >= Net48ReleaseKey;
        }

        /// <summary>Přečte hodnotu <c>Release</c> z registru; <c>null</c>, pokud chybí nebo ji nelze přečíst.</summary>
        public static int? ReadInstalledReleaseKey()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(RegistryPath))
                {
                    var value = key == null ? null : key.GetValue("Release");
                    return value is int ? (int)value : (int?)null;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Vrátí <c>true</c>, pokud je framework v pořádku. Jinak nabídne otevření stránky ke stažení
        /// a vrátí <c>false</c> – aplikace se má ukončit.
        /// </summary>
        public static bool EnsureSupported()
        {
            if (IsSupported(ReadInstalledReleaseKey()))
            {
                return true;
            }

            var answer = MessageBox.Show(
                "Aplikace vyžaduje .NET Framework 4.8. Chcete otevřít stránku ke stažení?",
                AppInfo.ApplicationName,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (answer == MessageBoxResult.Yes)
            {
                try
                {
                    Process.Start(DownloadUrl);
                }
                catch (Exception)
                {
                    // Bez výchozího prohlížeče není co otevřít; hláška už uživateli řekla, co chybí.
                }
            }

            return false;
        }
    }
}
