namespace VolejbalVrsovice.Core.Entities
{
    /// <summary>Družstvo v rámci klubu (např. „Ženy A“, „Smíšené – rekreační“, „Přípravka“).</summary>
    public sealed class Team : EntityBase
    {
        /// <summary>Název – unikátní.</summary>
        public string Name { get; set; }

        public string Description { get; set; }

        /// <summary>Vedoucí družstva.</summary>
        public string ContactPerson { get; set; }

        public string ContactEmail { get; set; }

        public string ContactPhone { get; set; }

        public bool IsActive { get; set; }
    }
}
