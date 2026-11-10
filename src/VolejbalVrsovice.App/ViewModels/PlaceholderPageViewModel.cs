using System;

namespace VolejbalVrsovice.App.ViewModels
{
    /// <summary>
    /// Stránka části aplikace, která se teprve doplní (družstva, hráči, příspěvky, akce).
    /// Menu i rozvržení hlavního okna jsou díky ní hotové dřív než jednotlivé evidence.
    /// </summary>
    public sealed class PlaceholderPageViewModel
    {
        public PlaceholderPageViewModel(string title, string description)
        {
            Title = title ?? throw new ArgumentNullException(nameof(title));
            Description = description ?? throw new ArgumentNullException(nameof(description));
        }

        public string Title { get; }

        public string Description { get; }

        public string Notice
        {
            get { return "Tato část aplikace se připravuje – bude doplněna v další verzi."; }
        }
    }
}
