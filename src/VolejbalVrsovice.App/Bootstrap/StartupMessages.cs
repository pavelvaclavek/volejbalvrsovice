using VolejbalVrsovice.Data.Storage;

namespace VolejbalVrsovice.App.Bootstrap
{
    /// <summary>Texty hlášek, kterými může skončit start aplikace.</summary>
    public static class StartupMessages
    {
        public static string DataFolderNotWritable(string dataDirectory)
        {
            return "Do složky s daty nelze zapisovat:\n" + dataDirectory
                   + "\n\nAplikaci zkopírujte do složky, kam máte právo zapisovat.";
        }

        /// <summary>„Aplikaci právě používá {uživatel} na {PC} od {čas}.“</summary>
        public static string AlreadyRunning(LockHolderInfo holder)
        {
            var text = "Aplikaci právě používá ";

            if (holder == null || string.IsNullOrEmpty(holder.UserName))
            {
                text += "jiný uživatel";
            }
            else
            {
                text += holder.UserName;
            }

            if (holder != null && !string.IsNullOrEmpty(holder.MachineName))
            {
                text += " na " + holder.MachineName;
            }

            if (holder != null && holder.StartedAt.HasValue)
            {
                text += " od " + holder.StartedAt.Value.ToLocalTime().ToString(UiCulture.DateTimePattern, UiCulture.Create());
            }

            return text + ".\n\nAplikaci může mít v jednu chvíli otevřenou jen jeden člověk. Zkuste to znovu, až ji zavře.";
        }

        public static string StartFailed(string detail)
        {
            return "Aplikaci se nepodařilo spustit.\n\n" + detail;
        }

        public const string UnexpectedError =
            "V aplikaci došlo k neočekávané chybě.\n\nPodrobnosti jsou zapsány v protokolu ve složce Data\\logs.";
    }
}
