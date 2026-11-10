using System;

namespace VolejbalVrsovice.Core.Entities
{
    /// <summary>Akce – nepravidelná společenská událost, která eviduje náklady účastníků.</summary>
    public sealed class Event : EntityBase
    {
        public string Name { get; set; }

        /// <summary>Datum konání, u vícedenní akce první den (jen datum).</summary>
        public DateTime Date { get; set; }

        /// <summary>Poslední den vícedenní akce; u jednodenní <c>null</c>.</summary>
        public DateTime? DateTo { get; set; }

        public string Location { get; set; }

        /// <summary>Výchozí náklad na účastníka v Kč (≥ 0).</summary>
        public decimal DefaultCost { get; set; }

        public string Description { get; set; }

        /// <summary>Uzavřená akce má zamčené účastníky i náklady.</summary>
        public bool IsClosed { get; set; }
    }
}
