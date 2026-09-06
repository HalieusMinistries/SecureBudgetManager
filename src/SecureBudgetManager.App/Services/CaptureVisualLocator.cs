using System.Windows;

namespace SecureBudgetManager.App.Services;

/// <summary>
/// Filled by the main window after layout. Avoids a DI cycle between the window and the command.
/// </summary>
public sealed class CaptureVisualLocator : ICaptureVisualLocator
{
    public bool IsWorkspaceVisible { get; private set; }

    public FrameworkElement? Page { get; private set; }

    public FrameworkElement? FindActivePage() => IsWorkspaceVisible ? Page : null;

    public void Update(bool workspaceVisible, FrameworkElement? page)
    {
        IsWorkspaceVisible = workspaceVisible;
        Page = workspaceVisible ? page : null;
    }
}
