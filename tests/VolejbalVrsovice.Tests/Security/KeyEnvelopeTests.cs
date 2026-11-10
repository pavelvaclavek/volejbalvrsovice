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

        [Fact]
        public void Wrapped_key_is_unwrapped_with_the_same_password()
        {
            var key = KeyEnvelope.GenerateKey();

            var wrapped = KeyEnvelope.Wrap(key, Password, Iterations);

            Assert.True(KeyEnvelope.TryUnwrap(wrapped, Password, out var unwrapped));
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
            var wrapped = KeyEnvelope.Wrap(KeyEnvelope.GenerateKey(), Password, Iterations);

            Assert.False(KeyEnvelope.TryUnwrap(wrapped, wrongPassword, out var unwrapped));
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
            var original = KeyEnvelope.Wrap(KeyEnvelope.GenerateKey(), Password, Iterations);

            var tampered = new WrappedKey(
                part == "salt" ? FlipBit(original.Salt) : original.Salt,
                part == "iterations" ? original.Iterations + 1 : original.Iterations,
                part == "iv" ? FlipBit(original.Iv) : original.Iv,
                part == "cipherText" ? FlipBit(original.CipherText) : original.CipherText,
                part == "mac" ? FlipBit(original.Mac) : original.Mac);

            Assert.False(KeyEnvelope.TryUnwrap(tampered, Password, out _));
        }

        [Fact]
        public void Every_wrap_uses_a_fresh_salt_and_iv()
        {
            var key = KeyEnvelope.GenerateKey();

            var first = KeyEnvelope.Wrap(key, Password, Iterations);
            var second = KeyEnvelope.Wrap(key, Password, Iterations);

            Assert.NotEqual(first.Salt, second.Salt);
            Assert.NotEqual(first.Iv, second.Iv);
            Assert.NotEqual(first.CipherText, second.CipherText);
        }

        [Fact]
        public void Wrapped_record_has_the_documented_sizes()
        {
            var wrapped = KeyEnvelope.Wrap(KeyEnvelope.GenerateKey(), Password, Iterations);

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

            var wrapped = KeyEnvelope.Wrap(key, Password, Iterations);

            Assert.False(Contains(wrapped.CipherText, key.Take(8).ToArray()));
        }

        /// <summary>
        /// Záznam vytvořený nezávisle (PBKDF2 a HMAC: standardní knihovna Pythonu, AES-256-CBC: OpenSSL).
        /// Hlídá, že formát je přesně ten dokumentovaný: heslo v UTF-8, PBKDF2-HMAC-SHA256 → 64 B,
        /// prvních 32 B šifrovací klíč, druhých 32 B klíč MAC, MAC přes IV || šifrový text.
        /// </summary>
        [Fact]
        public void Unwraps_a_record_created_by_an_independent_implementation()
        {
            var wrapped = new WrappedKey(
                salt: Convert.FromBase64String("oKGio6SlpqeoqaqrrK2urw=="),
                iterations: 1000,
                iv: Convert.FromBase64String("EBESExQVFhcYGRobHB0eHw=="),
                cipherText: Convert.FromBase64String("HCbbdaDzYAlADhlg/gV4HFkkSG4qDNgP2sPPKBzOGtNzuZ6lM9c86l2brMr2gpcj"),
                mac: Convert.FromBase64String("ctCBqZ+FnR4gZBCdR5b/wsBp63ITu8dUCSeSbLhzcCA="));

            Assert.True(KeyEnvelope.TryUnwrap(wrapped, "Heslo-Žluťoučký-2026", out var key));
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
            Assert.Throws<ArgumentException>(() => KeyEnvelope.Wrap(new byte[16], Password, Iterations));
            Assert.Throws<ArgumentException>(() => KeyEnvelope.Wrap(KeyEnvelope.GenerateKey(), string.Empty, Iterations));
        }

        [Fact]
        public void Clear_overwrites_the_buffer_with_zeros()
        {
            var key = KeyEnvelope.GenerateKey();

            KeyEnvelope.Clear(key);

            Assert.All(key, value => Assert.Equal(0, value));
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
