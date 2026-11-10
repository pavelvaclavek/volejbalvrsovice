using System.Reflection;

namespace VolejbalVrsovice.App
{
    /// <summary>Neměnné údaje o aplikaci.</summary>
    public static class AppInfo
    {
        /// <summary>Krátký název – titulky dialogů a hlášek.</summary>
        public const string ApplicationName = "Volejbal Vršovice";

        /// <summary>Titulek hlavního okna.</summary>
        public const string ApplicationTitle = "Volejbal Vršovice – evidence příspěvků a čerpání";

        /// <summary>Podtitul v hlavičce hlavního okna.</summary>
        public const string ApplicationSubtitle = "Evidence příspěvků a čerpání";

        /// <summary>Název klubu. Klub se v datech nemodeluje – je to konstanta (zadání, sekce 6.2).</summary>
        public const string ClubName = "TJ Sokol Praha Vršovice";

        private static readonly string CurrentVersion = ReadVersion();

        /// <summary>
        /// Verze aplikace ve tvaru X.Y.Z z <see cref="AssemblyInformationalVersionAttribute"/>.
        /// Při vydání se nastavuje z tagu (<c>-p:Version=</c>), takže verze v aplikaci = verze release.
        /// </summary>
        public static string Version
        {
            get { return CurrentVersion; }
        }

        /// <summary>Text zobrazený v přihlašovacím i hlavním okně.</summary>
        public static string VersionText
        {
            get { return "Verze " + CurrentVersion; }
        }

        private static string ReadVersion()
        {
            var attribute = typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            if (attribute == null || string.IsNullOrWhiteSpace(attribute.InformationalVersion))
            {
                return "0.0.0";
            }

            // Případnou příponu „+hash commitu“ nezobrazovat.
            var version = attribute.InformationalVersion;
            var metadata = version.IndexOf('+');
            return metadata < 0 ? version : version.Substring(0, metadata);
        }
    }
}
