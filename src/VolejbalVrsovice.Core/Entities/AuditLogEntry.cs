using System;

namespace VolejbalVrsovice.Core.Entities
{
    /// <summary>
    /// Záznam auditu změn finančních údajů (příspěvek, účast na akci, výchozí náklad akce,
    /// uzavření a znovuotevření akce). Záznamy se jen přidávají.
    /// </summary>
    public sealed class AuditLogEntry
    {
        public int Id { get; set; }

        /// <summary>Okamžik změny (UTC).</summary>
        public DateTime Timestamp { get; set; }

        /// <summary>Id uživatele aplikace, který změnu provedl; <c>null</c> = systém.</summary>
        public int? UserId { get; set; }

        /// <summary>Název entity, např. <c>Contribution</c>.</summary>
        public string Entity { get; set; }

        public int EntityId { get; set; }

        /// <summary>Druh změny – viz <see cref="AuditActions"/>.</summary>
        public string Action { get; set; }

        /// <summary>Původní hodnoty (JSON); u vložení <c>null</c>.</summary>
        public string OldValues { get; set; }

        /// <summary>Nové hodnoty (JSON); u smazání <c>null</c>.</summary>
        public string NewValues { get; set; }
    }

    /// <summary>Hodnoty sloupce <see cref="AuditLogEntry.Action"/>.</summary>
    public static class AuditActions
    {
        public const string Insert = "Insert";
        public const string Update = "Update";
        public const string Delete = "Delete";
        public const string Close = "Close";
        public const string Reopen = "Reopen";
    }
}
