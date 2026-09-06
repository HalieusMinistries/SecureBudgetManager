namespace SecureBudgetManager.App.Services;

public sealed class WorkspaceNoticeService : IWorkspaceNotice
{
    public static IWorkspaceNotice Null { get; } = new NullWorkspaceNotice();

    private static readonly AsyncLocal<IWorkspaceNotice?> CurrentNotice = new();

    public static IWorkspaceNotice Current
    {
        get => CurrentNotice.Value ?? Null;
        set => CurrentNotice.Value = value;
    }

    private readonly TimeSpan _successClearAfter = TimeSpan.FromSeconds(6);
    private CancellationTokenSource? _clear;

    public event EventHandler<WorkspaceNoticeEventArgs>? Raised;

    public string? Message { get; private set; }

    public WorkspaceNoticeKind Kind { get; private set; } = WorkspaceNoticeKind.Info;

    public void ShowSaved(string message) => Show(message, WorkspaceNoticeKind.Success, _successClearAfter);

    public void ShowError(string message) => Show(message, WorkspaceNoticeKind.Error);

    public void Show(string message, WorkspaceNoticeKind kind, TimeSpan? autoClear = null)
    {
        CancelPendingClear();
        Message = message;
        Kind = kind;
        Raised?.Invoke(this, new WorkspaceNoticeEventArgs { Message = message, Kind = kind });

        if (autoClear is not { } delay)
        {
            return;
        }

        _clear = new CancellationTokenSource();
        var token = _clear.Token;
        _ = ClearLaterAsync(delay, token);
    }

    public void Clear()
    {
        CancelPendingClear();
        Message = null;
        Kind = WorkspaceNoticeKind.Info;
        Raised?.Invoke(this, new WorkspaceNoticeEventArgs { Message = null, Kind = WorkspaceNoticeKind.Info });
    }

    private async Task ClearLaterAsync(TimeSpan delay, CancellationToken token)
    {
        try
        {
            await Task.Delay(delay, token).ConfigureAwait(true);
            if (!token.IsCancellationRequested)
            {
                Clear();
            }
        }
        catch (OperationCanceledException)
        {
            // A newer notice replaced this one.
        }
    }

    private void CancelPendingClear()
    {
        if (_clear is null)
        {
            return;
        }

        _clear.Cancel();
        _clear.Dispose();
        _clear = null;
    }

    private sealed class NullWorkspaceNotice : IWorkspaceNotice
    {
        public event EventHandler<WorkspaceNoticeEventArgs>? Raised
        {
            add { }
            remove { }
        }

        public string? Message => null;

        public WorkspaceNoticeKind Kind => WorkspaceNoticeKind.Info;

        public void ShowSaved(string message)
        {
        }

        public void ShowError(string message)
        {
        }

        public void Show(string message, WorkspaceNoticeKind kind, TimeSpan? autoClear = null)
        {
        }

        public void Clear()
        {
        }
    }
}
