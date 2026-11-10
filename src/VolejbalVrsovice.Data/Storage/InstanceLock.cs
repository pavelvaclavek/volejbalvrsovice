using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace VolejbalVrsovice.Data.Storage
{
    /// <summary>Kdo drží zámek aplikace – jen informace pro zprávu uživateli.</summary>
    public sealed class LockHolderInfo
    {
        public LockHolderInfo(string machineName, string userName, DateTimeOffset? startedAt)
        {
            MachineName = machineName;
            UserName = userName;
            StartedAt = startedAt;
        }

        public string MachineName { get; }

        public string UserName { get; }

        public DateTimeOffset? StartedAt { get; }
    }

    /// <summary>Výsledek pokusu o získání zámku.</summary>
    public sealed class InstanceLockResult
    {
        private InstanceLockResult(InstanceLock instanceLock, LockHolderInfo holder)
        {
            Lock = instanceLock;
            Holder = holder;
        }

        /// <summary><c>true</c>, pokud tato instance zámek získala.</summary>
        public bool Acquired => Lock != null;

        /// <summary>Získaný zámek; držet po celou dobu běhu a na konci uvolnit (<see cref="IDisposable.Dispose"/>).</summary>
        public InstanceLock Lock { get; }

        /// <summary>Držitel zámku, pokud je obsazený; <c>null</c>, když se informace nepodařilo přečíst.</summary>
        public LockHolderInfo Holder { get; }

        internal static InstanceLockResult Success(InstanceLock instanceLock) => new InstanceLockResult(instanceLock, null);

        internal static InstanceLockResult Busy(LockHolderInfo holder) => new InstanceLockResult(null, holder);
    }

    /// <summary>
    /// Zámek proti souběžnému spuštění nad stejnou složkou <c>Data</c> (i ze dvou počítačů přes síťový disk).
    /// O obsazení rozhoduje výhradně exkluzivně otevřený soubor <c>volejbal.lock</c> – po pádu procesu
    /// ho operační systém uvolní sám. Soubor <c>volejbal.lock.info</c> slouží jen pro text zprávy.
    /// </summary>
    public sealed class InstanceLock : IDisposable
    {
        private const int ErrorSharingViolation = 32;
        private const int ErrorLockViolation = 33;

        private readonly string _lockFile;
        private readonly string _infoFile;
        private FileStream _stream;

        private InstanceLock(FileStream stream, string lockFile, string infoFile)
        {
            _stream = stream;
            _lockFile = lockFile;
            _infoFile = infoFile;
        }

        public static InstanceLockResult TryAcquire(AppPaths paths)
        {
            if (paths == null) throw new ArgumentNullException(nameof(paths));

            return TryAcquire(paths, new LockHolderInfo(Environment.MachineName, Environment.UserName, DateTimeOffset.Now));
        }

        /// <summary>Varianta s explicitní identitou držitele (pro testy).</summary>
        public static InstanceLockResult TryAcquire(AppPaths paths, LockHolderInfo self)
        {
            if (paths == null) throw new ArgumentNullException(nameof(paths));
            if (self == null) throw new ArgumentNullException(nameof(self));

            Directory.CreateDirectory(paths.DataDirectory);

            FileStream stream;
            try
            {
                stream = new FileStream(paths.LockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException ex) when (IsSharingViolation(ex))
            {
                return InstanceLockResult.Busy(TryReadInfo(paths.LockInfoFile));
            }

            WriteInfo(paths.LockInfoFile, self);
            return InstanceLockResult.Success(new InstanceLock(stream, paths.LockFile, paths.LockInfoFile));
        }

        public void Dispose()
        {
            var stream = _stream;
            if (stream == null)
            {
                return;
            }

            _stream = null;

            // Info soubor mazat ještě pod zámkem – jinak bychom mohli smazat údaje instance,
            // která se spustila hned po nás.
            TryDelete(_infoFile);
            stream.Dispose();
            TryDelete(_lockFile);
        }

        private static bool IsSharingViolation(IOException ex)
        {
            var code = ex.HResult & 0xFFFF;
            return code == ErrorSharingViolation || code == ErrorLockViolation;
        }

        private static void WriteInfo(string infoFile, LockHolderInfo self)
        {
            var text = new StringBuilder()
                .Append("machine=").AppendLine(self.MachineName)
                .Append("user=").AppendLine(self.UserName)
                .Append("started=").AppendLine(self.StartedAt?.ToString("o", CultureInfo.InvariantCulture))
                .ToString();

            try
            {
                // Info soubor není zámek: ostatní instance ho musí umět přečíst, i když běžíme.
                using (var stream = new FileStream(infoFile, FileMode.Create, FileAccess.Write, FileShare.Read))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    writer.Write(text);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Nepodstatné – druhá instance jen neuvidí, kdo aplikaci drží.
            }
        }

        private static LockHolderInfo TryReadInfo(string infoFile)
        {
            try
            {
                string machine = null;
                string user = null;
                DateTimeOffset? started = null;

                using (var stream = new FileStream(infoFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        var separator = line.IndexOf('=');
                        if (separator <= 0)
                        {
                            continue;
                        }

                        var key = line.Substring(0, separator).Trim();
                        var value = line.Substring(separator + 1).Trim();
                        if (value.Length == 0)
                        {
                            continue;
                        }

                        if (key == "machine")
                        {
                            machine = value;
                        }
                        else if (key == "user")
                        {
                            user = value;
                        }
                        else if (key == "started"
                                 && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
                        {
                            started = parsed;
                        }
                    }
                }

                return machine == null && user == null && started == null
                    ? null
                    : new LockHolderInfo(machine, user, started);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Zbylý soubor ničemu nevadí – příští spuštění ho znovu otevře.
            }
        }
    }
}
