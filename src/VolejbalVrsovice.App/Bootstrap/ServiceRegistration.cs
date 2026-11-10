using System;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using VolejbalVrsovice.App.Services;
using VolejbalVrsovice.App.ViewModels;
using VolejbalVrsovice.App.Views;
using VolejbalVrsovice.Core.Repositories;
using VolejbalVrsovice.Core.Security;
using VolejbalVrsovice.Core.Services;
using VolejbalVrsovice.Data.Db;
using VolejbalVrsovice.Data.Repositories;
using VolejbalVrsovice.Data.Security;
using VolejbalVrsovice.Data.Storage;

namespace VolejbalVrsovice.App.Bootstrap
{
    /// <summary>Složení aplikace (DI) – jediné místo, kde se rozhoduje, která implementace se použije.</summary>
    public static class ServiceRegistration
    {
        public static ServiceProvider BuildServiceProvider(AppPaths paths, ILogger logger)
        {
            var services = new ServiceCollection();

            services.AddSingleton(paths);
            services.AddSingleton(logger);
            services.AddSingleton<IClock, SystemClock>();

            AddDataStore(services, paths);
            AddSecurity(services);
            AddRepositories(services);
            AddUserInterface(services);

            return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        }

        // Úložiště dat: šifrovaná databáze + soubor klíčů.
        private static void AddDataStore(IServiceCollection services, AppPaths paths)
        {
            services.AddSingleton(KdfSettings.Default);
            services.AddSingleton(new KeyStore(paths.KeysFile));
            services.AddSingleton(new SqliteConnectionFactory(paths.DatabaseFile));

            // Instanci vytvořit ručně: kontejner by jinak zvolil konstruktor s (prázdným) seznamem migrací.
            services.AddSingleton(new Migrator());
            services.AddSingleton<BackupService>();
            services.AddSingleton<DataStore>();
        }

        // Relace drží přihlášeného uživatele i klíč databáze; repozitáře si klíč berou od ní.
        private static void AddSecurity(IServiceCollection services)
        {
            services.AddSingleton<AppSession>();
            services.AddSingleton<ISession>(provider => provider.GetRequiredService<AppSession>());
            services.AddSingleton<ICurrentUser>(provider => provider.GetRequiredService<AppSession>());
            services.AddSingleton<IDatabaseKeySource>(provider => provider.GetRequiredService<AppSession>());
            services.AddSingleton<IDatabase, Database>();

            services.AddSingleton<LoginThrottle>();
            services.AddSingleton<IAuthService, AuthService>();
            services.AddSingleton<IUserService, UserService>();
        }

        private static void AddRepositories(IServiceCollection services)
        {
            services.AddSingleton<IAppUserRepository, AppUserRepository>();
            services.AddSingleton<ITeamRepository, TeamRepository>();
            services.AddSingleton<ITrainingScheduleRepository, TrainingScheduleRepository>();
            services.AddSingleton<IPlayerRepository, PlayerRepository>();
            services.AddSingleton<ITeamMembershipRepository, TeamMembershipRepository>();
            services.AddSingleton<IContributionRepository, ContributionRepository>();
            services.AddSingleton<IEventRepository, EventRepository>();
            services.AddSingleton<IEventParticipantRepository, EventParticipantRepository>();
            services.AddSingleton<IAuditLogRepository, AuditLogRepository>();
            services.AddSingleton<ICountryRepository, CountryRepository>();
            services.AddSingleton<IBalanceQueries, BalanceQueries>();
        }

        private static void AddUserInterface(IServiceCollection services)
        {
            var dialogs = new DialogService();
            dialogs.Register<ChangePasswordViewModel, ChangePasswordDialog>();
            dialogs.Register<RecoveryCodeViewModel, RecoveryCodeDialog>();
            dialogs.Register<ForgotPasswordViewModel, ForgotPasswordDialog>();
            dialogs.Register<AddUserViewModel, AddUserDialog>();
            dialogs.Register<ResetPasswordViewModel, ResetPasswordDialog>();

            services.AddSingleton<IDialogService>(dialogs);
            services.AddSingleton<IClipboardService, ClipboardService>();
            services.AddSingleton<IRecoveryCodePrinter, RecoveryCodePrinter>();
            services.AddSingleton<AppController>();

            // ViewModely oken a stránek vznikají vždy nové (po každém přihlášení čistý stav).
            services.AddTransient<LoginViewModel>();
            services.AddTransient<MainViewModel>();
            services.AddTransient<UsersViewModel>();
            services.AddSingleton<Func<UsersViewModel>>(provider => () => provider.GetRequiredService<UsersViewModel>());
        }
    }
}
