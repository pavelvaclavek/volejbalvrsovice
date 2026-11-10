using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Xml;
using Serilog;
using Serilog.Core;
using VolejbalVrsovice.Core;
using VolejbalVrsovice.Core.Errors;

namespace VolejbalVrsovice.Data.Security
{
    /// <summary>
    /// Úložiště klíčů <c>Data\keys.json</c> – nešifrovaný soubor vedle databáze (musí být mimo ni,
    /// jinak by nešla odemknout). Obsahuje jen e-maily uživatelů aplikace a klíč databáze zašifrovaný
    /// heslem každého z nich, případně záchranným kódem. Nic dalšího sem nepatří.
    /// <para>
    /// Zápis je atomický: <c>keys.json.tmp</c> → <c>File.Replace</c> se zálohou <c>keys.json.bak</c>.
    /// Záloha slouží jen po dobu výměny (kdyby výměna selhala v půli, zůstane v ní předchozí verze)
    /// a po úspěšném zápisu se hned maže – jinak by v ní dál ležely záznamy, které už platit nemají.
    /// </para>
    /// </summary>
    public sealed class KeyStore
    {
        public const int FormatVersion = 1;

        // Soubor, který se právě přejmenoval, může na okamžik držet jiný program (antivirus, synchronizace).
        private const int BackupDeleteAttempts = 3;
        private const int BackupDeleteRetryDelayMs = 50;

        private static readonly DataContractJsonSerializer Serializer =
            new DataContractJsonSerializer(typeof(KeyStoreDocument));

        private readonly object _gate = new object();
        private readonly string _file;
        private readonly ILogger _logger;

        public KeyStore(string file)
            : this(file, Logger.None)
        {
        }

        public KeyStore(string file, ILogger logger)
        {
            if (string.IsNullOrWhiteSpace(file)) throw new ArgumentException("Cesta k souboru klíčů musí být zadána.", nameof(file));

            _file = file;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public string FilePath
        {
            get { return _file; }
        }

        public bool Exists
        {
            get { return File.Exists(_file); }
        }

        /// <summary>
        /// Založí nový soubor klíčů s jediným uživatelem (první spuštění). Existující soubor nikdy
        /// nepřepíše – v takovém případě vyhodí <see cref="IOException"/>.
        /// </summary>
        public void Create(Guid dbId, string email, WrappedKey key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            var document = new KeyStoreDocument
            {
                FormatVersion = FormatVersion,
                DbId = dbId.ToString("D"),
                Users = new List<KeyStoreUserEntry> { KeyStoreUserEntry.From(RequireEmail(email), key) },
            };

            lock (_gate)
            {
                if (File.Exists(_file))
                {
                    throw new IOException("Soubor klíčů už existuje – nebude přepsán.");
                }

                Write(document, replaceExisting: false);
            }
        }

        /// <summary>Identifikátor databáze, ke které soubor klíčů patří.</summary>
        public Guid GetDbId()
        {
            lock (_gate)
            {
                return Guid.Parse(Read().DbId);
            }
        }

        public IReadOnlyList<string> GetUserEmails()
        {
            lock (_gate)
            {
                return Read().Users.Select(user => user.Email).ToList();
            }
        }

        /// <summary>Zabalený klíč uživatele, nebo <c>null</c>, pokud uživatel v souboru není.</summary>
        public WrappedKey FindUser(string email)
        {
            var normalized = EmailAddress.Normalize(email);

            lock (_gate)
            {
                var entry = Read().Users.FirstOrDefault(user => IsSameEmail(user.Email, normalized));
                return entry == null ? null : entry.ToWrappedKey();
            }
        }

        /// <summary>Klíč zabalený záchranným kódem, nebo <c>null</c>, pokud kód ještě nebyl vygenerován.</summary>
        public WrappedKey GetRecovery()
        {
            lock (_gate)
            {
                var recovery = Read().Recovery;
                return recovery == null ? null : recovery.ToWrappedKey();
            }
        }

        /// <summary>Přidá uživatele nebo nahradí jeho dosavadní záznam (změna a reset hesla).</summary>
        public void SetUser(string email, WrappedKey key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            var normalized = RequireEmail(email);

            lock (_gate)
            {
                var document = Read();
                var entry = KeyStoreUserEntry.From(normalized, key);
                var index = document.Users.FindIndex(user => IsSameEmail(user.Email, normalized));

                if (index >= 0)
                {
                    document.Users[index] = entry;
                }
                else
                {
                    document.Users.Add(entry);
                }

                Write(document, replaceExisting: true);
            }
        }

        /// <summary>Odstraní záznam uživatele (deaktivace). Vrací <c>false</c>, pokud v souboru nebyl.</summary>
        public bool RemoveUser(string email)
        {
            var normalized = EmailAddress.Normalize(email);

            lock (_gate)
            {
                var document = Read();
                if (document.Users.RemoveAll(user => IsSameEmail(user.Email, normalized)) == 0)
                {
                    return false;
                }

                Write(document, replaceExisting: true);
                return true;
            }
        }

        /// <summary>Uloží klíč zabalený záchranným kódem; dosavadní kód tím přestává platit.</summary>
        public void SetRecovery(WrappedKey key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            lock (_gate)
            {
                var document = Read();
                document.Recovery = KeyStoreEntry.From(key);
                Write(document, replaceExisting: true);
            }
        }

        private static string RequireEmail(string email)
        {
            var normalized = EmailAddress.Normalize(email);
            if (normalized.Length == 0)
            {
                throw new ArgumentException("E-mail musí být zadán.", nameof(email));
            }

            return normalized;
        }

        private static bool IsSameEmail(string stored, string normalized)
        {
            return string.Equals(EmailAddress.Normalize(stored), normalized, StringComparison.Ordinal);
        }

        private KeyStoreDocument Read()
        {
            KeyStoreDocument document;

            try
            {
                using (var stream = new FileStream(_file, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    document = (KeyStoreDocument)Serializer.ReadObject(stream);
                }
            }
            catch (FileNotFoundException ex)
            {
                throw new DataStoreMismatchException(ex);
            }
            catch (DirectoryNotFoundException ex)
            {
                throw new DataStoreMismatchException(ex);
            }
            catch (SerializationException ex)
            {
                throw new KeyStoreCorruptedException("Soubor není platný JSON s očekávanou strukturou.", ex);
            }
            catch (XmlException ex)
            {
                throw new KeyStoreCorruptedException("Soubor není platný JSON.", ex);
            }

            Validate(document);
            return document;
        }

        private static void Validate(KeyStoreDocument document)
        {
            if (document == null)
            {
                throw new KeyStoreCorruptedException("Soubor je prázdný.");
            }

            if (document.FormatVersion != FormatVersion)
            {
                throw new KeyStoreCorruptedException("Neznámá verze formátu: " + document.FormatVersion + ".");
            }

            if (!Guid.TryParse(document.DbId, out _))
            {
                throw new KeyStoreCorruptedException("Neplatné dbId.");
            }

            if (document.Users == null)
            {
                throw new KeyStoreCorruptedException("Chybí seznam uživatelů.");
            }

            var emails = new HashSet<string>(StringComparer.Ordinal);
            foreach (var user in document.Users)
            {
                if (user == null || string.IsNullOrWhiteSpace(user.Email))
                {
                    throw new KeyStoreCorruptedException("Záznam uživatele bez e-mailu.");
                }

                // Aplikace nikdy dva záznamy pro jeden e-mail nezapíše. Kdyby tam byly (ruční zásah),
                // nebylo by jasné, který platí – třeba jestli ten se starým heslem.
                if (!emails.Add(EmailAddress.Normalize(user.Email)))
                {
                    throw new KeyStoreCorruptedException("Více záznamů pro stejného uživatele.");
                }

                user.ToWrappedKey();
            }

            if (document.Recovery != null)
            {
                document.Recovery.ToWrappedKey();
            }
        }

        private void Write(KeyStoreDocument document, bool replaceExisting)
        {
            var temp = _file + ".tmp";

            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                using (var writer = JsonReaderWriterFactory.CreateJsonWriter(
                    stream, new UTF8Encoding(false), ownsStream: false, indent: true, indentChars: "  "))
                {
                    Serializer.WriteObject(writer, document);
                    writer.Flush();
                }

                stream.Flush(flushToDisk: true);
            }

            if (!replaceExisting)
            {
                File.Move(temp, _file);
                return;
            }

            var backup = _file + ".bak";
            File.Replace(temp, _file, backup);
            DeleteBackup(backup);
        }

        /// <summary>
        /// Smaže zálohu po úspěšné výměně souboru. Zůstal by v ní předchozí stav: klíč zabalený starým
        /// heslem, záznam právě deaktivovaného uživatele nebo dosavadní záchranný kód – kdo takové heslo
        /// či kód zná, by jím klíč databáze rozbalil i potom, co přestaly platit.
        /// <para>
        /// Nový soubor je v tu chvíli už zapsaný, takže nezdařený úklid nesmí zápis změnit v chybu:
        /// jen se zaznamená a zálohu přepíše a smaže příští zápis.
        /// </para>
        /// </summary>
        private void DeleteBackup(string backup)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    File.Delete(backup);
                    return;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    if (attempt >= BackupDeleteAttempts)
                    {
                        _logger.Warning(ex, "Předchozí verzi souboru klíčů (keys.json.bak) se nepodařilo smazat – smaže ji příští zápis");
                        return;
                    }
                }

                Thread.Sleep(BackupDeleteRetryDelayMs);
            }
        }
    }

    /// <summary>Kořen <c>keys.json</c>.</summary>
    [DataContract]
    internal sealed class KeyStoreDocument
    {
        [DataMember(Name = "formatVersion", Order = 0, IsRequired = true)]
        public int FormatVersion { get; set; }

        /// <summary>GUID – kontrola, že <c>keys.json</c> patří k této databázi (tabulka <c>Meta</c>).</summary>
        [DataMember(Name = "dbId", Order = 1, IsRequired = true)]
        public string DbId { get; set; }

        [DataMember(Name = "users", Order = 2, IsRequired = true)]
        public List<KeyStoreUserEntry> Users { get; set; }

        [DataMember(Name = "recovery", Order = 3, EmitDefaultValue = false)]
        public KeyStoreEntry Recovery { get; set; }
    }

    // Pozn.: záznamy schválně nesdílejí předka – serializátor by pole předka zapsal před e-mail
    // a pořadí v souboru by neodpovídalo dokumentovanému formátu.

    /// <summary>Klíč zabalený záchranným kódem (binární hodnoty v Base64).</summary>
    [DataContract]
    internal sealed class KeyStoreEntry
    {
        [DataMember(Name = "salt", Order = 1, IsRequired = true)]
        public string Salt { get; set; }

        [DataMember(Name = "iterations", Order = 2, IsRequired = true)]
        public int Iterations { get; set; }

        [DataMember(Name = "iv", Order = 3, IsRequired = true)]
        public string Iv { get; set; }

        [DataMember(Name = "wrappedKey", Order = 4, IsRequired = true)]
        public string WrappedKey { get; set; }

        [DataMember(Name = "mac", Order = 5, IsRequired = true)]
        public string Mac { get; set; }

        public static KeyStoreEntry From(WrappedKey key)
        {
            return new KeyStoreEntry
            {
                Salt = Convert.ToBase64String(key.Salt),
                Iterations = key.Iterations,
                Iv = Convert.ToBase64String(key.Iv),
                WrappedKey = Convert.ToBase64String(key.CipherText),
                Mac = Convert.ToBase64String(key.Mac),
            };
        }

        public WrappedKey ToWrappedKey()
        {
            return KeyStoreCodec.Decode(Salt, Iterations, Iv, WrappedKey, Mac);
        }
    }

    /// <summary>Záznam uživatele v <c>keys.json</c>: e-mail + klíč zabalený jeho heslem.</summary>
    [DataContract]
    internal sealed class KeyStoreUserEntry
    {
        [DataMember(Name = "email", Order = 0, IsRequired = true)]
        public string Email { get; set; }

        [DataMember(Name = "salt", Order = 1, IsRequired = true)]
        public string Salt { get; set; }

        [DataMember(Name = "iterations", Order = 2, IsRequired = true)]
        public int Iterations { get; set; }

        [DataMember(Name = "iv", Order = 3, IsRequired = true)]
        public string Iv { get; set; }

        [DataMember(Name = "wrappedKey", Order = 4, IsRequired = true)]
        public string WrappedKey { get; set; }

        [DataMember(Name = "mac", Order = 5, IsRequired = true)]
        public string Mac { get; set; }

        public static KeyStoreUserEntry From(string email, WrappedKey key)
        {
            return new KeyStoreUserEntry
            {
                Email = email,
                Salt = Convert.ToBase64String(key.Salt),
                Iterations = key.Iterations,
                Iv = Convert.ToBase64String(key.Iv),
                WrappedKey = Convert.ToBase64String(key.CipherText),
                Mac = Convert.ToBase64String(key.Mac),
            };
        }

        public WrappedKey ToWrappedKey()
        {
            return KeyStoreCodec.Decode(Salt, Iterations, Iv, WrappedKey, Mac);
        }
    }

    internal static class KeyStoreCodec
    {
        /// <summary>Převede hodnoty z JSON na <see cref="WrappedKey"/> a ověří jejich délky.</summary>
        public static WrappedKey Decode(string salt, int iterations, string iv, string wrappedKey, string mac)
        {
            byte[] saltBytes;
            byte[] ivBytes;
            byte[] cipherText;
            byte[] macBytes;

            try
            {
                saltBytes = Convert.FromBase64String(salt ?? string.Empty);
                ivBytes = Convert.FromBase64String(iv ?? string.Empty);
                cipherText = Convert.FromBase64String(wrappedKey ?? string.Empty);
                macBytes = Convert.FromBase64String(mac ?? string.Empty);
            }
            catch (FormatException ex)
            {
                throw new KeyStoreCorruptedException("Hodnota není platný Base64.", ex);
            }

            if (saltBytes.Length != KeyEnvelope.SaltSize
                || ivBytes.Length != KeyEnvelope.IvSize
                || macBytes.Length != KeyEnvelope.MacSize
                || cipherText.Length == 0
                || cipherText.Length % KeyEnvelope.IvSize != 0
                || iterations < 1)
            {
                throw new KeyStoreCorruptedException("Záznam klíče má neplatnou délku některé hodnoty.");
            }

            return new WrappedKey(saltBytes, iterations, ivBytes, cipherText, macBytes);
        }
    }
}
