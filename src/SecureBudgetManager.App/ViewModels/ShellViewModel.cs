using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureBudgetManager.App.Security;
using SecureBudgetManager.App.Services;
using SecureBudgetManager.Core.Abstractions;
using SecureBudgetManager.Core.Security;

namespace SecureBudgetManager.App.ViewModels;

/// <summary>
/// Opens the local household database and shows the workspace. Privacy concealment hides figures
/// on screen; it is not database encryption.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject, IDisposable
{
    private readonly IVaultService _vault;
    private readonly IApplicationLock _applicationLock;
    private readonly IBudgetSession _session;
    private readonly UnlockViewModel _privacy;
    private readonly MainViewModel _workspace;
    private readonly InactivityMonitor _inactivity;
    private readonly SessionLockWatcher _sessionWatcher;
    private readonly FullPageCaptureCommand _capture;
    private bool _disposed;

    public ShellViewModel(
        IVaultService vault,
        IApplicationLock applicationLock,
        IBudgetSession session,
        UnlockViewModel privacy,
        MainViewModel workspace,
        InactivityMonitor inactivity,
        SessionLockWatcher sessionWatcher,
        FullPageCaptureCommand capture)
    {
        _vault = vault;
        _applicationLock = applicationLock;
        _session = session;
        _privacy = privacy;
        _workspace = workspace;
        _inactivity = inactivity;
        _sessionWatcher = sessionWatcher;
        _capture = capture;
        CaptureFullPageCommand = new RelayCommand(CaptureFullPage, () => _capture.CanExecute());

        _privacy.Unlocked += OnRevealed;
        _workspace.LockRequested += OnManualPrivacyRequested;
        _inactivity.TimeoutElapsed += OnInactivityElapsed;
        _sessionWatcher.SessionSecured += OnSessionSecured;

        currentScreen = ChooseInitialScreen();
        _sessionWatcher.Start();
    }

    [ObservableProperty]
    private ObservableObject currentScreen;

    public MainViewModel Workspace => _workspace;

    public IRelayCommand CaptureFullPageCommand { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _privacy.Unlocked -= OnRevealed;
        _workspace.LockRequested -= OnManualPrivacyRequested;
        _inactivity.TimeoutElapsed -= OnInactivityElapsed;
        _sessionWatcher.SessionSecured -= OnSessionSecured;

        _inactivity.Dispose();
        _sessionWatcher.Dispose();
        _disposed = true;
    }

    private ObservableObject ChooseInitialScreen()
    {
        try
        {
            var opened = _vault.RevealAsync().GetAwaiter().GetResult();
            if (opened.Succeeded && TryOpenWorkspace())
            {
                return _workspace;
            }

            _session.Clear();
            _privacy.Reset(opened.Message ?? _session.LastError ?? "The local household database could not be opened.");
            return _privacy;
        }
        catch (Exception exception) when (exception is VaultException or InvalidOperationException or IOException)
        {
            _session.Clear();
            _privacy.Reset(exception.Message);
            return _privacy;
        }
    }

    private void OnRevealed(object? sender, EventArgs e) => EnterWorkspace();

    private void EnterWorkspace()
    {
        if (!TryOpenWorkspace())
        {
            _privacy.Reset(_session.LastError ?? "The local household database could not be opened.");
            CurrentScreen = _privacy;
            return;
        }

        CurrentScreen = _workspace;
    }

    private bool TryOpenWorkspace()
    {
        if (!_session.Open())
        {
            return false;
        }

        _workspace.ResetNavigation();
        _inactivity.Start();
        return true;
    }

    private void OnInactivityElapsed(object? sender, EventArgs e) =>
        ConcealWorkspace("inactivity", $"Figures were hidden after {DescribeTimeout(_inactivity.Timeout)} without activity. This is not database encryption.");

    private void OnSessionSecured(object? sender, string reason) =>
        ConcealWorkspace(reason, $"Figures were hidden because {reason}. This is not database encryption. Restarting still opens the Dashboard.");

    private void ConcealWorkspace(string auditReason, string userMessage)
    {
        if (_vault.Status != VaultStatus.Unlocked)
        {
            _session.Clear();
            return;
        }

        _inactivity.Stop();
        _workspace.ResetNavigation();
        _session.Clear();
        _applicationLock.RequestLock(auditReason);
        _privacy.Reset(userMessage);
        CurrentScreen = _privacy;
    }

    private void OnManualPrivacyRequested(object? sender, EventArgs e) =>
        ConcealWorkspace(
            "the Privacy button",
            "Figures are hidden on this screen. This is not database encryption. Anyone with access to the Windows files can still read the local database. Restarting the programme opens the Dashboard normally.");

    private void CaptureFullPage()
    {
        var message = _capture.Execute(_workspace.CurrentViewModel.Title);
        if (!string.IsNullOrWhiteSpace(message))
        {
            _workspace.StatusMessage = message;
        }
    }

    partial void OnCurrentScreenChanged(ObservableObject value)
    {
        _ = value;
        CaptureFullPageCommand.NotifyCanExecuteChanged();
    }

    private static string DescribeTimeout(TimeSpan timeout) =>
        timeout.TotalMinutes >= 1
            ? $"{Math.Round(timeout.TotalMinutes)} minutes"
            : $"{Math.Round(timeout.TotalSeconds)} seconds";
}
