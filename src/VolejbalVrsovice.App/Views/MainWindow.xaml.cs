using System.Windows;
using VolejbalVrsovice.App.ViewModels;

namespace VolejbalVrsovice.App.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
