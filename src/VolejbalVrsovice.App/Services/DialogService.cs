using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using VolejbalVrsovice.App.ViewModels;

namespace VolejbalVrsovice.App.Services
{
    public sealed class DialogService : IDialogService
    {
        private readonly Dictionary<Type, Func<Window>> _windows = new Dictionary<Type, Func<Window>>();

        /// <summary>Přiřadí ViewModelu dialogu jeho okno.</summary>
        public void Register<TViewModel, TWindow>()
            where TViewModel : DialogViewModel
            where TWindow : Window, new()
        {
            _windows[typeof(TViewModel)] = () => new TWindow();
        }

        public void ShowInfo(string message)
        {
            Show(message, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public void ShowWarning(string message)
        {
            Show(message, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        public void ShowError(string message)
        {
            Show(message, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        public bool Confirm(string message)
        {
            return Show(message, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
        }

        public bool ShowDialog(DialogViewModel viewModel)
        {
            if (viewModel == null) throw new ArgumentNullException(nameof(viewModel));

            if (!_windows.TryGetValue(viewModel.GetType(), out var createWindow))
            {
                throw new InvalidOperationException("Pro " + viewModel.GetType().Name + " není zaregistrované okno.");
            }

            var window = createWindow();
            var owner = FindOwner();
            window.DataContext = viewModel;
            window.Owner = owner;
            window.WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
            window.ShowInTaskbar = owner == null;

            var closedByViewModel = false;

            EventHandler<DialogCloseEventArgs> onCloseRequested = (sender, args) =>
            {
                closedByViewModel = true;
                window.DialogResult = args.Result;   // nastavení výsledku modální okno zavře
            };

            CancelEventHandler onClosing = (sender, args) =>
            {
                // Křížek / Alt+F4 / Esc: některé dialogy se takto zavřít nesmí.
                if (!closedByViewModel && !viewModel.CanClose)
                {
                    args.Cancel = true;
                }
            };

            viewModel.CloseRequested += onCloseRequested;
            window.Closing += onClosing;
            try
            {
                return window.ShowDialog() == true;
            }
            finally
            {
                viewModel.CloseRequested -= onCloseRequested;
                window.Closing -= onClosing;
            }
        }

        private static MessageBoxResult Show(
            string message, MessageBoxButton buttons, MessageBoxImage image, MessageBoxResult defaultResult = MessageBoxResult.OK)
        {
            var owner = FindOwner();
            return owner == null
                ? MessageBox.Show(message, AppInfo.ApplicationName, buttons, image, defaultResult)
                : MessageBox.Show(owner, message, AppInfo.ApplicationName, buttons, image, defaultResult);
        }

        /// <summary>Okno, nad kterým se má dialog zobrazit: aktivní, jinak poslední viditelné.</summary>
        private static Window FindOwner()
        {
            var application = Application.Current;
            if (application == null)
            {
                return null;
            }

            var windows = application.Windows.OfType<Window>().Where(window => window.IsVisible).ToList();
            return windows.LastOrDefault(window => window.IsActive) ?? windows.LastOrDefault();
        }
    }
}
