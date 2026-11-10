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
            DatabaseFile = Path.Combine(DataDirectory, "volejbal.db");
            KeysFile = Path.Combine(DataDirectory, "keys.json");
            BackupDirectory = Path.Combine(DataDirectory, "backup");
            LogsDirectory = Path.Combine(DataDirectory, "logs");
            LockFile = Path.Combine(DataDirectory, "volejbal.lock");
            LockInfoFile = Path.Combine(DataDirectory, "volejbal.lock.info");
        }

        /// <summary>Složka s .exe.</summary>
        public string BaseDirectory { get; }

        /// <summary>Složka <c>Data</c> – vzniká při prvním spuštění, není součástí ZIPu.</summary>
        public string DataDirectory { get; }

        /// <summary>Šifrovaná databáze <c>Data\volejbal.db</c>.</summary>
        public string DatabaseFile { get; }

        /// <summary>Úložiště klíčů <c>Data\keys.json</c> – patří k databázi, zálohují se vždy spolu.</summary>
        public string KeysFile { get; }

        /// <summary>Zálohy <c>Data\backup\yyyyMMdd_HHmmss</c>.</summary>
        public string BackupDirectory { get; }

        /// <summary>Složka s protokoly <c>Data\logs</c>.</summary>
        public string LogsDirectory { get; }

        /// <summary>Zámek běžící instance – o obsazení rozhoduje výhradně tento soubor.</summary>
        public string LockFile { get; }

        /// <summary>Informace o držiteli zámku (jen pro zprávu uživateli).</summary>
        public string LockInfoFile { get; }
    }
}
