using System;
using System.Linq;
using System.Threading.Tasks;
using VolejbalVrsovice.Core.Entities;
using VolejbalVrsovice.Tests.TestSupport;
using Xunit;

namespace VolejbalVrsovice.Tests.Repositories
{
    /// <summary>Saldo(hráč) = Σ příspěvků − Σ nákladů na akce (zadání, sekce 6.7).</summary>
    public class BalanceTests : IDisposable
    {
        private readonly TestStore _store = TestStore.CreateUnlocked();

        public void Dispose()
        {
            _store.Dispose();
        }

        [Fact]
        public async Task Player_without_any_movement_has_zero_balance()
        {
            var player = await _store.AddPlayerAsync("Jana", "Nováková", await _store.AddTeamAsync("Ženy A"));

            var balance = await _store.Balances.GetPlayerBalanceAsync(player.Id);

            Assert.Equal(0m, balance.ContributionTotal);
            Assert.Equal(0m, balance.CostTotal);
            Assert.Equal(0m, balance.Balance);
            Assert.False(balance.IsNegative);
        }

        [Fact]
        public async Task Unknown_player_has_no_balance()
        {
            Assert.Null(await _store.Balances.GetPlayerBalanceAsync(4711));
        }

        /// <summary>Akceptační kritérium 3: příspěvky 1 000 Kč, akce 600 + 700 Kč → saldo −300 Kč.</summary>
        [Fact]
        public async Task Balance_is_contributions_minus_event_costs()
        {
            var player = await _store.AddPlayerAsync("Jana", "Nováková", await _store.AddTeamAsync("Ženy A"));
            await _store.AddContributionAsync(player, 1000m);
            await _store.AddParticipantAsync(await _store.AddEventAsync("Vánoční turnaj", 600m), player);
            await _store.AddParticipantAsync(await _store.AddEventAsync("Soustředění", 700m), player);

            var balance = await _store.Balances.GetPlayerBalanceAsync(player.Id);

            Assert.Equal(1000m, balance.ContributionTotal);
            Assert.Equal(1300m, balance.CostTotal);
            Assert.Equal(-300m, balance.Balance);
            Assert.True(balance.IsNegative);
        }

        /// <summary>Akceptační kritérium 8 (část se saldem): převod salda −450 Kč bez dalších pohybů → saldo −450 Kč.</summary>
        [Fact]
        public async Task Negative_balance_transfer_alone_gives_a_negative_balance()
        {
            var player = await _store.AddPlayerAsync("Jana", "Nováková", await _store.AddTeamAsync("Ženy A"));
            await _store.AddContributionAsync(player, -450m, PaymentMethod.BalanceTransfer);

            var balance = await _store.Balances.GetPlayerBalanceAsync(player.Id);

            Assert.Equal(-450m, balance.ContributionTotal);
            Assert.Equal(0m, balance.CostTotal);
            Assert.Equal(-450m, balance.Balance);
        }

        [Fact]
        public async Task Balance_transfer_is_combined_with_other_movements()
        {
            var player = await _store.AddPlayerAsync("Jana", "Nováková", await _store.AddTeamAsync("Ženy A"));
            await _store.AddContributionAsync(player, -450m, PaymentMethod.BalanceTransfer);
            await _store.AddContributionAsync(player, 300m, PaymentMethod.Cash);
            await _store.AddContributionAsync(player, 399.50m);
            await _store.AddParticipantAsync(await _store.AddEventAsync("Turnaj", 120.25m), player);

            var balance = await _store.Balances.GetPlayerBalanceAsync(player.Id);

            Assert.Equal(249.50m, balance.ContributionTotal);
            Assert.Equal(120.25m, balance.CostTotal);
            Assert.Equal(129.25m, balance.Balance);
        }

        [Fact]
        public async Task Overridden_cost_counts_instead_of_the_default_cost()
        {
            var player = await _store.AddPlayerAsync("Jana", "Nováková", await _store.AddTeamAsync("Ženy A"));
            var evt = await _store.AddEventAsync("Soustředění", 500m);
            await _store.AddParticipantAsync(evt, player, 200m);

            Assert.Equal(-200m, (await _store.Balances.GetPlayerBalanceAsync(player.Id)).Balance);
        }

        [Fact]
        public async Task Removing_a_movement_changes_the_balance_immediately()
        {
            var player = await _store.AddPlayerAsync("Jana", "Nováková", await _store.AddTeamAsync("Ženy A"));
            var contribution = await _store.AddContributionAsync(player, 800m);
            var participant = await _store.AddParticipantAsync(await _store.AddEventAsync("Turnaj", 300m), player);

            await _store.Contributions.DeleteAsync(contribution.Id);
            Assert.Equal(-300m, (await _store.Balances.GetPlayerBalanceAsync(player.Id)).Balance);

            await _store.Participants.DeleteAsync(participant.Id);
            Assert.Equal(0m, (await _store.Balances.GetPlayerBalanceAsync(player.Id)).Balance);
        }

        [Fact]
        public async Task Balance_belongs_to_the_player_not_to_the_team()
        {
            var womenA = await _store.AddTeamAsync("Ženy A");
            var mixed = await _store.AddTeamAsync("Smíšené – rekreační");
            var jana = await _store.AddPlayerAsync("Jana", "Nováková", womenA, mixed);   // ve dvou družstvech
            var eva = await _store.AddPlayerAsync("Eva", "Čermáková", womenA);

            await _store.AddContributionAsync(jana, 1000m);
            await _store.AddParticipantAsync(await _store.AddEventAsync("Turnaj žen", 600m), jana);
            await _store.AddParticipantAsync(await _store.AddEventAsync("Turnaj smíšených", 700m), jana);
            await _store.AddContributionAsync(eva, 250m);

            var inWomenA = await _store.Balances.GetByTeamAsync(womenA.Id);
            var inMixed = await _store.Balances.GetByTeamAsync(mixed.Id);

            // V obou družstvech je vidět celkové saldo hráčky – včetně akce, na kterou šla s jiným družstvem.
            Assert.Equal(-300m, inWomenA.Single(balance => balance.PlayerId == jana.Id).Balance);
            Assert.Equal(-300m, inMixed.Single(balance => balance.PlayerId == jana.Id).Balance);
            Assert.Equal(250m, inWomenA.Single(balance => balance.PlayerId == eva.Id).Balance);
            Assert.Equal(2, inWomenA.Count);
            Assert.Single(inMixed);
        }

        [Fact]
        public async Task Debt_summary_counts_a_player_in_two_teams_only_once()
        {
            var womenA = await _store.AddTeamAsync("Ženy A");
            var mixed = await _store.AddTeamAsync("Smíšené – rekreační");
            var prep = await _store.AddTeamAsync("Přípravka");

            var jana = await _store.AddPlayerAsync("Jana", "Nováková", womenA, mixed);   // −300, ve dvou družstvech
            var eva = await _store.AddPlayerAsync("Eva", "Čermáková", womenA);           // −100
            var petr = await _store.AddPlayerAsync("Petr", "Svoboda", mixed);            // +50
            var tom = await _store.AddPlayerAsync("Tomáš", "Malý", prep);                // −40, jiné družstvo

            await _store.AddContributionAsync(jana, -300m, PaymentMethod.BalanceTransfer);
            await _store.AddContributionAsync(eva, -100m, PaymentMethod.BalanceTransfer);
            await _store.AddContributionAsync(petr, 50m);
            await _store.AddContributionAsync(tom, -40m, PaymentMethod.BalanceTransfer);

            var both = await _store.Balances.GetDebtSummaryAsync(new[] { womenA.Id, mixed.Id });
            var onlyWomenA = await _store.Balances.GetDebtSummaryAsync(new[] { womenA.Id });
            var onlyMixed = await _store.Balances.GetDebtSummaryAsync(new[] { mixed.Id });
            var wholeClub = await _store.Balances.GetDebtSummaryAsync();
            var noTeam = await _store.Balances.GetDebtSummaryAsync(new int[0]);

            Assert.Equal(2, both.PlayersInDebt);          // Jana jen jednou
            Assert.Equal(-400m, both.TotalDebt);          // −300 − 100, ne −700

            Assert.Equal(2, onlyWomenA.PlayersInDebt);
            Assert.Equal(-400m, onlyWomenA.TotalDebt);

            Assert.Equal(1, onlyMixed.PlayersInDebt);
            Assert.Equal(-300m, onlyMixed.TotalDebt);

            Assert.Equal(3, wholeClub.PlayersInDebt);
            Assert.Equal(-440m, wholeClub.TotalDebt);

            Assert.Equal(0, noTeam.PlayersInDebt);
            Assert.Equal(0m, noTeam.TotalDebt);
        }

        [Fact]
        public async Task Former_team_member_is_not_in_the_team_balance_list()
        {
            var team = await _store.AddTeamAsync("Ženy A");
            var player = await _store.AddPlayerAsync("Jana", "Nováková", team);
            await _store.AddContributionAsync(player, -200m, PaymentMethod.BalanceTransfer);

            var membership = (await _store.Memberships.GetByPlayerAsync(player.Id)).Single();
            membership.LeftOn = new DateTime(2025, 6, 30);
            await _store.Memberships.UpdateAsync(membership);

            Assert.Empty(await _store.Balances.GetByTeamAsync(team.Id));
            Assert.Equal(0, (await _store.Balances.GetDebtSummaryAsync(new[] { team.Id })).PlayersInDebt);

            // Dluh hráčky tím nezmizel – v celoklubovém souhrnu zůstává.
            Assert.Equal(-200m, (await _store.Balances.GetDebtSummaryAsync()).TotalDebt);
            Assert.Equal(-200m, (await _store.Balances.GetPlayerBalanceAsync(player.Id)).Balance);
        }

        [Fact]
        public async Task All_balances_are_returned_for_every_player()
        {
            var team = await _store.AddTeamAsync("Ženy A");
            var jana = await _store.AddPlayerAsync("Jana", "Nováková", team);
            var eva = await _store.AddPlayerAsync("Eva", "Čermáková", team);
            await _store.AddContributionAsync(jana, 100m);

            var all = await _store.Balances.GetAllAsync();

            Assert.Equal(new[] { jana.Id, eva.Id }, all.Select(balance => balance.PlayerId));
            Assert.Equal(new[] { 100m, 0m }, all.Select(balance => balance.Balance));
        }
    }
}
