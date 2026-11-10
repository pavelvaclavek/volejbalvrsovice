using System.Globalization;

namespace VolejbalVrsovice.App
{
    /// <summary>
    /// Jednotné formátování v celém UI: česky, měna „1 250,00 Kč“, datum „d. M. yyyy“ –
    /// bez ohledu na regionální nastavení Windows.
    /// </summary>
    public static class UiCulture
    {
        public const string Name = "cs-CZ";

        public const string DatePattern = "d. M. yyyy";

        public const string DateTimePattern = "d. M. yyyy H:mm";

        public static CultureInfo Create()
        {
            // useUserOverride: false – úpravy formátů v Ovládacích panelech se neuplatní.
            var culture = new CultureInfo(Name, false);
            culture.DateTimeFormat.ShortDatePattern = DatePattern;
            return culture;
        }
    }
}
