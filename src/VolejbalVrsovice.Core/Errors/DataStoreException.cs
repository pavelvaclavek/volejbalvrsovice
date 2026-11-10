using System;

namespace VolejbalVrsovice.Core.Errors
{
    /// <summary>
    /// Chyba úložiště dat (databáze + soubor klíčů). <see cref="Exception.Message"/> je česká hláška
    /// určená přímo uživateli a nikdy neobsahuje osobní údaje, hesla ani klíče.
    /// </summary>
    public abstract class DataStoreException : Exception
    {
        protected DataStoreException(string message)
            : base(message)
        {
        }

        protected DataStoreException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// Databáze a soubor klíčů k sobě nepatří nebo jeden z nich chybí. Aplikace v takové situaci
    /// nesmí nic vytvořit ani přepsat.
    /// </summary>
    public sealed class DataStoreMismatchException : DataStoreException
    {
        public const string UserMessage =
            "Databáze a soubor klíčů k sobě nepatří nebo jeden chybí – obnovte je ze zálohy.";

        public DataStoreMismatchException()
            : base(UserMessage)
        {
        }

        public DataStoreMismatchException(Exception innerException)
            : base(UserMessage, innerException)
        {
        }
    }

    /// <summary>Soubor klíčů <c>keys.json</c> nejde přečíst nebo má neplatný obsah.</summary>
    public sealed class KeyStoreCorruptedException : DataStoreException
    {
        public const string UserMessage =
            "Soubor klíčů (keys.json) je poškozený nebo má neznámý formát – obnovte jej ze zálohy.";

        public KeyStoreCorruptedException(string detail)
            : base(UserMessage)
        {
            Detail = detail;
        }

        public KeyStoreCorruptedException(string detail, Exception innerException)
            : base(UserMessage, innerException)
        {
            Detail = detail;
        }

        /// <summary>Technický popis pro protokol (bez citlivých údajů).</summary>
        public string Detail { get; }
    }

    /// <summary>Databázi vytvořila novější verze aplikace, než která je právě spuštěná.</summary>
    public sealed class DatabaseNewerThanAppException : DataStoreException
    {
        public DatabaseNewerThanAppException(int databaseVersion, int applicationVersion)
            : base("Databázi vytvořila novější verze aplikace. Stáhněte si aktuální verzi aplikace a spusťte ji znovu.")
        {
            DatabaseVersion = databaseVersion;
            ApplicationVersion = applicationVersion;
        }

        public int DatabaseVersion { get; }

        public int ApplicationVersion { get; }
    }
}
