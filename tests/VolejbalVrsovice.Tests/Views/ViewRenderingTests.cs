using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Serilog.Core;
using VolejbalVrsovice.App.ViewModels;
using VolejbalVrsovice.App.Views;
using VolejbalVrsovice.Core.Entities;
using VolejbalVrsovice.Core.Services;
using VolejbalVrsovice.Tests.TestSupport;
using Xunit;

namespace VolejbalVrsovice.Tests.Views
{
    /// <summary>
    /// Každé okno se skutečně vytvoří, naváže na ViewModel a vykreslí (mimo obrazovku). Test tak zachytí
    /// chyby, které se jinak projeví až za běhu: nenačtený XAML, chybějící styl, špatně pojmenovanou vazbu.
    /// <para>
    /// Nastavením proměnné prostředí <c>VV_RENDER_DIR</c> se vykreslená okna uloží jako PNG –
    /// hodí se pro rychlou kontrolu vzhledu bez spouštění aplikace.
    /// </para>
    /// </summary>
    public class ViewRenderingTests
    {
        private static readonly string OutputDirectory = Environment.GetEnvironmentVariable("VV_RENDER_DIR");

        private readonly FakeAuthService _auth = new FakeAuthService();
        private readonly FakeDialogService _dialogs = new FakeDialogService();

        [Fact]
        public async Task Login_window_renders_ready_for_input()
        {
            var viewModel = new LoginViewModel(_auth, _dialogs, Logger.None);
            await viewModel.InitializeAsync();

            Check(ViewRenderer.Render("login", () => new LoginWindow { DataContext = viewModel }));
        }

        [Fact]
        public async Task Login_window_renders_a_failed_attempt()
        {
            var viewModel = new LoginViewModel(_auth, _dialogs, Logger.None);
            await viewModel.InitializeAsync();
            viewModel.Email = "admin@admin.cz";
            viewModel.Password = "spatne-heslo";
            await viewModel.LoginCommand.ExecuteAsync(null);
            viewModel.Password = "dalsi-pokus";

            Check(ViewRenderer.Render("login-chyba", () => new LoginWindow { DataContext = viewModel }));
        }

        [Fact]
        public async Task Login_window_renders_the_first_run_preparation()
        {
            var gate = new TaskCompletionSource<bool>();
            _auth.InitializeGate = gate.Task;
            var viewModel = new LoginViewModel(_auth, _dialogs, Logger.None);

            // Stejně jako v aplikaci se příprava spouští z UI vlákna – pokračování po await se tak vracejí
            // na UI vlákno a smějí měnit stav tlačítek navázaného okna.
            var initialization = WpfTestHost.Run(() => viewModel.InitializeAsync());
            await Task.Delay(500);   // po 200 ms se objeví hláška o prvním spuštění
            Assert.Equal("První spuštění – připravuji databázi…", viewModel.StatusMessage);

            Check(ViewRenderer.Render("login-prvni-spusteni", () => new LoginWindow { DataContext = viewModel }));

            gate.SetResult(true);
            await initialization;
            Assert.True(viewModel.IsReady);
        }

        [Fact]
        public async Task Forced_password_change_renders_with_field_errors()
        {
            var viewModel = new ChangePasswordViewModel(_auth, isForced: true) { NewPassword = "admin", ConfirmPassword = "neco-jineho" };
            await viewModel.SaveCommand.ExecuteAsync(null);

            Check(ViewRenderer.Render("zmena-hesla-vynucena", () => new ChangePasswordDialog { DataContext = viewModel }));
        }

        [Fact]
        public async Task Voluntary_password_change_renders_with_a_refusal()
        {
            _auth.OwnChangeResult = PasswordChangeResult.Failure("Současné heslo není správné.");
            var viewModel = new ChangePasswordViewModel(_auth, isForced: false)
            {
                CurrentPassword = "spatne",
                NewPassword = "Moje-nove-heslo-1",
                ConfirmPassword = "Moje-nove-heslo-1",
            };
            await viewModel.SaveCommand.ExecuteAsync(null);

            Check(ViewRenderer.Render("zmena-hesla", () => new ChangePasswordDialog { DataContext = viewModel }));
        }

        [Fact]
        public void Recovery_code_dialog_renders()
        {
            var viewModel = new RecoveryCodeViewModel("K7QM-2XHP-9RTD-WNB4-FJ6C-VYA3", new FakeClipboard(), new FakePrinter());
            viewModel.CopyCommand.Execute(null);

            Check(ViewRenderer.Render("zachranny-kod", () => new RecoveryCodeDialog { DataContext = viewModel }));
        }

        [Fact]
        public async Task Forgot_password_dialog_renders_with_a_refusal()
        {
            _auth.RecoverResult = OperationResult.Failure("Záchranný kód není platný.");
            var viewModel = new ForgotPasswordViewModel(_auth, _dialogs)
            {
                RecoveryCode = "K7QM-2XHP-9RTD-WNB4-FJ6C-VYA3",
                AdminEmail = "admin@admin.cz",
                NewPassword = "Obnovene-heslo-3",
                ConfirmPassword = "Obnovene-heslo-3",
            };
            await viewModel.SubmitCommand.ExecuteAsync(null);

            Check(ViewRenderer.Render("zapomenute-heslo", () => new ForgotPasswordDialog { DataContext = viewModel }));
        }

        [Fact]
        public void Main_window_renders_for_an_admin_with_the_users_page()
        {
            var session = new FakeSession { User = TestUsers.Admin() };
            var users = new FakeUserService();
            users.Users.Add(new AppUser { Id = 1, Email = "admin@admin.cz", DisplayName = "Správce", Role = UserRole.Admin, IsActive = true, LastLoginAt = new DateTime(2026, 11, 10, 8, 15, 0, DateTimeKind.Utc) });
            users.Users.Add(new AppUser { Id = 2, Email = "trener@example.cz", DisplayName = "Petr Svoboda", Role = UserRole.User, IsActive = true, MustChangePassword = true });
            users.Users.Add(new AppUser { Id = 3, Email = "byvala@example.cz", DisplayName = "Jana Nováková", Role = UserRole.User, IsActive = false, LastLoginAt = new DateTime(2026, 6, 1, 17, 40, 0, DateTimeKind.Utc) });

            var viewModel = new MainViewModel(session, _auth, _dialogs,
                () => new UsersViewModel(users, session, _dialogs, new FakeClipboard(), new FakePrinter()));
            var page = Assert.IsType<UsersViewModel>(viewModel.CurrentPage);
            page.SelectedUser = page.Users.Single(row => row.Email == "trener@example.cz");

            Check(ViewRenderer.Render("hlavni-okno-spravce", () => new MainWindow { DataContext = viewModel }));
        }

        [Fact]
        public void Main_window_renders_for_an_ordinary_user()
        {
            var session = new FakeSession { User = TestUsers.Ordinary() };
            var viewModel = new MainViewModel(session, _auth, _dialogs,
                () => new UsersViewModel(new FakeUserService(), session, _dialogs, new FakeClipboard(), new FakePrinter()));

            Check(ViewRenderer.Render("hlavni-okno-uzivatel", () => new MainWindow { DataContext = viewModel }));
        }

        [Fact]
        public async Task New_user_dialog_renders_with_field_errors()
        {
            var viewModel = new AddUserViewModel(new FakeUserService())
            {
                Email = "bez-zavinace",
                DisplayName = string.Empty,
                TemporaryPassword = "kratke",
                ConfirmPassword = "jine",
            };
            await viewModel.SaveCommand.ExecuteAsync(null);

            Check(ViewRenderer.Render("novy-uzivatel", () => new AddUserDialog { DataContext = viewModel }));
        }

        [Fact]
        public void Reset_password_dialog_renders()
        {
            var viewModel = new ResetPasswordViewModel(new FakeUserService(), TestUsers.Ordinary());

            Check(ViewRenderer.Render("reset-hesla", () => new ResetPasswordDialog { DataContext = viewModel }));
        }

        private static void Check(RenderedView view)
        {
            if (!string.IsNullOrEmpty(OutputDirectory))
            {
                view.SaveTo(OutputDirectory);
            }

            Assert.True(view.Image.PixelWidth >= 300 && view.Image.PixelHeight >= 150,
                view.Name + ": nečekaná velikost " + view.Image.PixelWidth + "×" + view.Image.PixelHeight);
            Assert.True(view.CountDistinctColors() > 3, view.Name + ": okno se vykreslilo prázdné");
            Assert.True(view.BindingErrors.Count == 0,
                view.Name + ": chyby vazeb:" + Environment.NewLine + string.Join(Environment.NewLine, view.BindingErrors));
        }
    }
}
