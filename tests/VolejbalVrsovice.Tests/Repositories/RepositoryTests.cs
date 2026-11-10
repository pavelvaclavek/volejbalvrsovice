using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using VolejbalVrsovice.Core.Entities;
using VolejbalVrsovice.Tests.TestSupport;
using Xunit;

namespace VolejbalVrsovice.Tests.Repositories
{
    public class RepositoryTests : IDisposable
    {
        private const int AdminId = 1;

        private readonly TestStore _store = TestStore.CreateUnlocked();

        public void Dispose()
        {
            _store.Dispose();
        }

        // --- AppUser ---------------------------------------------------------------------------

        [Fact]
        public async Task User_is_stored_with_normalized_email_and_found_case_insensitively()
        {
            var user = new AppUser { Email = "  Trener@Example.CZ ", DisplayName = "Petr Svoboda", Role = UserRole.User, IsActive = true, MustChangePassword = true };

            await _store.Users.InsertAsync(user);
            var loaded = await _store.Users.GetByEmailAsync("TRENER@example.cz");

            Assert.True(user.Id > AdminId);
            Assert.Equal("trener@example.cz", loaded.Email);
            Assert.Equal("Petr Svoboda", loaded.DisplayName);
            Assert.Equal(UserRole.User, loaded.Role);
            Assert.True(loaded.MustChangePassword);
            Assert.False(loaded.IsAdmin);
        }

        [Fact]
        public async Task User_email_must_be_unique()
        {
            var duplicate = new AppUser { Email = "ADMIN@admin.cz", DisplayName = "Druhý správce", Role = UserRole.Admin, IsActive = true };

            await Assert.ThrowsAsync<SqliteException>(() => _store.Users.InsertAsync(duplicate));
        }

        [Fact]
        public async Task Repository_fills_audit_columns_not_the_caller()
        {
            _store.CurrentUser.UserId = AdminId;
            var team = new Team
            {
                Name = "Ženy A",
                IsActive = true,
                CreatedAt = new DateTime(1999, 1, 1, 0, 0, 0, DateTimeKind.Utc),   // volající hodnoty se ignorují
                CreatedBy = 999,
            };

            await _store.Teams.InsertAsync(team);
            var created = await _store.Teams.GetByIdAsync(team.Id);

            Assert.Equal(new DateTime(2026, 3, 5, 9, 30, 15, DateTimeKind.Utc), created.CreatedAt);
            Assert.Equal(DateTimeKind.Utc, created.CreatedAt.Kind);
            Assert.Equal(AdminId, created.CreatedBy);
            Assert.Equal(created.CreatedAt, created.ModifiedAt);
            Assert.Equal(AdminId, created.ModifiedBy);

            _store.Clock.Advance(TimeSpan.FromHours(2));
            created.Description = "Extraliga";
            await _store.Teams.UpdateAsync(created);
            var updated = await _store.Teams.GetByIdAsync(team.Id);

            Assert.Equal(new DateTime(2026, 3, 5, 9, 30, 15, DateTimeKind.Utc), updated.CreatedAt);
            Assert.Equal(new DateTime(2026, 3, 5, 11, 30, 15, DateTimeKind.Utc), updated.ModifiedAt);
            Assert.Equal("Extraliga", updated.Description);
        }

        [Fact]
        public async Task Login_time_and_active_admin_count_are_tracked()
        {
            var admin = await _store.Users.GetByEmailAsync("admin@admin.cz");
            await _store.Users.RecordLoginAsync(admin.Id);

            await _store.Users.InsertAsync(new AppUser { Email = "druhy@example.cz", DisplayName = "Druhý", Role = UserRole.Admin, IsActive = true });
            await _store.Users.InsertAsync(new AppUser { Email = "neaktivni@example.cz", DisplayName = "Neaktivní", Role = UserRole.Admin, IsActive = false });
            await _store.Users.InsertAsync(new AppUser { Email = "uzivatel@example.cz", DisplayName = "Uživatel", Role = UserRole.User, IsActive = true });

            Assert.Equal(new DateTime(2026, 3, 5, 9, 30, 15, DateTimeKind.Utc), (await _store.Users.GetByIdAsync(admin.Id)).LastLoginAt);
            Assert.Equal(2, await _store.Users.CountActiveAdminsAsync());
            Assert.Equal(4, (await _store.Users.GetAllAsync()).Count);
        }

        [Fact]
        public async Task Updating_a_missing_record_fails()
        {
            var ghost = new Team { Id = 12345, Name = "Neexistuje", IsActive = true };

            await Assert.ThrowsAsync<InvalidOperationException>(() => _store.Teams.UpdateAsync(ghost));
        }

        // --- Team + TrainingSchedule --------------------------------------------------------------

        [Fact]
        public async Task Team_name_must_be_unique()
        {
            await _store.AddTeamAsync("Ženy A");

            await Assert.ThrowsAsync<SqliteException>(() => _store.AddTeamAsync("Ženy A"));
        }

        [Fact]
        public async Task Teams_are_sorted_in_czech_and_inactive_can_be_filtered_out()
        {
            await _store.AddTeamAsync("Ženy A");
            await _store.AddTeamAsync("Chlapci");
            await _store.AddTeamAsync("Čerti", isActive: false);
            await _store.AddTeamAsync("Dorost");

            var all = await _store.Teams.GetAllAsync();
            var active = await _store.Teams.GetAllAsync(includeInactive: false);

            Assert.Equal(new[] { "Čerti", "Dorost", "Chlapci", "Ženy A" }, all.Select(team => team.Name));
            Assert.Equal(new[] { "Dorost", "Chlapci", "Ženy A" }, active.Select(team => team.Name));
        }

        [Fact]
        public async Task Training_times_round_trip_and_are_ordered_from_monday()
        {
            var team = await _store.AddTeamAsync("Ženy A");
            await _store.Trainings.InsertAsync(Training(team, DayOfWeek.Sunday, "10:00", "12:00"));
            await _store.Trainings.InsertAsync(Training(team, DayOfWeek.Wednesday, "19:30", "21:00"));
            await _store.Trainings.InsertAsync(Training(team, DayOfWeek.Monday, "18:00", "19:30"));
            await _store.Trainings.InsertAsync(Training(team, DayOfWeek.Monday, "07:00", "08:15"));

            var trainings = await _store.Trainings.GetByTeamAsync(team.Id);

            Assert.Equal(
                new[] { "Monday 07:00-08:15", "Monday 18:00-19:30", "Wednesday 19:30-21:00", "Sunday 10:00-12:00" },
                trainings.Select(training => training.DayOfWeek + " " + training.TimeFrom.ToString(@"hh\:mm") + "-" + training.TimeTo.ToString(@"hh\:mm")));
            Assert.Equal("Hala Vršovice, Vršovická 1", trainings[0].Address);
            Assert.Equal("19:30", await _store.ScalarAsync<string>("SELECT TimeFrom FROM TrainingSchedule WHERE Id = @id;", new { id = trainings[2].Id }));
        }

        [Fact]
        public async Task Training_must_end_after_it_starts()
        {
            var team = await _store.AddTeamAsync("Ženy A");

            await Assert.ThrowsAsync<SqliteException>(() => _store.Trainings.InsertAsync(Training(team, DayOfWeek.Monday, "19:00", "19:00")));
            await Assert.ThrowsAsync<SqliteException>(() => _store.Trainings.InsertAsync(Training(team, DayOfWeek.Monday, "19:00", "18:00")));
        }

        [Fact]
        public async Task Training_can_be_updated_and_deleted()
        {
            var team = await _store.AddTeamAsync("Ženy A");
            var training = Training(team, DayOfWeek.Friday, "17:00", "18:30");
            await _store.Trainings.InsertAsync(training);

            training.TimeTo = new TimeSpan(19, 0, 0);
            training.Note = "  jen sudé týdny ";
            await _store.Trainings.UpdateAsync(training);

            var loaded = await _store.Trainings.GetByIdAsync(training.Id);
            Assert.Equal(new TimeSpan(19, 0, 0), loaded.TimeTo);
            Assert.Equal("jen sudé týdny", loaded.Note);

            await _store.Trainings.DeleteAsync(training.Id);
            Assert.Null(await _store.Trainings.GetByIdAsync(training.Id));
            await Assert.ThrowsAsync<InvalidOperationException>(() => _store.Trainings.DeleteAsync(training.Id));
        }

        // --- Player + TeamMembership ----------------------------------------------------------------

        [Fact]
        public async Task Player_round_trips_all_fields()
        {
            var team = await _store.AddTeamAsync("Ženy A");
            var player = new Player
            {
                FirstName = " Jana ",
                LastName = "Nováková",
                Gender = Gender.Female,
                BirthDate = new DateTime(1998, 12, 31),
                NationalityCode = "sk",
                Email = "jana@example.cz",
                Phone = "+420 777 123 456",
                Address = "Vršovická 10, Praha 10",
                JoinedOn = new DateTime(2019, 9, 1),
                IsActive = true,
                HeightCm = 178,
                Note = "",
            };

            await _store.Players.InsertAsync(player, new[] { new TeamMembership { TeamId = team.Id, JoinedOn = new DateTime(2019, 9, 1) } });
            var loaded = await _store.Players.GetByIdAsync(player.Id);

            Assert.Equal("Jana", loaded.FirstName);
            Assert.Equal("Nováková", loaded.LastName);
            Assert.Equal(Gender.Female, loaded.Gender);
            Assert.Equal(new DateTime(1998, 12, 31), loaded.BirthDate);
            Assert.Equal(DateTimeKind.Unspecified, loaded.BirthDate.Kind);
            Assert.Equal("SK", loaded.NationalityCode);
            Assert.Equal("+420 777 123 456", loaded.Phone);
            Assert.Equal(new DateTime(2019, 9, 1), loaded.JoinedOn);
            Assert.True(loaded.IsActive);
            Assert.Equal(178, loaded.HeightCm);
            Assert.Null(loaded.Note);
            Assert.Equal("1998-12-31", await _store.ScalarAsync<string>("SELECT BirthDate FROM Player WHERE Id = @id;", new { id = player.Id }));
        }

        [Fact]
        public async Task Player_optional_fields_may_stay_empty()
        {
            var team = await _store.AddTeamAsync("Přípravka");
            var player = new Player { FirstName = "Tomáš", LastName = "Malý", BirthDate = new DateTime(2015, 6, 1), Phone = "777 000 111", JoinedOn = new DateTime(2025, 9, 1), IsActive = true };

            await _store.Players.InsertAsync(player, new[] { new TeamMembership { TeamId = team.Id, JoinedOn = new DateTime(2025, 9, 1) } });
            var loaded = await _store.Players.GetByIdAsync(player.Id);

            Assert.Null(loaded.Gender);
            Assert.Null(loaded.HeightCm);
            Assert.Null(loaded.Email);
            Assert.Equal("CZ", loaded.NationalityCode);   // výchozí státní příslušnost
        }

        [Fact]
        public async Task Player_must_have_at_least_one_membership_when_created()
        {
            var player = new Player { FirstName = "Bez", LastName = "Družstva", BirthDate = new DateTime(2000, 1, 1), Email = "x@example.cz", JoinedOn = new DateTime(2024, 1, 1), IsActive = true };

            await Assert.ThrowsAsync<ArgumentException>(() => _store.Players.InsertAsync(player, new TeamMembership[0]));
            await Assert.ThrowsAsync<ArgumentException>(() => _store.Players.InsertAsync(player, null));

            Assert.Empty(await _store.Players.GetAllAsync());
        }

        [Fact]
        public async Task Player_and_memberships_are_saved_together_or_not_at_all()
        {
            var team = await _store.AddTeamAsync("Ženy A");
            var player = new Player { FirstName = "Eva", LastName = "Dvořáková", BirthDate = new DateTime(2000, 1, 1), Email = "eva@example.cz", JoinedOn = new DateTime(2024, 1, 1), IsActive = true };
            var memberships = new[]
            {
                new TeamMembership { TeamId = team.Id, JoinedOn = new DateTime(2024, 1, 1) },
                new TeamMembership { TeamId = 9999, JoinedOn = new DateTime(2024, 1, 1) },   // neexistující družstvo
            };

            await Assert.ThrowsAsync<SqliteException>(() => _store.Players.InsertAsync(player, memberships));

            Assert.Empty(await _store.Players.GetAllAsync());
            Assert.Equal(0, await _store.ScalarAsync<int>("SELECT count(*) FROM TeamMembership;"));
        }

        [Theory]
        [InlineData("contact")]
        [InlineData("heightLow")]
        [InlineData("heightHigh")]
        [InlineData("nationality")]
        public async Task Player_rules_are_enforced_by_the_database(string rule)
        {
            var team = await _store.AddTeamAsync("Ženy A");
            var player = new Player { FirstName = "Eva", LastName = "Dvořáková", BirthDate = new DateTime(2000, 1, 1), Email = "eva@example.cz", JoinedOn = new DateTime(2024, 1, 1), IsActive = true };

            switch (rule)
            {
                case "contact":
                    player.Email = " ";       // ani e-mail, ani telefon
                    player.Phone = null;
                    break;
                case "heightLow":
                    player.HeightCm = 99;
                    break;
                case "heightHigh":
                    player.HeightCm = 251;
                    break;
                case "nationality":
                    player.NationalityCode = "XX";   // není v číselníku ISO
                    break;
            }

            await Assert.ThrowsAsync<SqliteException>(() =>
                _store.Players.InsertAsync(player, new[] { new TeamMembership { TeamId = team.Id, JoinedOn = new DateTime(2024, 1, 1) } }));
        }

        [Fact]
        public async Task Player_in_two_teams_is_listed_in_both()
        {
            var womenA = await _store.AddTeamAsync("Ženy A");
            var mixed = await _store.AddTeamAsync("Smíšené – rekreační");
            var both = await _store.AddPlayerAsync("Jana", "Nováková", womenA, mixed);
            var onlyA = await _store.AddPlayerAsync("Eva", "Čermáková", womenA);

            var inA = await _store.Players.GetByTeamAsync(womenA.Id);
            var inMixed = await _store.Players.GetByTeamAsync(mixed.Id);

            Assert.Equal(new[] { onlyA.Id, both.Id }, inA.Select(player => player.Id));   // Čermáková před Novákovou
            Assert.Equal(new[] { both.Id }, inMixed.Select(player => player.Id));
            Assert.Equal(2, (await _store.Memberships.GetByPlayerAsync(both.Id)).Count);
            Assert.Equal(2, (await _store.Players.GetAllAsync()).Count);
        }

        [Fact]
        public async Task Ended_membership_keeps_history_but_removes_player_from_team_list()
        {
            var team = await _store.AddTeamAsync("Ženy A");
            var player = await _store.AddPlayerAsync("Jana", "Nováková", team);
            var membership = (await _store.Memberships.GetByPlayerAsync(player.Id)).Single();

            membership.LeftOn = new DateTime(2025, 6, 30);
            await _store.Memberships.UpdateAsync(membership);

            Assert.Empty(await _store.Players.GetByTeamAsync(team.Id));
            var history = (await _store.Memberships.GetByTeamAsync(team.Id)).Single();
            Assert.Equal(new DateTime(2025, 6, 30), history.LeftOn);
            Assert.False(history.IsCurrent);
        }

        [Fact]
        public async Task Membership_pair_is_unique_and_cannot_end_before_it_started()
        {
            var team = await _store.AddTeamAsync("Ženy A");
            var player = await _store.AddPlayerAsync("Jana", "Nováková", team);

            await Assert.ThrowsAsync<SqliteException>(() => _store.Memberships.InsertAsync(
                new TeamMembership { PlayerId = player.Id, TeamId = team.Id, JoinedOn = new DateTime(2026, 1, 1) }));

            var membership = (await _store.Memberships.GetByPlayerAsync(player.Id)).Single();
            membership.LeftOn = membership.JoinedOn.AddDays(-1);
            await Assert.ThrowsAsync<SqliteException>(() => _store.Memberships.UpdateAsync(membership));
        }

        // --- Contribution --------------------------------------------------------------------------

        [Fact]
        public async Task Money_is_stored_as_integer_halere()
        {
            var player = await _store.AddPlayerAsync("Jana", "Nováková", await _store.AddTeamAsync("Ženy A"));
            var contribution = new Contribution
            {
                PlayerId = player.Id,
                Date = new DateTime(2026, 1, 15),
                Amount = 1250.50m,
                PaymentMethod = PaymentMethod.BankTransfer,
                VariableSymbol = "2026001",
                BankReference = "TX-778899",
                Note = "příspěvek jaro",
            };

            await _store.Contributions.InsertAsync(contribution);
            var loaded = await _store.Contributions.GetByIdAsync(contribution.Id);

            Assert.Equal(125050L, await _store.ScalarAsync<long>("SELECT Amount FROM Contribution WHERE Id = @id;", new { id = contribution.Id }));
            Assert.Equal("integer", await _store.ScalarAsync<string>("SELECT typeof(Amount) FROM Contribution WHERE Id = @id;", new { id = contribution.Id }));
            Assert.Equal(1250.50m, loaded.Amount);
            Assert.Equal(PaymentMethod.BankTransfer, loaded.PaymentMethod);
            Assert.Equal("2026001", loaded.VariableSymbol);
            Assert.Equal("TX-778899", loaded.BankReference);
            Assert.Equal(new DateTime(2026, 1, 15), loaded.Date);
        }

        [Fact]
        public async Task Amount_with_more_than_two_decimals_is_refused()
        {
            var player = await _store.AddPlayerAsync("Jana", "Nováková", await _store.AddTeamAsync("Ženy A"));

            await Assert.ThrowsAsync<ArgumentException>(() => _store.AddContributionAsync(player, 10.005m));
        }

        [Theory]
        [InlineData(PaymentMethod.BankTransfer, 0)]
        [InlineData(PaymentMethod.BankTransfer, -100)]
        [InlineData(PaymentMethod.Cash, 0)]
        [InlineData(PaymentMethod.Cash, -1)]
        [InlineData(PaymentMethod.BalanceTransfer, 0)]
        public async Task Invalid_contribution_amounts_are_refused(PaymentMethod method, int amount)
        {
            var player = await _store.AddPlayerAsync("Jana", "Nováková", await _store.AddTeamAsync("Ženy A"));

            await Assert.ThrowsAsync<SqliteException>(() => _store.AddContributionAsync(player, amount, method));
        }

        [Fact]
        public async Task Balance_transfer_may_be_negative_but_only_one_per_player()
        {
            var team = await _store.AddTeamAsync("Ženy A");
            var jana = await _store.AddPlayerAsync("Jana", "Nováková", team);
            var eva = await _store.AddPlayerAsync("Eva", "Čermáková", team);

            Assert.False(await _store.Contributions.HasBalanceTransferAsync(jana.Id));
            await _store.AddContributionAsync(jana, -450m, PaymentMethod.BalanceTransfer);
            Assert.True(await _store.Contributions.HasBalanceTransferAsync(jana.Id));

            // Druhý převod salda u téhož hráče je odmítnut, u jiného hráče je v pořádku.
            await Assert.ThrowsAsync<SqliteException>(() => _store.AddContributionAsync(jana, 100m, PaymentMethod.BalanceTransfer));
            await _store.AddContributionAsync(eva, 200m, PaymentMethod.BalanceTransfer);

            // Běžné příspěvky omezené nejsou.
            await _store.AddContributionAsync(jana, 500m);
            await _store.AddContributionAsync(jana, 500m, PaymentMethod.Cash);
            Assert.Equal(3, (await _store.Contributions.GetByPlayerAsync(jana.Id)).Count);
        }

        [Fact]
        public async Task Variable_symbol_belongs_only_to_bank_transfers()
        {
            var player = await _store.AddPlayerAsync("Jana", "Nováková", await _store.AddTeamAsync("Ženy A"));
            var cash = new Contribution { PlayerId = player.Id, Date = new DateTime(2026, 1, 15), Amount = 300m, PaymentMethod = PaymentMethod.Cash, VariableSymbol = "123" };

            await Assert.ThrowsAsync<SqliteException>(() => _store.Contributions.InsertAsync(cash));
        }

        [Fact]
        public async Task Contribution_can_be_updated_and_deleted()
        {
            var player = await _store.AddPlayerAsync("Jana", "Nováková", await _store.AddTeamAsync("Ženy A"));
            var contribution = await _store.AddContributionAsync(player, 500m);

            contribution.Amount = 550m;
            contribution.Date = new DateTime(2026, 2, 1);
            await _store.Contributions.UpdateAsync(contribution);
            Assert.Equal(550m, (await _store.Contributions.GetByIdAsync(contribution.Id)).Amount);

            await _store.Contributions.DeleteAsync(contribution.Id);
            Assert.Empty(await _store.Contributions.GetByPlayerAsync(player.Id));
        }

        // --- Event + EventParticipant -----------------------------------------------------------------

        [Fact]
        public async Task Event_and_participants_round_trip()
        {
            var player = await _store.AddPlayerAsync("Jana", "Nováková", await _store.AddTeamAsync("Ženy A"));
            var evt = new Event
            {
                Name = "Soustředění Lipno",
                Date = new DateTime(2026, 7, 10),
                DateTo = new DateTime(2026, 7, 14),
                Location = "Lipno nad Vltavou",
                DefaultCost = 2500m,
                Description = "letní soustředění",
            };

            await _store.Events.InsertAsync(evt);
            var participant = await _store.AddParticipantAsync(evt, player);
            participant.Cost = 1800m;
            participant.IsCostOverridden = true;
            participant.Note = "jen dva dny";
            await _store.Participants.UpdateAsync(participant);

            var loadedEvent = await _store.Events.GetByIdAsync(evt.Id);
            var loadedParticipant = (await _store.Participants.GetByEventAsync(evt.Id)).Single();

            Assert.Equal("Soustředění Lipno", loadedEvent.Name);
            Assert.Equal(new DateTime(2026, 7, 14), loadedEvent.DateTo);
            Assert.Equal(2500m, loadedEvent.DefaultCost);
            Assert.False(loadedEvent.IsClosed);
            Assert.Equal(1800m, loadedParticipant.Cost);
            Assert.True(loadedParticipant.IsCostOverridden);
            Assert.Equal("jen dva dny", loadedParticipant.Note);
            Assert.Equal(participant.Id, (await _store.Participants.GetByPlayerAsync(player.Id)).Single().Id);
            Assert.Equal(250000L, await _store.ScalarAsync<long>("SELECT DefaultCost FROM Event WHERE Id = @id;", new { id = evt.Id }));
        }

        [Fact]
        public async Task Events_are_listed_from_the_newest()
        {
            await _store.Events.InsertAsync(new Event { Name = "Stará", Date = new DateTime(2025, 5, 1) });
            await _store.Events.InsertAsync(new Event { Name = "Nová", Date = new DateTime(2026, 5, 1) });
            await _store.Events.InsertAsync(new Event { Name = "Prostřední", Date = new DateTime(2025, 12, 6) });

            Assert.Equal(new[] { "Nová", "Prostřední", "Stará" }, (await _store.Events.GetAllAsync()).Select(evt => evt.Name));
        }

        [Fact]
        public async Task Participant_is_unique_per_event_and_cost_cannot_be_negative()
        {
            var player = await _store.AddPlayerAsync("Jana", "Nováková", await _store.AddTeamAsync("Ženy A"));
            var evt = await _store.AddEventAsync("Vánoční turnaj", 500m);
            await _store.AddParticipantAsync(evt, player);

            await Assert.ThrowsAsync<SqliteException>(() => _store.AddParticipantAsync(evt, player));

            var other = await _store.AddEventAsync("Jarní turnaj", 500m);
            await Assert.ThrowsAsync<SqliteException>(() => _store.AddParticipantAsync(other, player, -1m));
            await Assert.ThrowsAsync<SqliteException>(() => _store.AddEventAsync("Záporná", -10m));
        }

        [Fact]
        public async Task Participant_can_be_removed()
        {
            var player = await _store.AddPlayerAsync("Jana", "Nováková", await _store.AddTeamAsync("Ženy A"));
            var evt = await _store.AddEventAsync("Vánoční turnaj", 500m);
            var participant = await _store.AddParticipantAsync(evt, player);

            await _store.Participants.DeleteAsync(participant.Id);

            Assert.Empty(await _store.Participants.GetByEventAsync(evt.Id));
            Assert.Null(await _store.Participants.GetByIdAsync(participant.Id));
        }

        // --- AuditLog ------------------------------------------------------------------------------

        [Fact]
        public async Task Audit_entries_are_appended_with_time_and_user()
        {
            _store.CurrentUser.UserId = AdminId;

            var first = new AuditLogEntry { Entity = "Contribution", EntityId = 7, Action = AuditActions.Insert, NewValues = "{\"Amount\":500}" };
            await _store.Audit.AddAsync(first);
            _store.Clock.Advance(TimeSpan.FromMinutes(5));
            await _store.Audit.AddAsync(new AuditLogEntry { Entity = "Contribution", EntityId = 7, Action = AuditActions.Update, OldValues = "{\"Amount\":500}", NewValues = "{\"Amount\":550}" });
            await _store.Audit.AddAsync(new AuditLogEntry { Entity = "Event", EntityId = 7, Action = AuditActions.Close });

            var history = await _store.Audit.GetByEntityAsync("Contribution", 7);

            Assert.Equal(new[] { AuditActions.Insert, AuditActions.Update }, history.Select(entry => entry.Action));
            Assert.Equal(first.Id, history[0].Id);
            Assert.Equal(new DateTime(2026, 3, 5, 9, 30, 15, DateTimeKind.Utc), history[0].Timestamp);
            Assert.Equal(new DateTime(2026, 3, 5, 9, 35, 15, DateTimeKind.Utc), history[1].Timestamp);
            Assert.All(history, entry => Assert.Equal(AdminId, entry.UserId));
            Assert.Null(history[0].OldValues);
            Assert.Equal("{\"Amount\":550}", history[1].NewValues);
        }

        private static TrainingSchedule Training(Team team, DayOfWeek day, string from, string to)
        {
            return new TrainingSchedule
            {
                TeamId = team.Id,
                DayOfWeek = day,
                TimeFrom = TimeSpan.Parse(from),
                TimeTo = TimeSpan.Parse(to),
                Address = "Hala Vršovice, Vršovická 1",
            };
        }
    }
}
