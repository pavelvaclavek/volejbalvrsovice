using System;

namespace VolejbalVrsovice.Core.Entities
{
    /// <summary>
    /// Příspěvek hráče (příjem). Není vázán na akci ani na družstvo – jde do jedné „peněženky“ hráče.
    /// </summary>
    public sealed class Contribution : EntityBase
    {
        public int PlayerId { get; set; }

        /// <summary>Datum přijetí (jen datum); u převodu salda datum převodu.</summary>
        public DateTime Date { get; set; }

        /// <summary>
        /// Částka v Kč. U skutečného příjmu vždy kladná; u <see cref="Entities.PaymentMethod.BalanceTransfer"/>
        /// může být i záporná (dluh), nikdy však 0.
        /// </summary>
        public decimal Amount { get; set; }

        public PaymentMethod PaymentMethod { get; set; }

        /// <summary>Variabilní symbol – jen u bankovního převodu.</summary>
        public string VariableSymbol { get; set; }

        /// <summary>ID transakce nebo poznámka z bankovního výpisu.</summary>
        public string BankReference { get; set; }

        public string Note { get; set; }

        public bool IsBalanceTransfer
        {
            get { return PaymentMethod == PaymentMethod.BalanceTransfer; }
        }
    }
}
