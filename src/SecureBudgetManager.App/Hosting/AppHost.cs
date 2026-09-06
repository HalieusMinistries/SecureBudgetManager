using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SecureBudgetManager.App.Security;
using SecureBudgetManager.App.Services;
using SecureBudgetManager.App.ViewModels;
using SecureBudgetManager.Infrastructure;

namespace SecureBudgetManager.App.Hosting;

internal static class AppHost
{
    public static IHost Build(string[] args)
    {
        var settings = new HostApplicationBuilderSettings
        {
            Args = args,
            ApplicationName = "SecureBudgetManager",
            EnvironmentName = "Local",
            ContentRootPath = AppContext.BaseDirectory
        };

        var builder = Host.CreateEmptyApplicationBuilder(settings);

        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Information);
        builder.Logging.AddDebug();

        builder.Services.AddInfrastructure();

        builder.Services.AddSingleton<InactivityMonitor>();
        builder.Services.AddSingleton<SessionLockWatcher>();
        builder.Services.AddSingleton<IUserDialog, WpfUserDialog>();
        builder.Services.AddSingleton<IWorkspaceNotice, WorkspaceNoticeService>();
        builder.Services.AddSingleton<IBudgetSession, BudgetSession>();
        builder.Services.AddSingleton<IPendingHouseholdImport, PendingHouseholdImportService>();
        builder.Services.AddSingleton<CaptureVisualLocator>();
        builder.Services.AddSingleton<ICaptureVisualLocator>(services => services.GetRequiredService<CaptureVisualLocator>());
        builder.Services.AddSingleton<IUiPreferenceStore, FileUiPreferenceStore>();
        builder.Services.AddSingleton<ICaptureDialogs, WpfCaptureDialogs>();
        builder.Services.AddSingleton<IFolderLauncher, WindowsFolderLauncher>();
        builder.Services.AddSingleton<IFullPageCaptureService, WpfFullPageCaptureService>();
        builder.Services.AddSingleton<FullPageCaptureCommand>();

        builder.Services.AddSingleton<DashboardViewModel>();
        builder.Services.AddSingleton<HouseholdViewModel>();
        builder.Services.AddSingleton<IncomeViewModel>();
        builder.Services.AddSingleton<ExpensesViewModel>();
        builder.Services.AddSingleton<PayrollViewModel>();
        builder.Services.AddSingleton<CashFlowViewModel>();
        builder.Services.AddSingleton<PlanningViewModel>();
        builder.Services.AddSingleton<DebtViewModel>();
        builder.Services.AddSingleton<SavingsViewModel>();
        builder.Services.AddSingleton<ActualsViewModel>();
        builder.Services.AddSingleton<ThisWeekViewModel>();
        builder.Services.AddSingleton<AllocationsViewModel>();
        builder.Services.AddSingleton<GroceryPlanViewModel>();
        builder.Services.AddSingleton<LocalGuidanceViewModel>();
        builder.Services.AddSingleton<AllocationRulesViewModel>();
        builder.Services.AddSingleton<ProductsViewModel>();
        builder.Services.AddSingleton<InternationalTransfersViewModel>();
        builder.Services.AddSingleton<ForeignAccountsViewModel>();
        builder.Services.AddSingleton<SettingsViewModel>();
        builder.Services.AddSingleton<HelpViewModel>();
        builder.Services.AddSingleton<SetupViewModel>();
        builder.Services.AddSingleton<UnlockViewModel>();
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<ShellViewModel>();
        builder.Services.AddSingleton<MainWindow>();

        return builder.Build();
    }
}
