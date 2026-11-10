using System;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using VolejbalVrsovice.App.Bootstrap;
using VolejbalVrsovice.App.ViewModels;
using VolejbalVrsovice.App.Views;
using VolejbalVrsovice.Core.Errors;
using VolejbalVrsovice.Core.Services;
using VolejbalVrsovice.Data.Db;
using VolejbalVrsovice.Data.Security;
using VolejbalVrsovice.Data.Storage;

namespace VolejbalVrsovice.App
{
    public partial class App : Application
    {
        private InstanceLock _instanceLock;
        private ServiceProvider _services;

        protected override void OnStartup(StartupEventArgs e)
        {
            // Kontrola .NET Frameworku je úplně první krok (zadání, sekce 3). Zbytek startu je schválně
            // v samostatných metodách, aby se knihovny aplikace začaly načítat až po této kontrole.
            if (!FrameworkCheck.EnsureSupported())
            {
                Shutdown(ExitCodes.FrameworkMissing);
                return;
            }

            base.OnStartup(e);
            StartApplicationSafely();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void StartApplicationSafely()
        {
            try
            {
                StartApplication();
            }
            catch (Exception ex)
            {
                // Bez hlavního okna by proces zůstal viset – proto vždy ukončit.
                Log.Fatal(ex, "Start aplikace selhal");
                ShowError(StartupMessages.StartFailed(ex.Message));
                Shutdown(ExitCodes.UnexpectedError);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_services != null)
            {
                _services.Dispose();
                _services = null;
            }

            Log.Information("Aplikace ukončena (kód {ExitCode})", e.ApplicationExitCode);
            Log.CloseAndFlush();

            // Zámek uvolnit až nakonec – protokol leží ve složce Data, kterou zámek chrání.
            if (_instanceLock != null)
            {
                _instanceLock.Dispose();
                _instanceLock = null;
            }

            base.OnExit(e);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void StartApplication()
        {
            RegisterGlobalExceptionHandlers();
            ApplyCzechCulture();

            var paths = new AppPaths(AppContext.BaseDirectory);

            // 2) Lze do složky Data zapisovat?
            if (!DataFolder.TryEnsureWritable(paths, out _))
            {
                ShowError(StartupMessages.DataFolderNotWritable(paths.DataDirectory));
                Shutdown(ExitCodes.DataFolderNotWritable);
                return;
            }

            // 3) Zámek proti souběžnému spuštění (drží se po celou dobu běhu).
            var lockResult = InstanceLock.TryAcquire(paths);
            if (!lockResult.Acquired)
            {
                MessageBox.Show(StartupMessages.AlreadyRunning(lockResult.Holder), AppInfo.ApplicationName,
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                Shutdown(ExitCodes.AlreadyRunning);
                return;
            }

            _instanceLock = lockResult.Lock;

            // Protokol až pod zámkem – do souboru tak vždy zapisuje jen jedna instance.
            Log.Logger = LoggingSetup.CreateLogger(paths.LogsDirectory);
            Log.Information("Aplikace spuštěna, verze {Version}", GetVersion());

            _services = ConfigureServices(paths);

            // 4) Úložiště dat – rozhodovací tabulka při startu (zadání, sekce 5a).
            var dataStore = _services.GetRequiredService<DataStore>();
            switch (dataStore.Inspect())
            {
                case DataStoreState.Inconsistent:
                    // Existuje jen databáze, nebo jen soubor klíčů: nic nevytvářet ani nepřepisovat.
                    Log.Error("Úložiště dat je neúplné – chybí databáze nebo soubor klíčů");
                    ShowError(DataStoreMismatchException.UserMessage);
                    Shutdown(ExitCodes.DataStoreInconsistent);
                    return;

                case DataStoreState.FirstRun:
                    dataStore.CreateNew();
                    break;
            }

            var window = _services.GetRequiredService<MainWindow>();
            window.Closed += (sender, args) => Shutdown(ExitCodes.Ok);
            MainWindow = window;
            window.Show();
        }

        private static ServiceProvider ConfigureServices(AppPaths paths)
        {
            var services = new ServiceCollection();

            services.AddSingleton(paths);
            services.AddSingleton(Log.Logger);
            services.AddSingleton<IClock, SystemClock>();

            // Úložiště dat: šifrovaná databáze + soubor klíčů.
            services.AddSingleton(KdfSettings.Default);
            services.AddSingleton(new KeyStore(paths.KeysFile));
            services.AddSingleton(new SqliteConnectionFactory(paths.DatabaseFile));
            // Instanci vytvořit ručně: kontejner by jinak zvolil konstruktor s (prázdným) seznamem migrací.
            services.AddSingleton(new Migrator());
            services.AddSingleton<BackupService>();
            services.AddSingleton<DataStore>();

            services.AddSingleton<MainViewModel>();
            services.AddSingleton<MainWindow>();

            return services.BuildServiceProvider();
        }

        private static string GetVersion()
        {
            var attribute = typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            return attribute == null ? "?" : attribute.InformationalVersion;
        }

        private static void ApplyCzechCulture()
        {
            var culture = UiCulture.Create();

            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;

            // Bez tohoto by XAML vazby formátovaly podle en-US.
            FrameworkElement.LanguageProperty.OverrideMetadata(
                typeof(FrameworkElement),
                new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(culture.IetfLanguageTag)));
        }

        private void RegisterGlobalExceptionHandlers()
        {
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Log.Error(e.Exception, "Neošetřená výjimka v UI vlákně");
            ShowError(StartupMessages.UnexpectedError);
            e.Handled = true;
        }

        private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            // Výjimka mimo UI vlákno – proces skončí, stihneme jen zapsat protokol a říct to uživateli.
            Log.Fatal(e.ExceptionObject as Exception, "Neošetřená výjimka mimo UI vlákno (proces končí: {IsTerminating})", e.IsTerminating);
            Log.CloseAndFlush();
            ShowError(StartupMessages.UnexpectedError);
        }

        private static void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            Log.Error(e.Exception, "Nezpracovaná výjimka v úloze na pozadí");
            e.SetObserved();
        }

        private static void ShowError(string message)
        {
            MessageBox.Show(message, AppInfo.ApplicationName, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
