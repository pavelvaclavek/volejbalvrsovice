using System;
using VolejbalVrsovice.Core.Entities;
using VolejbalVrsovice.Core.Services;
using VolejbalVrsovice.Data.Db;

namespace VolejbalVrsovice.Data.Security
{
    /// <summary>
    /// Relace přihlášeného uživatele: kdo je přihlášen a klíč databáze (DEK). Klíč se drží jen
    /// v paměti jako <c>byte[]</c> a při odhlášení se přepíše nulami. Nikdy se neloguje.
    /// </summary>
    public sealed class AppSession : ISession, IDatabaseKeySource
    {
        private readonly object _gate = new object();
        private AppUser _user;
        private byte[] _key;

        public AppUser User
        {
            get
            {
                lock (_gate)
                {
                    return _user;
                }
            }
        }

        public bool IsAuthenticated
        {
            get { return User != null; }
        }

        public int? UserId
        {
            get
            {
                var user = User;
                return user == null ? (int?)null : user.Id;
            }
        }

        /// <summary>
        /// Vrací vždy novou kopii klíče. Vlastní pole relace ven nikdy nejde – odhlášení ho přepíše nulami
        /// a operace, která zrovna běží (zabalení klíče trvá několik sekund), by jinak pracovala s nulami.
        /// </summary>
        public byte[] GetKey()
        {
            lock (_gate)
            {
                if (_key == null)
                {
                    throw new InvalidOperationException("Databáze není odemčená – nikdo není přihlášen.");
                }

                return (byte[])_key.Clone();
            }
        }

        /// <summary>
        /// Zahájí relaci. Přebírá vlastnictví pole <paramref name="key"/> – volající ho dál nepoužívá
        /// ani nemaže.
        /// </summary>
        public void Start(AppUser user, byte[] key)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));
            if (key == null) throw new ArgumentNullException(nameof(key));

            lock (_gate)
            {
                Clear();
                _user = user;
                _key = key;
            }
        }

        /// <summary>Nahradí údaje přihlášeného uživatele čerstvými (např. po změně hesla).</summary>
        public void Refresh(AppUser user)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));

            lock (_gate)
            {
                if (_user == null || _user.Id != user.Id)
                {
                    throw new InvalidOperationException("Obnovit lze jen údaje právě přihlášeného uživatele.");
                }

                _user = user;
            }
        }

        /// <summary>Ukončí relaci a přepíše klíč databáze nulami.</summary>
        public void End()
        {
            lock (_gate)
            {
                Clear();
            }
        }

        private void Clear()
        {
            if (_key != null)
            {
                KeyEnvelope.Clear(_key);
                _key = null;
            }

            _user = null;
        }
    }

    /// <summary>Pevně daný klíč – pro práci s databází mimo relaci (přihlašování, obnova záchranným kódem).</summary>
    internal sealed class FixedKeySource : IDatabaseKeySource
    {
        private readonly byte[] _key;

        public FixedKeySource(byte[] key)
        {
            _key = key ?? throw new ArgumentNullException(nameof(key));
        }

        public byte[] GetKey()
        {
            return (byte[])_key.Clone();
        }
    }

    /// <summary>Pevně daný původce změn – pro zápisy mimo relaci.</summary>
    internal sealed class FixedCurrentUser : ICurrentUser
    {
        public FixedCurrentUser(int? userId)
        {
            UserId = userId;
        }

        public int? UserId { get; }
    }
}
