using System;
using System.Linq;
using VolejbalVrsovice.Data.Security;
using Xunit;

namespace VolejbalVrsovice.Tests.Security
{
    public class KeyEnvelopeTests
    {
        private const int Iterations = 1000;
        private const string Password = "Správné-heslo-1";

        private static readonly KeyBinding Jana = KeyBinding.ForUser("jana.novakova@example.cz");

        [Fact]
        public void Wrapped_key_is_unwrapped_with_the_same_password()
        {
            var key = KeyEnvelope.GenerateKey();

            var wrapped = KeyEnvelope.Wrap(key, Password, Iterations, Jana);

            Assert.True(KeyEnvelope.TryUnwrap(wrapped, Password, Jana, out var unwrapped));
            Assert.Equal(key, unwrapped);
        }

        [Theory]
        [InlineData("špatné-heslo")]
        [InlineData("správné-heslo-1")]   // liší se jen velikostí písmene
        [InlineData("Správné-heslo-1 ")]  // mezera navíc
        [InlineData("")]
        [InlineData(null)]
        public void Wrong_password_does_not_unwrap(string wrongPassword)
        {
            var wrapped = KeyEnvelope.Wrap(KeyEnvelope.GenerateKey(), Password, Iterations, Jana);

            Assert.False(KeyEnvelope.TryUnwrap(wrapped, wrongPassword, Jana, out var unwrapped));
            Assert.Null(unwrapped);
        }

        [Theory]
        [InlineData("salt")]
        [InlineData("iv")]
        [InlineData("cipherText")]
        [InlineData("mac")]
        [InlineData("iterations")]
        public void Tampered_record_is_rejected(string part)
        {
            var original = KeyEnvelope.Wrap(KeyEnvelope.GenerateKey(), Password, Iterations, Jana);

            var tampered = new WrappedKey(
                part == "salt" ? FlipBit(original.Salt) : original.Salt,
                part == "iterations" ? original.Iterations + 1 : original.Iterations,
                part == "iv" ? FlipBit(original.Iv) : original.Iv,
                part == "cipherText" ? FlipBit(original.CipherText) : original.CipherText,
                part == "mac" ? FlipBit(original.Mac) : original.Mac);

            Assert.False(KeyEnvelope.TryUnwrap(tampered, Password, Jana, out _));
        }

        /// <summary>
        /// Záznam je svázaný s e-mailem: zkopírovaný v keys.json pod jiného uživatele se neověří,
        /// i když heslo sedí.
        /// </summary>
        [Fact]
        public void Record_does_not_unwrap_under_another_users_email()
        {
            var wrapped = KeyEnvelope.Wrap(KeyEnvelope.GenerateKey(), Password, Iterations, Jana);

            Assert.False(KeyEnvelope.TryUnwrap(wrapped, Password, KeyBinding.ForUser("admin@admin.cz"), out var key));
            Assert.Null(key);
        }

        [Fact]
        public void Binding_ignores_letter_case_and_surrounding_spaces_of_the_email()
        {
            var wrapped = KeyEnvelope.Wrap(KeyEnvelope.GenerateKey(), Password, Iterations, Jana);

            Assert.True(KeyEnvelope.TryUnwrap(wrapped, Password, KeyBinding.ForUser("  Jana.Novakova@Example.CZ "), out _));
        }

        [Fact]
        public void User_record_and_recovery_record_are_not_interchangeable()
        {
            var key = KeyEnvelope.GenerateKey();
            const string secret = "ABCDEFGHJKLMNPQRSTUVWXYZ";   // heslo, které vypadá jako záchranný kód

            var userRecord = KeyEnvelope.Wrap(key, secret, Iterations, Jana);
            var recoveryRecord = KeyEnvelope.Wrap(key, secret, Iterations, KeyBinding.Recovery);

            Assert.False(KeyEnvelope.TryUnwrap(userRecord, secret, KeyBinding.Recovery, out _));
            Assert.False(KeyEnvelope.TryUnwrap(recoveryRecord, secret, Jana, out _));
            Assert.True(KeyEnvelope.TryUnwrap(recoveryRecord, secret, KeyBinding.Recovery, out _));
        }

        [Fact]
        public void Every_wrap_uses_a_fresh_salt_and_iv()
        {
            var key = KeyEnvelope.GenerateKey();

            var first = KeyEnvelope.Wrap(key, Password, Iterations, Jana);
            var second = KeyEnvelope.Wrap(key, Password, Iterations, Jana);

            Assert.NotEqual(first.Salt, second.Salt);
            Assert.NotEqual(first.Iv, second.Iv);
            Assert.NotEqual(first.CipherText, second.CipherText);
        }

        [Fact]
        public void Wrapped_record_has_the_documented_sizes()
        {
            var wrapped = KeyEnvelope.Wrap(KeyEnvelope.GenerateKey(), Password, Iterations, Jana);

            Assert.Equal(16, wrapped.Salt.Length);
            Assert.Equal(16, wrapped.Iv.Length);
            Assert.Equal(48, wrapped.CipherText.Length);   // 32 B klíč + celý blok PKCS7
            Assert.Equal(32, wrapped.Mac.Length);
            Assert.Equal(Iterations, wrapped.Iterations);
        }

        [Fact]
        public void Wrapped_record_does_not_contain_the_plain_key()
        {
            var key = KeyEnvelope.GenerateKey();

            var wrapped = KeyEnvelope.Wrap(key, Password, Iterations, Jana);

            Assert.False(Contains(wrapped.CipherText, key.Take(8).ToArray()));
        }

        /// <summary>
        /// Záznam vytvořený nezávisle (PBKDF2 a HMAC: standardní knihovna Pythonu, AES-256-CBC: OpenSSL).
        /// Hlídá, že formát je přesně ten dokumentovaný: heslo v UTF-8, PBKDF2-HMAC-SHA256 → 64 B,
        /// prvních 32 B šifrovací klíč, druhých 32 B klíč MAC,
        /// MAC přes délku vazby (4 B, big-endian) || vazbu || IV || šifrový text,
        /// kde vazba je UTF-8 text „VolejbalVrsovice.keys.v1|user|jana.novakova@example.cz“.
        /// </summary>
        [Fact]
        public void Unwraps_a_record_created_by_an_independent_implementation()
        {
            var wrapped = new WrappedKey(
                salt: Convert.FromBase64String("oKGio6SlpqeoqaqrrK2urw=="),
                iterations: 1000,
                iv: Convert.FromBase64String("EBESExQVFhcYGRobHB0eHw=="),
                cipherText: Convert.FromBase64String("HCbbdaDzYAlADhlg/gV4HFkkSG4qDNgP2sPPKBzOGtNzuZ6lM9c86l2brMr2gpcj"),
                mac: Convert.FromBase64String("fvdRIIC0Vr4vFsw4Q1UFR53rP1syybeyVUV5ppN/ACY="));

            Assert.True(KeyEnvelope.TryUnwrap(wrapped, "Heslo-Žluťoučký-2026", Jana, out var key));
            Assert.Equal(Enumerable.Range(0, 32).Select(value => (byte)value).ToArray(), key);
        }

        [Fact]
        public void Production_iteration_count_is_at_least_600_000()
        {
            Assert.True(KeyEnvelope.DefaultIterations >= 600000);
            Assert.Equal(KeyEnvelope.DefaultIterations, KdfSettings.Default.Iterations);
        }

        [Fact]
        public void Generated_keys_are_256_bit_and_random()
        {
            var first = KeyEnvelope.GenerateKey();
            var second = KeyEnvelope.GenerateKey();

            Assert.Equal(32, first.Length);
            Assert.NotEqual(first, second);
        }

        [Fact]
        public void Wrap_rejects_a_key_of_wrong_size_and_an_empty_password()
        {
            Assert.Throws<ArgumentException>(() => KeyEnvelope.Wrap(new byte[16], Password, Iterations, Jana));
            Assert.Throws<ArgumentException>(() => KeyEnvelope.Wrap(KeyEnvelope.GenerateKey(), string.Empty, Iterations, Jana));
        }

        /// <summary>
        /// Vynulovaný klíč (relace mezitím skončila) se nikdy nezabalí – takový záznam by uživatele zamkl.
        /// </summary>
        [Fact]
        public void Wrap_refuses_a_key_that_was_already_wiped()
        {
            var key = KeyEnvelope.GenerateKey();
            KeyEnvelope.Clear(key);

            Assert.Throws<InvalidOperationException>(() => KeyEnvelope.Wrap(key, Password, Iterations, Jana));
        }

        [Fact]
        public void Clear_overwrites_the_buffer_with_zeros()
        {
            var key = KeyEnvelope.GenerateKey();

            KeyEnvelope.Clear(key);

            Assert.All(key, value => Assert.Equal(0, value));
        }

        [Fact]
        public void Binding_requires_an_email()
        {
            Assert.Throws<ArgumentException>(() => KeyBinding.ForUser("  "));
            Assert.Throws<ArgumentException>(() => KeyBinding.ForUser(null));
        }

        private static byte[] FlipBit(byte[] source)
        {
            var copy = (byte[])source.Clone();
            copy[copy.Length / 2] ^= 0x01;
            return copy;
        }

        private static bool Contains(byte[] haystack, byte[] needle)
        {
            for (var i = 0; i <= haystack.Length - needle.Length; i++)
            {
                if (haystack.Skip(i).Take(needle.Length).SequenceEqual(needle))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
