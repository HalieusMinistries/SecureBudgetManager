using Microsoft.Win32;

namespace SecureBudgetManager.App.Security;

/// <summary>
/// Raises an event when Windows locks the session, the user switches away, or the machine suspends,
/// so the vault can be closed at the same moment the desktop is secured.
/// </summary>
public sealed class SessionLockWatcher : IDisposable
{
    private bool _subscribed;
    private bool _disposed;

    public event EventHandler<string>? SessionSecured;

    public void Start()
    {
        if (_subscribed)
        {
            return;
        }

        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionEnding += OnSessionEnding;
        _subscribed = true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_subscribed)
        {
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.SessionEnding -= OnSessionEnding;
            _subscribed = false;
        }

        _disposed = true;
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        var reason = e.Reason switch
        {
            SessionSwitchReason.SessionLock => "the Windows session was locked",
            SessionSwitchReason.SessionLogoff => "the Windows user signed out",
            SessionSwitchReason.ConsoleDisconnect => "the Windows session was disconnected",
            SessionSwitchReason.RemoteDisconnect => "the remote session was disconnected",
            _ => null
        };

        if (reason is not null)
        {
            SessionSecured?.Invoke(this, reason);
        }
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend)
        {
            SessionSecured?.Invoke(this, "the computer went to sleep");
        }
    }

    private void OnSessionEnding(object sender, SessionEndingEventArgs e) =>
        SessionSecured?.Invoke(this, "Windows is shutting down");
}
