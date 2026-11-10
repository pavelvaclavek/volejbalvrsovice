using System;
using System.IO;
using System.Security;

namespace VolejbalVrsovice.Data.Storage
{
    /// <summary>Kontrola složky <c>Data</c> při startu aplikace.</summary>
    public static class DataFolder
    {
        /// <summary>
        /// Vytvoří složku <c>Data</c> (pokud chybí) a ověří, že do ní lze zapisovat –
        /// skutečným založením a smazáním zkušebního souboru.
        /// </summary>
        /// <returns><c>false</c>, pokud zapisovat nelze (např. složka v Program Files).</returns>
        public static bool TryEnsureWritable(AppPaths paths, out string error)
        {
            if (paths == null) throw new ArgumentNullException(nameof(paths));

            try
            {
                Directory.CreateDirectory(paths.DataDirectory);

                var probe = Path.Combine(paths.DataDirectory, ".zapis-" + Guid.NewGuid().ToString("N") + ".tmp");
                using (var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    bufferSize: 1, options: FileOptions.DeleteOnClose))
                {
                    stream.WriteByte(0);
                    stream.Flush();
                }

                error = null;
                return true;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException
                                       || ex is IOException
                                       || ex is SecurityException
                                       || ex is NotSupportedException)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
