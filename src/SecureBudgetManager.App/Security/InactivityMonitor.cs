using System.Windows.Input;
using System.Windows.Threading;
using SecureBudgetManager.Core.Locking;

namespace SecureBudgetManager.App.Security;

/// <summary>
/// Locks the workspace after a period without keyboard or mouse input inside the application.
/// </summary>
public sealed class InactivityMonitor : IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly TimeProvider _timeProvider;
    private InactivityLockPolicy _policy;
    private DateTimeOffset _lastActivityUtc;
    private bool _running;
    private bool _disposed;

    public InactivityMonitor(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        _policy = new InactivityLockPolicy(InactivityLockPolicy.DefaultTimeout);
        _lastActivityUtc = timeProvider.GetUtcNow();

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(15)
        };
        _timer.Tick += OnTick;
    }

    public event EventHandler? TimeoutElapsed;

    public TimeSpan Timeout => _policy.Timeout;

    public void SetTimeout(TimeSpan timeout)
    {
        _policy = new InactivityLockPolicy(timeout);
        RecordActivity();
    }

    public void Start()
    {
        if (_running)
        {
            return;
        }

        InputManager.Current.PreProcessInput += OnPreProcessInput;
        RecordActivity();
        _timer.Start();
        _running = true;
    }

    public void Stop()
    {
        if (!_running)
        {
            return;
        }

        InputManager.Current.PreProcessInput -= OnPreProcessInput;
        _timer.Stop();
        _running = false;
    }

    public void RecordActivity() => _lastActivityUtc = _timeProvider.GetUtcNow();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _timer.Tick -= OnTick;
        _disposed = true;
    }

    private void OnPreProcessInput(object sender, PreProcessInputEventArgs e)
    {
        if (e.StagingItem.Input is InputEventArgs { RoutedEvent: not null })
        {
            RecordActivity();
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (!_policy.ShouldLock(_lastActivityUtc, _timeProvider.GetUtcNow()))
        {
            return;
        }

        Stop();
        TimeoutElapsed?.Invoke(this, EventArgs.Empty);
    }
}
