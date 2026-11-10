using System;
using System.IO;

namespace VolejbalVrsovice.Data.Storage
{
    /// <summary>
    /// Cesty k souborům aplikace. Vše se odvozuje ze složky, ve které leží .exe
    /// (xcopy nasazení) – žádné %AppData%, žádný registr.
    /// </summary>
    public sealed class AppPaths
    {
        public AppPaths(string baseDirectory)
        {
            if (string.IsNullOrWhiteSpace(baseDirectory))
            {
                throw new ArgumentException("Základní složka aplikace musí být zadána.", nameof(baseDirectory));
            }

            BaseDirectory = Path.GetFullPath(baseDirectory);
            DataDirectory = Path.Combine(BaseDirectory, "Data");
            LogsDirectory = Path.Combine(DataDirectory, "logs");
            LockFile = Path.Combine(DataDirectory, "volejbal.lock");
            LockInfoFile = Path.Combine(DataDirectory, "volejbal.lock.info");
        }

        /// <summary>Složka s .exe.</summary>
        public string BaseDirectory { get; }

        /// <summary>Složka <c>Data</c> – vzniká při prvním spuštění, není součástí ZIPu.</summary>
        public string DataDirectory { get; }

        /// <summary>Složka s protokoly <c>Data\logs</c>.</summary>
        public string LogsDirectory { get; }

        /// <summary>Zámek běžící instance – o obsazení rozhoduje výhradně tento soubor.</summary>
        public string LockFile { get; }

        /// <summary>Informace o držiteli zámku (jen pro zprávu uživateli).</summary>
        public string LockInfoFile { get; }
    }
}
