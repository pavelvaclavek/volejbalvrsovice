using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VolejbalVrsovice.App.Services;
using VolejbalVrsovice.Core.Entities;
using VolejbalVrsovice.Core.Services;

namespace VolejbalVrsovice.App.ViewModels
{
    /// <summary>Stránka hlavního okna, která si po zobrazení načítá data.</summary>
    public interface ILoadable
    {
        Task LoadAsync();
    }

    /// <summary>
    /// Stránka, která umí být zaneprázdněná operací, během níž se uživatel nesmí odhlásit ani přejít
    /// jinam (typicky několikasekundové zabalování klíče databáze).
    /// </summary>
    public interface IBusyAware
    {
        bool IsBusy { get; }

        event EventHandler IsBusyChanged;
    }

    /// <summary>Položka levého menu.</summary>
    public sealed class MenuItemViewModel
    {
        private readonly Func<object> _createPage;

        public MenuItemViewModel(string title, Func<object> createPage)
        {
            Title = title;
            _createPage = createPage ?? throw new ArgumentNullException(nameof(createPage));
        }

        public string Title { get; }

        public object CreatePage()
        {
            return _createPage();
        }

        // Název položky pro čtečky obrazovky (UI Automation bere jméno prvku z ToString).
        public override string ToString()
        {
            return Title;
        }
    }

    /// <summary>
    /// ViewModel hlavního okna: hlavička (klub, přihlášený uživatel, změna hesla, odhlášení),
    /// levé menu podle role a právě zobrazená stránka.
    /// </summary>
    public sealed class MainViewModel : ObservableObject
    {
        public const string TeamsMenu = "Družstva";
        public const string PlayersMenu = "Hráči";
        public const string ContributionsMenu = "Příspěvky";
        public const string EventsMenu = "Akce";
        public const string UsersMenu = "Uživatelé";

        private readonly IAuthService _auth;
        private readonly IDialogService _dialogs;

        private MenuItemViewModel _selectedMenuItem;
        private object _currentPage;

        public MainViewModel(ISession session, IAuthService auth, IDialogService dialogs, Func<UsersViewModel> createUsersPage)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (createUsersPage == null) throw new ArgumentNullException(nameof(createUsersPage));

            _auth = auth ?? throw new ArgumentNullException(nameof(auth));
            _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));

            var user = session.User ?? throw new InvalidOperationException("Hlavní okno lze otevřít jen po přihlášení.");
            UserName = user.DisplayName;
            UserRoleText = user.IsAdmin ? "Správce" : "Uživatel";
            IsAdmin = user.IsAdmin;

            MenuItems = BuildMenu(user, createUsersPage);

            ChangePasswordCommand = new RelayCommand(ChangePassword, () => !IsPageBusy);
            LogoutCommand = new RelayCommand(RequestLogout, () => !IsPageBusy);

            SelectedMenuItem = MenuItems.FirstOrDefault();
        }

        /// <summary>Uživatel se chce odhlásit (nebo deaktivoval sám sebe).</summary>
        public event EventHandler LogoutRequested;

        public IRelayCommand ChangePasswordCommand { get; }

        public IRelayCommand LogoutCommand { get; }

        public string Title
        {
            get { return AppInfo.ApplicationTitle; }
        }

        /// <summary>Název klubu v hlavičce (klub se nemodeluje, je to konstanta).</summary>
        public string ClubName
        {
            get { return AppInfo.ClubName; }
        }

        public string Subtitle
        {
            get { return AppInfo.ApplicationSubtitle; }
        }

        public string VersionText
        {
            get { return AppInfo.VersionText; }
        }

        public string UserName { get; }

        public string UserRoleText { get; }

        public bool IsAdmin { get; }

        public IReadOnlyList<MenuItemViewModel> MenuItems { get; }

        public MenuItemViewModel SelectedMenuItem
        {
            get { return _selectedMenuItem; }
            set
            {
                if (IsPageBusy)
                {
                    // Menu je po dobu operace zablokované; kdyby změna přišla jinudy, výběr vrátit zpět.
                    OnPropertyChanged();
                    return;
                }

                if (SetProperty(ref _selectedMenuItem, value))
                {
                    CurrentPage = value == null ? null : value.CreatePage();
                }
            }
        }

        public object CurrentPage
        {
            get { return _currentPage; }
            private set
            {
                var previous = _currentPage;
                if (!SetProperty(ref _currentPage, value))
                {
                    return;
                }

                if (previous is IBusyAware previousBusy)
                {
                    previousBusy.IsBusyChanged -= OnPageBusyChanged;
                }

                if (value is IBusyAware busy)
                {
                    busy.IsBusyChanged += OnPageBusyChanged;
                }

                if (value is ILoadable loadable)
                {
                    Load(loadable);
                }
            }
        }

        /// <summary>
        /// Na aktuální stránce běží operace s klíčem databáze. Po tu dobu se nelze odhlásit, měnit heslo
        /// ani přepnout stránku – odhlášení by operaci přerušilo v půli.
        /// </summary>
        public bool IsPageBusy
        {
            get
            {
                var busy = _currentPage as IBusyAware;
                return busy != null && busy.IsBusy;
            }
        }

        public bool IsNavigationEnabled
        {
            get { return !IsPageBusy; }
        }

        // async void záměrně: chyba při načítání stránky tak doputuje do globálního handleru výjimek
        // (protokol + hláška), místo aby se ztratila v nedočkané úloze.
        private static async void Load(ILoadable page)
        {
            await page.LoadAsync();
        }

        private void OnPageBusyChanged(object sender, EventArgs e)
        {
            OnPropertyChanged(nameof(IsPageBusy));
            OnPropertyChanged(nameof(IsNavigationEnabled));
            ChangePasswordCommand.NotifyCanExecuteChanged();
            LogoutCommand.NotifyCanExecuteChanged();
        }

        private IReadOnlyList<MenuItemViewModel> BuildMenu(AppUser user, Func<UsersViewModel> createUsersPage)
        {
            var items = new List<MenuItemViewModel>
            {
                Placeholder(TeamsMenu, "Družstva klubu a jejich pravidelné tréninky."),
                Placeholder(PlayersMenu, "Přehled hráčů družstva se saldem a karta hráče."),
                Placeholder(ContributionsMenu, "Příspěvky hráčů – opisy bankovních a hotovostních příjmů."),
                Placeholder(EventsMenu, "Akce a náklady jejich účastníků."),
            };

            // Správa uživatelů je jen pro roli Správce.
            if (user.IsAdmin)
            {
                items.Add(new MenuItemViewModel(UsersMenu, () =>
                {
                    var page = createUsersPage();
                    page.SelfDeactivated += (sender, args) => RequestLogout();
                    return page;
                }));
            }

            return items;
        }

        private static MenuItemViewModel Placeholder(string title, string description)
        {
            return new MenuItemViewModel(title, () => new PlaceholderPageViewModel(title, description));
        }

        private void ChangePassword()
        {
            if (_dialogs.ShowDialog(new ChangePasswordViewModel(_auth, isForced: false)))
            {
                _dialogs.ShowInfo("Heslo bylo změněno.");
            }
        }

        private void RequestLogout()
        {
            LogoutRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
