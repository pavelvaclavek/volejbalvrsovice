using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using VolejbalVrsovice.Core.Services;
using VolejbalVrsovice.Data.Storage;

namespace VolejbalVrsovice.Data.Db
{
    /// <summary>
    /// Zálohy do <c>Data\backup\yyyyMMdd_HHmmss\</c>. Zálohuje se vždy dvojice <c>volejbal.db</c> +
    /// <c>keys.json</c> (jedno bez druhého je k ničemu); databáze v záloze zůstává šifrovaná stejným
    /// klíčem. Uchovává se posledních <see cref="KeepCount"/> záloh.
    /// </summary>
    public sealed class BackupService
    {
        public const int KeepCount = 10;

        private const string FolderFormat = "yyyyMMdd_HHmmss";
        private static readonly Regex FolderName = new Regex(@"^\d{8}_\d{6}(_\d+)?$", RegexOptions.CultureInvariant);

        private readonly AppPaths _paths;
        private readonly IClock _clock;

        public BackupService(AppPaths paths, IClock clock)
        {
            _paths = paths ?? throw new ArgumentNullException(nameof(paths));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        /// <summary>
        /// Zkopíruje databázi a soubor klíčů do nové složky zálohy a smaže nejstarší zálohy nad limit.
        /// Volat jen ve chvíli, kdy není otevřené žádné spojení na databázi.
        /// </summary>
        /// <returns>Cesta k vytvořené záloze.</returns>
        public string CreateBackup()
        {
            Directory.CreateDirectory(_paths.BackupDirectory);

            var name = _clock.Now.ToString(FolderFormat, CultureInfo.InvariantCulture);
            var target = Path.Combine(_paths.BackupDirectory, name);
            for (var suffix = 2; Directory.Exists(target); suffix++)
            {
                target = Path.Combine(_paths.BackupDirectory, name + "_" + suffix.ToString(CultureInfo.InvariantCulture));
            }

            Directory.CreateDirectory(target);

            File.Copy(_paths.DatabaseFile, Path.Combine(target, Path.GetFileName(_paths.DatabaseFile)));
            File.Copy(_paths.KeysFile, Path.Combine(target, Path.GetFileName(_paths.KeysFile)));

            // Nedokončená transakce po pádu aplikace je v žurnálu – bez něj by záloha nebyla úplná.
            var journal = _paths.DatabaseFile + "-journal";
            if (File.Exists(journal))
            {
                File.Copy(journal, Path.Combine(target, Path.GetFileName(journal)));
            }

            Prune();
            return target;
        }

        private void Prune()
        {
            var obsolete = new DirectoryInfo(_paths.BackupDirectory)
                .GetDirectories()
                .Where(directory => FolderName.IsMatch(directory.Name))
                .OrderByDescending(directory => directory.Name, StringComparer.Ordinal)
                .Skip(KeepCount);

            foreach (var directory in obsolete)
            {
                try
                {
                    directory.Delete(recursive: true);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    // Starou zálohu se nepodařilo smazat – zkusí se to při příští záloze.
                }
            }
        }
    }
}
