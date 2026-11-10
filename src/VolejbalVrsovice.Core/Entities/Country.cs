namespace VolejbalVrsovice.Core.Entities
{
    /// <summary>Položka číselníku zemí (státní příslušnost hráče).</summary>
    public sealed class Country
    {
        public const string DefaultCode = "CZ";

        /// <summary>Kód ISO 3166-1 alpha-2, např. „CZ“.</summary>
        public string Code { get; set; }

        /// <summary>Český název země.</summary>
        public string Name { get; set; }
    }
}
