using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using Serilog.Core;
using VolejbalVrsovice.Core.Entities;
using VolejbalVrsovice.Core.Security;
using VolejbalVrsovice.Data.Db;
using VolejbalVrsovice.Data.Repositories;
using VolejbalVrsovice.Data.Security;
using VolejbalVrsovice.Data.Storage;

namespace VolejbalVrsovice.Tests.TestSupport
{
    /// <summary>
    /// Úložiště (šifrovaná databáze + keys.json) v dočasné složce. PBKDF2 tu běží s malým počtem
    /// iterací, aby testy byly rychlé – produkční hodnotu hlídá samostatný test.
    /// </summary>
    internal sealed class TestStore : IDisposable
    {
        public const int FastIterations = 1000;

        private readonly TempDirectory _temp = new TempDirectory();
        private byte[] _key;

        public TestStore(int iterations = FastIterations, IEnumerable<Migration> migrations = null)
        {
            Paths = new AppPaths(_temp.Path);
            Clock = new FakeClock();
            CurrentUser = new FakeCurrentUser();
            Kdf = new KdfSettings(iterations);
            KeyStore = new KeyStore(Paths.KeysFile);
            Connections = new SqliteConnectionFactory(Paths.DatabaseFile);
            Backups = new BackupService(Paths, Clock);
            Migrator = migrations == null ? new Migrator() : new Migrator(migrations);
            DataStore = CreateDataStore(Migrator);
        }

        public AppPaths Paths { get; }

        public FakeClock Clock { get; }

        public FakeCurrentUser CurrentUser { get; }

        public KdfSettings Kdf { get; }

        public KeyStore KeyStore { get; }

        public SqliteConnectionFactory Connections { get; }

        public BackupService Backups { get; }

        public Migrator Migrator { get; }

        public DataStore DataStore { get; }

        /// <summary>Klíč databáze – k dispozici po <see cref="CreateUnlocked"/> nebo <see cref="Unlock"/>.</summary>
        public byte[] Key
        {
            get { return _key ?? throw new InvalidOperationException("Úložiště není odemčené."); }
        }

        public IDatabase Database
        {
            get { return new Database(Connections, new FixedKeySource(Key)); }
        }

        public AppUserRepository Users
        {
            get { return new AppUserRepository(Database, CurrentUser, Clock); }
        }

        public TeamRepository Teams
        {
            get { return new TeamRepository(Database, CurrentUser, Clock); }
        }

        public TrainingScheduleRepository Trainings
        {
            get { return new TrainingScheduleRepository(Database, CurrentUser, Clock); }
        }

        public PlayerRepository Players
        {
            get { return new PlayerRepository(Database, CurrentUser, Clock); }
        }

        public TeamMembershipRepository Memberships
        {
            get { return new TeamMembershipRepository(Database, CurrentUser, Clock); }
        }

        public ContributionRepository Contributions
        {
            get { return new ContributionRepository(Database, CurrentUser, Clock); }
        }

        public EventRepository Events
        {
            get { return new EventRepository(Database, CurrentUser, Clock); }
        }

        public EventParticipantRepository Participants
        {
            get { return new EventParticipantRepository(Database, CurrentUser, Clock); }
        }

        public AuditLogRepository Audit
        {
            get { return new AuditLogRepository(Database, CurrentUser, Clock); }
        }

        public CountryRepository Countries
        {
            get { return new CountryRepository(Database); }
        }

        public BalanceQueries Balances
        {
            get { return new BalanceQueries(Database); }
        }

        /// <summary>Založí nové úložiště (první spuštění) a odemkne ho výchozím správcem.</summary>
        public static TestStore CreateUnlocked()
        {
            var store = new TestStore();
            store.DataStore.CreateNew();
            store.Unlock();
            return store;
        }

        /// <summary>Odemkne klíč databáze heslem výchozího správce (admin / admin).</summary>
        public byte[] Unlock(string password = DefaultAdmin.Password)
        {
            if (!KeyEnvelope.TryUnwrap(KeyStore.FindUser(DefaultAdmin.Email), password, KeyBinding.ForUser(DefaultAdmin.Email), out var key))
            {
                throw new InvalidOperationException("Výchozí správce se nepodařilo odemknout.");
            }

            _key = key;
            return key;
        }

        public DataStore CreateDataStore(Migrator migrator)
        {
            return new DataStore(Paths, KeyStore, Connections, migrator, Backups, Clock, Kdf, Logger.None);
        }

        public Task<T> ScalarAsync<T>(string sql, object parameters = null)
        {
            return Database.ReadAsync(connection => connection.ExecuteScalarAsync<T>(sql, parameters));
        }

        public Task<int> ExecuteAsync(string sql, object parameters = null)
        {
            return Database.WriteAsync((connection, transaction) => connection.ExecuteAsync(sql, parameters, transaction));
        }

        // --- stavebnice testovacích dat ---------------------------------------------------------

        public async Task<Team> AddTeamAsync(string name, bool isActive = true)
        {
            var team = new Team { Name = name, IsActive = isActive };
            await Teams.InsertAsync(team);
            return team;
        }

        public async Task<Player> AddPlayerAsync(string firstName, string lastName, params Team[] teams)
        {
            var player = new Player
            {
                FirstName = firstName,
                LastName = lastName,
                BirthDate = new DateTime(1995, 4, 12),
                NationalityCode = Country.DefaultCode,
                Email = (firstName + "." + lastName + "@example.cz").ToLowerInvariant(),
                JoinedOn = new DateTime(2020, 9, 1),
                IsActive = true,
            };

            var memberships = new List<TeamMembership>();
            foreach (var team in teams)
            {
                memberships.Add(new TeamMembership { TeamId = team.Id, JoinedOn = new DateTime(2020, 9, 1) });
            }

            await Players.InsertAsync(player, memberships);
            return player;
        }

        public async Task<Contribution> AddContributionAsync(Player player, decimal amount, PaymentMethod method = PaymentMethod.BankTransfer)
        {
            var contribution = new Contribution
            {
                PlayerId = player.Id,
                Date = new DateTime(2026, 1, 15),
                Amount = amount,
                PaymentMethod = method,
            };

            await Contributions.InsertAsync(contribution);
            return contribution;
        }

        public async Task<Event> AddEventAsync(string name, decimal defaultCost)
        {
            var evt = new Event { Name = name, Date = new DateTime(2026, 2, 20), DefaultCost = defaultCost };
            await Events.InsertAsync(evt);
            return evt;
        }

        public async Task<EventParticipant> AddParticipantAsync(Event evt, Player player, decimal? cost = null)
        {
            var participant = new EventParticipant
            {
                EventId = evt.Id,
                PlayerId = player.Id,
                Cost = cost ?? evt.DefaultCost,
                IsCostOverridden = cost.HasValue,
            };

            await Participants.InsertAsync(participant);
            return participant;
        }

        public void Dispose()
        {
            if (_key != null)
            {
                KeyEnvelope.Clear(_key);
                _key = null;
            }

            _temp.Dispose();
        }
    }
}
