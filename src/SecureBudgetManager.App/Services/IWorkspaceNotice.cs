namespace SecureBudgetManager.App.Services;

public enum WorkspaceNoticeKind
{
    Info = 0,
    Success = 1,
    Warning = 2,
    Error = 3
}

public sealed class WorkspaceNoticeEventArgs : EventArgs
{
    public required string? Message { get; init; }

    public required WorkspaceNoticeKind Kind { get; init; }
}

/// <summary>
/// Brief, non-blocking confirmation or failure notice shown above the current page.
/// </summary>
public interface IWorkspaceNotice
{
    event EventHandler<WorkspaceNoticeEventArgs>? Raised;

    string? Message { get; }

    WorkspaceNoticeKind Kind { get; }

    void ShowSaved(string message);

    void ShowError(string message);

    void Show(string message, WorkspaceNoticeKind kind, TimeSpan? autoClear = null);

    void Clear();
}
