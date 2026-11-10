namespace VolejbalVrsovice.Core.Models
{
    /// <summary>
    /// Saldo hráče: <c>Σ příspěvků − Σ nákladů na akce</c>. Nikde se neukládá, vždy se počítá
    /// dotazem v databázi. Záporné saldo = hráč dluží. Saldo je za hráče, ne za družstvo.
    /// </summary>
    public sealed class PlayerBalance
    {
        public int PlayerId { get; set; }

        /// <summary>Součet příspěvků v Kč (včetně převodu salda, který může být záporný).</summary>
        public decimal ContributionTotal { get; set; }

        /// <summary>Součet nákladů na akce v Kč.</summary>
        public decimal CostTotal { get; set; }

        /// <summary>Saldo v Kč.</summary>
        public decimal Balance { get; set; }

        public bool IsNegative
        {
            get { return Balance < 0m; }
        }
    }

    /// <summary>Souhrn dlužníků – počítá se přes různé hráče (hráč ve dvou družstvech jen jednou).</summary>
    public sealed class DebtSummary
    {
        /// <summary>Počet hráčů se záporným saldem.</summary>
        public int PlayersInDebt { get; set; }

        /// <summary>Součet záporných sald v Kč (záporné číslo, nebo 0).</summary>
        public decimal TotalDebt { get; set; }
    }
}
