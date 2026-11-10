using System;
using System.IO;
using System.Linq;
using VolejbalVrsovice.Core.Errors;
using VolejbalVrsovice.Data.Security;
using VolejbalVrsovice.Tests.TestSupport;
using Xunit;

namespace VolejbalVrsovice.Tests.Security
{
    public class KeyStoreTests : IDisposable
    {
        private const int Iterations = 1000;

        // Úložiště vazbu záznamu neřeší (jen ukládá) – v těchto testech stačí jedna pro všechny záznamy.
        private static readonly KeyBinding Binding = KeyBinding.ForUser("test@example.cz");

        private readonly TempDirectory _temp = new TempDirectory();
        private readonly string _file;
        private readonly KeyStore _store;
        private readonly Guid _dbId = Guid.Parse("7c9e6679-7425-40de-944b-e07fc1f90ae7");
        private readonly byte[] _key = KeyEnvelope.GenerateKey();

        public KeyStoreTests()
        {
            _file = Path.Combine(_temp.Path, "keys.json");
            _store = new KeyStore(_file);
        }

        public void Dispose()
        {
            _temp.Dispose();
        }

        [Fact]
        public void New_file_has_the_documented_json_structure()
        {
            _store.Create(_dbId, "admin@admin.cz", Wrap("admin"));

            var json = File.ReadAllText(_file);

            Assert.Contains("\"formatVersion\": 1", json);
            Assert.Contains("\"dbId\": \"7c9e6679-7425-40de-944b-e07fc1f90ae7\"", json);
            Assert.Contains("\"users\": [", json);
            Assert.Contains("\"email\": \"admin@admin.cz\"", json);
            Assert.Contains("\"iterations\": 1000", json);
            Assert.DoesNotContain("\"recovery\"", json);

            // Pořadí polí záznamu jako v zadání: email, salt, iterations, iv, wrappedKey, mac.
            var positions = new[] { "\"email\"", "\"salt\"", "\"iterations\"", "\"iv\"", "\"wrappedKey\"", "\"mac\"" }
                .Select(name => json.IndexOf(name, StringComparison.Ordinal))
                .ToArray();
            Assert.All(positions, position => Assert.True(position > 0));
            Assert.Equal(positions.OrderBy(position => position), positions);
        }

        [Fact]
        public void Stored_key_can_be_read_back_and_unwrapped()
        {
            _store.Create(_dbId, "admin@admin.cz", Wrap("admin"));

            var wrapped = _store.FindUser("admin@admin.cz");

            Assert.True(KeyEnvelope.TryUnwrap(wrapped, "admin", Binding, out var key));
            Assert.Equal(_key, key);
            Assert.Equal(_dbId, _store.GetDbId());
        }

        [Fact]
        public void File_contains_neither_the_plain_key_nor_the_password()
        {
            _store.Create(_dbId, "admin@admin.cz", Wrap("Tajné-heslo-123"));
            _store.SetRecovery(Wrap("ABCDEFGHJKLMNPQRSTUVWXYZ"));

            var json = ReadJson();

            Assert.DoesNotContain(Convert.ToBase64String(_key), json);
            Assert.DoesNotContain(BitConverter.ToString(_key).Replace("-", string.Empty), json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Tajné-heslo-123", json);
            Assert.DoesNotContain("ABCDEFGHJKLMNPQRSTUVWXYZ", json);
        }

        [Fact]
        public void Create_never_overwrites_an_existing_file()
        {
            _store.Create(_dbId, "admin@admin.cz", Wrap("admin"));
            var before = File.ReadAllBytes(_file);

            Assert.Throws<IOException>(() => _store.Create(Guid.NewGuid(), "jiny@example.cz", Wrap("jine-heslo")));

            Assert.Equal(before, File.ReadAllBytes(_file));
        }

        [Fact]
        public void Emails_are_matched_case_insensitively_and_stored_normalized()
        {
            _store.Create(_dbId, "  Admin@Admin.CZ ", Wrap("admin"));

            Assert.NotNull(_store.FindUser("ADMIN@admin.cz"));
            Assert.Equal(new[] { "admin@admin.cz" }, _store.GetUserEmails());
        }

        [Fact]
        public void Unknown_user_is_not_found()
        {
            _store.Create(_dbId, "admin@admin.cz", Wrap("admin"));

            Assert.Null(_store.FindUser("nikdo@example.cz"));
        }

        [Fact]
        public void Set_user_adds_a_new_user_and_replaces_an_existing_one()
        {
            _store.Create(_dbId, "admin@admin.cz", Wrap("admin"));

            _store.SetUser("trener@example.cz", Wrap("docasne-heslo"));
            _store.SetUser("Admin@admin.cz", Wrap("nove-heslo-1"));

            Assert.Equal(new[] { "admin@admin.cz", "trener@example.cz" }, _store.GetUserEmails());
            Assert.False(KeyEnvelope.TryUnwrap(_store.FindUser("admin@admin.cz"), "admin", Binding, out _));
            Assert.True(KeyEnvelope.TryUnwrap(_store.FindUser("admin@admin.cz"), "nove-heslo-1", Binding, out _));
            Assert.True(KeyEnvelope.TryUnwrap(_store.FindUser("trener@example.cz"), "docasne-heslo", Binding, out _));
        }

        [Fact]
        public void Remove_user_deletes_only_that_user()
        {
            _store.Create(_dbId, "admin@admin.cz", Wrap("admin"));
            _store.SetUser("trener@example.cz", Wrap("docasne-heslo"));

            Assert.True(_store.RemoveUser("TRENER@example.cz"));
            Assert.False(_store.RemoveUser("trener@example.cz"));

            Assert.Null(_store.FindUser("trener@example.cz"));
            Assert.NotNull(_store.FindUser("admin@admin.cz"));
        }

        [Fact]
        public void Recovery_key_is_absent_until_set_and_then_replaceable()
        {
            _store.Create(_dbId, "admin@admin.cz", Wrap("admin"));
            Assert.Null(_store.GetRecovery());

            _store.SetRecovery(Wrap("PRVNIKODPRVNIKODPRVNIKOD"));
            _store.SetRecovery(Wrap("DRUHYKODDRUHYKODDRUHYKOD"));

            Assert.False(KeyEnvelope.TryUnwrap(_store.GetRecovery(), "PRVNIKODPRVNIKODPRVNIKOD", Binding, out _));
            Assert.True(KeyEnvelope.TryUnwrap(_store.GetRecovery(), "DRUHYKODDRUHYKODDRUHYKOD", Binding, out var key));
            Assert.Equal(_key, key);
            Assert.Contains("\"recovery\": {", File.ReadAllText(_file));
        }

        [Fact]
        public void Update_replaces_the_file_and_leaves_no_other_file_behind()
        {
            _store.Create(_dbId, "admin@admin.cz", Wrap("admin"));
            var original = File.ReadAllBytes(_file);

            _store.SetUser("trener@example.cz", Wrap("docasne-heslo"));

            Assert.NotEqual(original, File.ReadAllBytes(_file));
            Assert.Equal(new[] { "keys.json" }, FilesInFolder());   // žádné keys.json.tmp ani keys.json.bak
        }

        /// <summary>
        /// Po změně hesla, deaktivaci i novém záchranném kódu nesmí ve složce zůstat předchozí záznam:
        /// kdo zná staré heslo nebo starý kód, by jím klíč databáze rozbalil ze zálohy i potom,
        /// co přestaly platit.
        /// </summary>
        [Fact]
        public void Superseded_records_do_not_survive_anywhere_in_the_folder()
        {
            _store.Create(_dbId, "admin@admin.cz", Wrap("stare-heslo"));
            _store.SetUser("trener@example.cz", Wrap("heslo-trenera"));
            _store.SetRecovery(Wrap("PRVNIKODPRVNIKODPRVNIKOD"));

            var superseded = new[] { _store.FindUser("admin@admin.cz"), _store.FindUser("trener@example.cz"), _store.GetRecovery() }
                .Select(record => Convert.ToBase64String(record.CipherText))
                .ToArray();
            Assert.All(superseded, wrappedKey => Assert.Contains(wrappedKey, ReadJson()));

            _store.SetUser("admin@admin.cz", Wrap("nove-heslo-1"));     // změna hesla
            _store.RemoveUser("trener@example.cz");                     // deaktivace
            _store.SetRecovery(Wrap("DRUHYKODDRUHYKODDRUHYKOD"));       // nový záchranný kód

            Assert.Equal(new[] { "keys.json" }, FilesInFolder());
            var json = ReadJson();
            Assert.All(superseded, wrappedKey => Assert.DoesNotContain(wrappedKey, json));
            Assert.DoesNotContain("trener@example.cz", json);
        }

        [Fact]
        public void Backup_left_behind_earlier_is_removed_by_the_next_write()
        {
            _store.Create(_dbId, "admin@admin.cz", Wrap("admin"));
            File.WriteAllText(_file + ".bak", "záloha po starší verzi aplikace");

            _store.SetRecovery(Wrap("PRVNIKODPRVNIKODPRVNIKOD"));

            Assert.Equal(new[] { "keys.json" }, FilesInFolder());
        }

        [Fact]
        public void Failed_write_keeps_the_current_file_and_creates_no_backup()
        {
            _store.Create(_dbId, "admin@admin.cz", Wrap("admin"));
            var original = File.ReadAllBytes(_file);

            // Soubor drží jiný program a nedovolí ho nahradit.
            using (new FileStream(_file, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Assert.ThrowsAny<IOException>(() => _store.SetUser("trener@example.cz", Wrap("docasne-heslo")));
            }

            Assert.Equal(original, File.ReadAllBytes(_file));
            Assert.False(File.Exists(_file + ".bak"));
            Assert.Null(_store.FindUser("trener@example.cz"));
        }

        [Fact]
        public void Missing_file_is_reported_as_mismatch()
        {
            Assert.False(_store.Exists);
            Assert.Throws<DataStoreMismatchException>(() => _store.GetDbId());
        }

        [Theory]
        [InlineData("")]
        [InlineData("tohle není json")]
        [InlineData("{ \"formatVersion\": 1 }")]
        [InlineData("{ \"formatVersion\": 2, \"dbId\": \"7c9e6679-7425-40de-944b-e07fc1f90ae7\", \"users\": [] }")]
        [InlineData("{ \"formatVersion\": 1, \"dbId\": \"neni-guid\", \"users\": [] }")]
        [InlineData("{ \"formatVersion\": 1, \"dbId\": \"7c9e6679-7425-40de-944b-e07fc1f90ae7\", \"users\": [ { \"email\": \"a@b.cz\", \"salt\": \"###\", \"iterations\": 1000, \"iv\": \"AA==\", \"wrappedKey\": \"AA==\", \"mac\": \"AA==\" } ] }")]
        [InlineData("{ \"formatVersion\": 1, \"dbId\": \"7c9e6679-7425-40de-944b-e07fc1f90ae7\", \"users\": [ { \"email\": \"a@b.cz\", \"salt\": \"AA==\", \"iterations\": 1000, \"iv\": \"AA==\", \"wrappedKey\": \"AA==\", \"mac\": \"AA==\" } ] }")]
        public void Corrupted_file_is_rejected(string content)
        {
            File.WriteAllText(_file, content);

            Assert.Throws<KeyStoreCorruptedException>(() => _store.GetDbId());
        }

        /// <summary>
        /// Dva záznamy pro jeden e-mail aplikace nikdy nezapíše; kdyby vznikly ručním zásahem
        /// (např. vložením starého záznamu ze zálohy), soubor se odmítne celý.
        /// </summary>
        [Fact]
        public void Duplicate_user_records_are_rejected()
        {
            _store.Create(_dbId, "admin@admin.cz", Wrap("admin"));
            var json = File.ReadAllText(_file);
            var start = json.IndexOf("    {", StringComparison.Ordinal);
            var end = json.IndexOf("    }", StringComparison.Ordinal) + "    }".Length;
            var record = json.Substring(start, end - start);
            var duplicated = json.Insert(end, ",\n" + record.Replace("admin@admin.cz", "ADMIN@admin.cz"));
            File.WriteAllText(_file, duplicated);

            Assert.Throws<KeyStoreCorruptedException>(() => _store.FindUser("admin@admin.cz"));
        }

        [Fact]
        public void Corrupted_file_is_never_modified()
        {
            File.WriteAllText(_file, "{ poškozeno");

            Assert.Throws<KeyStoreCorruptedException>(() => _store.SetUser("a@b.cz", Wrap("heslo-heslo")));

            Assert.Equal("{ poškozeno", File.ReadAllText(_file));
        }

        private WrappedKey Wrap(string secret)
        {
            return KeyEnvelope.Wrap(_key, secret, Iterations, Binding);
        }

        /// <summary>
        /// Obsah souboru s lomítky bez zpětného lomítka: serializátor zapisuje „/“ jako „\/“, takže by se
        /// hodnota v Base64 při hledání v surovém textu nemusela najít, i kdyby v souboru byla.
        /// </summary>
        private string ReadJson()
        {
            return File.ReadAllText(_file).Replace("\\/", "/");
        }

        private string[] FilesInFolder()
        {
            return Directory.GetFiles(_temp.Path).Select(Path.GetFileName).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        }
    }
}
