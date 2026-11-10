using CommunityToolkit.Mvvm.ComponentModel;

namespace VolejbalVrsovice.App.ViewModels
{
    /// <summary>ViewModel hlavního okna.</summary>
    public sealed class MainViewModel : ObservableObject
    {
        public string Title
        {
            get { return AppInfo.ApplicationTitle; }
        }
    }
}
