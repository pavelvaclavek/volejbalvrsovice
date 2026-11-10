using System;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using VolejbalVrsovice.Core;

namespace VolejbalVrsovice.Data.Security
{
    /// <summary>
    /// Komu a k čemu zabalený klíč patří (uživatel s daným e-mailem, nebo záchranný kód).
    /// Vazba vstupuje do MAC, takže záznam v <c>keys.json</c> nejde přenést pod jiný e-mail
    /// ani zaměnit se záznamem záchranného kódu – takto upravený záznam se neověří.
    /// </summary>
    public sealed class KeyBinding
    {
        private const string Prefix = "VolejbalVrsovice.keys.v1|";

        private readonly string _label;

        private KeyBinding(string label)
        {
            _label = label;
        }

        /// <summary>Klíč zabalený záchranným kódem.</summary>
        public static KeyBinding Recovery { get; } = new KeyBinding(Prefix + "recovery");

        /// <summary>Klíč zabalený heslem uživatele s daným e-mailem (porovnává se normalizovaný tvar).</summary>
        public static KeyBinding ForUser(string email)
        {
            var normalized = EmailAddress.Normalize(email);
            if (normalized.Length == 0)
            {
                throw new ArgumentException("E-mail musí být zadán.", nameof(email));
            }

            return new KeyBinding(Prefix + "user|" + normalized);
        }

        internal byte[] ToBytes()
        {
            return Encoding.UTF8.GetBytes(_label);
        }
    }

    /// <summary>
    /// Obálkové šifrování klíče databáze (zadání, sekce 2 a 5a).
    /// <list type="bullet">
    /// <item>KEK = PBKDF2-HMAC-SHA256(heslo, sůl 16 B, ≥ 600 000 iterací) → 64 B:
    /// prvních 32 B šifrovací klíč, druhých 32 B klíč pro MAC.</item>
    /// <item>AES-256-CBC (PKCS7) s náhodným IV.</item>
    /// <item>Encrypt-then-MAC: HMAC-SHA256 přes <c>délka vazby (4 B, big-endian) || vazba || IV || šifrový text</c>;
    /// MAC se ověřuje v konstantním čase a vždy PŘED dešifrováním.</item>
    /// </list>
    /// .NET Framework 4.8 nemá <c>AesGcm</c>, proto CBC + HMAC.
    /// </summary>
    public static class KeyEnvelope
    {
        /// <summary>Počet iterací PBKDF2 pro nově vytvářené záznamy.</summary>
        public const int DefaultIterations = 600_000;

        /// <summary>Délka klíče databáze (256 bitů).</summary>
        public const int KeySize = 32;

        public const int SaltSize = 16;
        public const int IvSize = 16;
        public const int MacSize = 32;

        private const int DerivedSize = 64;

        /// <summary>Vygeneruje náhodný 256bitový klíč databáze.</summary>
        public static byte[] GenerateKey()
        {
            return RandomBytes(KeySize);
        }

        /// <summary>Zabalí klíč databáze tajemstvím (heslo uživatele nebo záchranný kód).</summary>
        public static WrappedKey Wrap(byte[] key, string secret, int iterations, KeyBinding binding)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (binding == null) throw new ArgumentNullException(nameof(binding));
            if (key.Length != KeySize) throw new ArgumentException("Klíč databáze musí mít 32 bajtů.", nameof(key));
            if (string.IsNullOrEmpty(secret)) throw new ArgumentException("Heslo nesmí být prázdné.", nameof(secret));
            if (iterations < 1) throw new ArgumentOutOfRangeException(nameof(iterations));

            var salt = RandomBytes(SaltSize);
            var derived = Derive(secret, salt, iterations);
            var encryptionKey = Slice(derived, 0, KeySize);
            var macKey = Slice(derived, KeySize, KeySize);

            try
            {
                // Samé nuly znamenají, že klíč mezitím někdo „smazal“ (odhlášení). Takový záznam by nešel
                // nikdy použít a uživatele by zamkl – proto ho nikdy nevytvářet. Kontrola je až po
                // odvození klíče, tedy těsně před tím, než se bajty klíče skutečně použijí.
                if (IsAllZero(key))
                {
                    throw new InvalidOperationException("Klíč databáze už není k dispozici – relace byla ukončena.");
                }

                byte[] iv;
                byte[] cipherText;

                using (var aes = CreateAes(encryptionKey))
                {
                    aes.GenerateIV();
                    iv = aes.IV;

                    using (var encryptor = aes.CreateEncryptor())
                    {
                        cipherText = encryptor.TransformFinalBlock(key, 0, key.Length);
                    }
                }

                return new WrappedKey(salt, iterations, iv, cipherText, ComputeMac(macKey, binding, iv, cipherText));
            }
            finally
            {
                Clear(derived);
                Clear(encryptionKey);
                Clear(macKey);
            }
        }

        /// <summary>
        /// Pokusí se klíč rozbalit. Vrací <c>false</c>, pokud nesouhlasí MAC – tedy při špatném hesle,
        /// poškozeném záznamu nebo záznamu, který patří někomu jinému (nelze rozlišit a ani se to nemá
        /// prozrazovat).
        /// </summary>
        public static bool TryUnwrap(WrappedKey wrapped, string secret, KeyBinding binding, out byte[] key)
        {
            if (wrapped == null) throw new ArgumentNullException(nameof(wrapped));
            if (binding == null) throw new ArgumentNullException(nameof(binding));

            key = null;
            if (string.IsNullOrEmpty(secret))
            {
                return false;
            }

            var derived = Derive(secret, wrapped.Salt, wrapped.Iterations);
            var encryptionKey = Slice(derived, 0, KeySize);
            var macKey = Slice(derived, KeySize, KeySize);

            try
            {
                // Nejdřív MAC, teprve potom dešifrování.
                var expectedMac = ComputeMac(macKey, binding, wrapped.Iv, wrapped.CipherText);
                if (!FixedTimeEquals(expectedMac, wrapped.Mac))
                {
                    return false;
                }

                byte[] plain;
                using (var aes = CreateAes(encryptionKey))
                {
                    aes.IV = wrapped.Iv;
                    using (var decryptor = aes.CreateDecryptor())
                    {
                        plain = decryptor.TransformFinalBlock(wrapped.CipherText, 0, wrapped.CipherText.Length);
                    }
                }

                if (plain.Length != KeySize)
                {
                    Clear(plain);
                    return false;
                }

                key = plain;
                return true;
            }
            catch (CryptographicException)
            {
                // MAC souhlasil, ale obsah nejde dešifrovat – záznam nevznikl touto aplikací.
                return false;
            }
            finally
            {
                Clear(derived);
                Clear(encryptionKey);
                Clear(macKey);
            }
        }

        /// <summary>Přepíše pole nulami (klíč se v paměti nemá povalovat déle, než je nutné).</summary>
        public static void Clear(byte[] buffer)
        {
            if (buffer != null)
            {
                Array.Clear(buffer, 0, buffer.Length);
            }
        }

        private static byte[] Derive(string secret, byte[] salt, int iterations)
        {
            using (var pbkdf2 = new Rfc2898DeriveBytes(secret, salt, iterations, HashAlgorithmName.SHA256))
            {
                return pbkdf2.GetBytes(DerivedSize);
            }
        }

        private static Aes CreateAes(byte[] encryptionKey)
        {
            var aes = Aes.Create();
            aes.KeySize = 256;
            aes.BlockSize = 128;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = encryptionKey;
            return aes;
        }

        private static byte[] ComputeMac(byte[] macKey, KeyBinding binding, byte[] iv, byte[] cipherText)
        {
            var bound = binding.ToBytes();

            // Délka vazby na začátku dělá vstup jednoznačným (vazbu nelze „prodloužit“ o bajty IV).
            var message = new byte[4 + bound.Length + iv.Length + cipherText.Length];
            message[0] = (byte)(bound.Length >> 24);
            message[1] = (byte)(bound.Length >> 16);
            message[2] = (byte)(bound.Length >> 8);
            message[3] = (byte)bound.Length;
            Buffer.BlockCopy(bound, 0, message, 4, bound.Length);
            Buffer.BlockCopy(iv, 0, message, 4 + bound.Length, iv.Length);
            Buffer.BlockCopy(cipherText, 0, message, 4 + bound.Length + iv.Length, cipherText.Length);

            using (var hmac = new HMACSHA256(macKey))
            {
                return hmac.ComputeHash(message);
            }
        }

        /// <summary>Porovnání bez předčasného ukončení – doba nezávisí na tom, kde se pole liší.</summary>
        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        private static bool FixedTimeEquals(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }

            var difference = 0;
            for (var i = 0; i < left.Length; i++)
            {
                difference |= left[i] ^ right[i];
            }

            return difference == 0;
        }

        private static bool IsAllZero(byte[] buffer)
        {
            var combined = 0;
            foreach (var value in buffer)
            {
                combined |= value;
            }

            return combined == 0;
        }

        private static byte[] Slice(byte[] source, int offset, int count)
        {
            var result = new byte[count];
            Buffer.BlockCopy(source, offset, result, 0, count);
            return result;
        }

        private static byte[] RandomBytes(int count)
        {
            var bytes = new byte[count];
            using (var random = RandomNumberGenerator.Create())
            {
                random.GetBytes(bytes);
            }

            return bytes;
        }
    }
}
