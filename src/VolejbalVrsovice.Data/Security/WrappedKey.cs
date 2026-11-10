using System;

namespace VolejbalVrsovice.Data.Security
{
    /// <summary>
    /// Klíč databáze (DEK) zašifrovaný klíčem odvozeným z hesla nebo ze záchranného kódu –
    /// jeden záznam v <c>keys.json</c>.
    /// </summary>
    public sealed class WrappedKey
    {
        public WrappedKey(byte[] salt, int iterations, byte[] iv, byte[] cipherText, byte[] mac)
        {
            Salt = salt ?? throw new ArgumentNullException(nameof(salt));
            Iv = iv ?? throw new ArgumentNullException(nameof(iv));
            CipherText = cipherText ?? throw new ArgumentNullException(nameof(cipherText));
            Mac = mac ?? throw new ArgumentNullException(nameof(mac));
            Iterations = iterations;
        }

        /// <summary>Sůl pro PBKDF2 (16 B).</summary>
        public byte[] Salt { get; }

        /// <summary>Počet iterací PBKDF2.</summary>
        public int Iterations { get; }

        /// <summary>Inicializační vektor AES-CBC (16 B).</summary>
        public byte[] Iv { get; }

        /// <summary>Zašifrovaný klíč databáze (v JSON pole <c>wrappedKey</c>).</summary>
        public byte[] CipherText { get; }

        /// <summary>HMAC-SHA256 přes <c>IV || šifrový text</c> (32 B).</summary>
        public byte[] Mac { get; }
    }
}
