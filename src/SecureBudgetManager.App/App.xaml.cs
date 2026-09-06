using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SecureBudgetManager.App.Hosting;
using SecureBudgetManager.Core.Abstractions;
using SecureBudgetManager.Core.Security;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace SecureBudgetManager.App;

public partial class App : Application
{
    private IHost? _host;
    private Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, @"Local\SecureBudgetManager.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "Secure Budget Manager is already running.",
                "Secure Budget Manager",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        _host = AppHost.Build(e.Args);
        _host.Start();

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            // Close the local database before the process ends.
            _host.Services.GetRequiredService<IVaultService>().Lock();
            _host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            _host.Dispose();
        }

        _singleInstance?.ReleaseMutex();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs args)
    {
        args.Handled = true;

        if (args.Exception is VaultException vaultException)
        {
            TryLockVault();
            MessageBox.Show(
                vaultException.Message,
                "Secure Budget Manager",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        MessageBox.Show(
            "This information was not saved. Secure Budget Manager ran into a problem with the last action.",
            "Secure Budget Manager",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void TryLockVault()
    {
        try
        {
            _host?.Services.GetRequiredService<IVaultService>().Lock();
        }
        catch (ObjectDisposedException)
        {
            // The host is already gone.
        }
    }
}
