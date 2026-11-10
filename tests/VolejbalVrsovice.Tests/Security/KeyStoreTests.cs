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

            Assert.True(KeyEnvelope.TryUnwrap(wrapped, "admin", out var key));
            Assert.Equal(_key, key);
            Assert.Equal(_dbId, _store.GetDbId());
        }

        [Fact]
        public void File_contains_neither_the_plain_key_nor_the_password()
        {
            _store.Create(_dbId, "admin@admin.cz", Wrap("Tajné-heslo-123"));
            _store.SetRecovery(Wrap("ABCDEFGHJKLMNPQRSTUVWXYZ"));

            var json = File.ReadAllText(_file);

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
            Assert.False(KeyEnvelope.TryUnwrap(_store.FindUser("admin@admin.cz"), "admin", out _));
            Assert.True(KeyEnvelope.TryUnwrap(_store.FindUser("admin@admin.cz"), "nove-heslo-1", out _));
            Assert.True(KeyEnvelope.TryUnwrap(_store.FindUser("trener@example.cz"), "docasne-heslo", out _));
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

            Assert.False(KeyEnvelope.TryUnwrap(_store.GetRecovery(), "PRVNIKODPRVNIKODPRVNIKOD", out _));
            Assert.True(KeyEnvelope.TryUnwrap(_store.GetRecovery(), "DRUHYKODDRUHYKODDRUHYKOD", out var key));
            Assert.Equal(_key, key);
            Assert.Contains("\"recovery\": {", File.ReadAllText(_file));
        }

        [Fact]
        public void Update_is_atomic_and_keeps_the_previous_version_as_backup()
        {
            _store.Create(_dbId, "admin@admin.cz", Wrap("admin"));
            var original = File.ReadAllBytes(_file);

            _store.SetUser("trener@example.cz", Wrap("docasne-heslo"));

            Assert.Equal(original, File.ReadAllBytes(_file + ".bak"));
            Assert.False(File.Exists(_file + ".tmp"));
            Assert.NotEqual(original, File.ReadAllBytes(_file));
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

        [Fact]
        public void Corrupted_file_is_never_modified()
        {
            File.WriteAllText(_file, "{ poškozeno");

            Assert.Throws<KeyStoreCorruptedException>(() => _store.SetUser("a@b.cz", Wrap("heslo-heslo")));

            Assert.Equal("{ poškozeno", File.ReadAllText(_file));
        }

        private WrappedKey Wrap(string secret)
        {
            return KeyEnvelope.Wrap(_key, secret, Iterations);
        }
    }
}
