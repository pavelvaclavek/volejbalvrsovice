using System;
using System.Linq;
using System.Threading.Tasks;
using VolejbalVrsovice.App;
using VolejbalVrsovice.App.ViewModels;
using VolejbalVrsovice.Core.Entities;
using VolejbalVrsovice.Tests.TestSupport;
using Xunit;

namespace VolejbalVrsovice.Tests.ViewModels
{
    public class MainViewModelTests
    {
        private readonly FakeAuthService _auth = new FakeAuthService();
        private readonly FakeDialogService _dialogs = new FakeDialogService();

        private MainViewModel Create(AppUser user)
        {
            var session = new FakeSession { User = user };
            return new MainViewModel(session, _auth, _dialogs,
                () => new UsersViewModel(new FakeUserService(), session, _dialogs, new FakeClipboard(), new FakePrinter()));
        }

        [Fact]
        public void Admin_sees_all_sections_including_users()
        {
            var viewModel = Create(TestUsers.Admin());

            Assert.True(viewModel.IsAdmin);
            Assert.Equal(new[] { "Družstva", "Hráči", "Příspěvky", "Akce", "Uživatelé" }, viewModel.MenuItems.Select(item => item.Title));
            Assert.Equal("Správce", viewModel.UserName);
            Assert.Equal("Správce", viewModel.UserRoleText);
        }

        [Fact]
        public void Ordinary_user_does_not_see_the_users_menu()
        {
            var viewModel = Create(TestUsers.Ordinary());

            Assert.False(viewModel.IsAdmin);
            Assert.Equal(new[] { "Družstva", "Hráči", "Příspěvky", "Akce" }, viewModel.MenuItems.Select(item => item.Title));
            Assert.DoesNotContain(viewModel.MenuItems, item => item.Title == "Uživatelé");
            Assert.Equal("Petr Svoboda", viewModel.UserName);
            Assert.Equal("Uživatel", viewModel.UserRoleText);
        }

        [Fact]
        public void First_section_is_open_after_login()
        {
            var viewModel = Create(TestUsers.Ordinary());

            Assert.Equal("Družstva", viewModel.SelectedMenuItem.Title);
            var page = Assert.IsType<PlaceholderPageViewModel>(viewModel.CurrentPage);
            Assert.Equal("Družstva", page.Title);
        }

        [Theory]
        [InlineData("Družstva")]
        [InlineData("Hráči")]
        [InlineData("Příspěvky")]
        [InlineData("Akce")]
        public void Sections_that_are_not_built_yet_say_so(string section)
        {
            var viewModel = Create(TestUsers.Ordinary());

            viewModel.SelectedMenuItem = viewModel.MenuItems.Single(item => item.Title == section);

            var page = Assert.IsType<PlaceholderPageViewModel>(viewModel.CurrentPage);
            Assert.Equal(section, page.Title);
            Assert.False(string.IsNullOrWhiteSpace(page.Description));
            Assert.Contains("připravuje", page.Notice);
        }

        [Fact]
        public void Selecting_the_users_menu_opens_the_users_page()
        {
            var viewModel = Create(TestUsers.Admin());

            viewModel.SelectedMenuItem = viewModel.MenuItems.Single(item => item.Title == "Uživatelé");

            Assert.IsType<UsersViewModel>(viewModel.CurrentPage);
        }

        [Fact]
        public void Header_shows_the_club_name_and_the_application_version()
        {
            var viewModel = Create(TestUsers.Ordinary());

            Assert.Equal("TJ Sokol Praha Vršovice", viewModel.ClubName);
            Assert.Equal("Evidence příspěvků a čerpání", viewModel.Subtitle);
            Assert.Equal("Verze " + AppInfo.Version, viewModel.VersionText);
            Assert.Equal("Volejbal Vršovice – evidence příspěvků a čerpání", viewModel.Title);
        }

        [Fact]
        public void Logout_is_requested_from_the_header()
        {
            var viewModel = Create(TestUsers.Ordinary());
            var requested = false;
            viewModel.LogoutRequested += (sender, args) => requested = true;

            viewModel.LogoutCommand.Execute(null);

            Assert.True(requested);
        }

        [Fact]
        public void Every_user_can_change_the_own_password_from_the_header()
        {
            var viewModel = Create(TestUsers.Ordinary());

            viewModel.ChangePasswordCommand.Execute(null);

            var dialog = Assert.IsType<ChangePasswordViewModel>(Assert.Single(_dialogs.Dialogs));
            Assert.False(dialog.IsForced);
            Assert.Equal("Heslo bylo změněno.", Assert.Single(_dialogs.Infos));
        }

        /// <summary>
        /// Dokud na stránce běží operace s klíčem databáze (trvá několik sekund), nejde se odhlásit,
        /// měnit heslo ani přepnout stránku – odhlášení by operaci přerušilo v půli.
        /// </summary>
        [Fact]
        public async Task Logout_and_navigation_are_blocked_while_the_page_works_with_the_database_key()
        {
            var gate = new TaskCompletionSource<bool>();
            var users = new FakeUserService { OperationGate = gate.Task };
            users.Users.Add(TestUsers.Admin());
            var session = new FakeSession { User = TestUsers.Admin() };
            var viewModel = new MainViewModel(session, _auth, _dialogs,
                () => new UsersViewModel(users, session, _dialogs, new FakeClipboard(), new FakePrinter()));
            viewModel.SelectedMenuItem = viewModel.MenuItems.Single(item => item.Title == "Uživatelé");
            var page = Assert.IsType<UsersViewModel>(viewModel.CurrentPage);
            var selectedBefore = viewModel.SelectedMenuItem;

            var generating = page.NewRecoveryCodeCommand.ExecuteAsync(null);

            Assert.True(viewModel.IsPageBusy);
            Assert.False(viewModel.IsNavigationEnabled);
            Assert.False(viewModel.LogoutCommand.CanExecute(null));
            Assert.False(viewModel.ChangePasswordCommand.CanExecute(null));

            viewModel.SelectedMenuItem = viewModel.MenuItems[0];   // pokus o přepnutí stránky se ignoruje
            Assert.Same(selectedBefore, viewModel.SelectedMenuItem);
            Assert.Same(page, viewModel.CurrentPage);

            gate.SetResult(true);
            await generating;

            Assert.False(viewModel.IsPageBusy);
            Assert.True(viewModel.IsNavigationEnabled);
            Assert.True(viewModel.LogoutCommand.CanExecute(null));
            Assert.True(viewModel.ChangePasswordCommand.CanExecute(null));
        }

        [Fact]
        public void Main_window_cannot_exist_without_a_logged_in_user()
        {
            Assert.Throws<InvalidOperationException>(() => Create(null));
        }
    }
}
