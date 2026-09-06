using System.Windows;

namespace SecureBudgetManager.App.Services;

public interface ICaptureVisualLocator
{
    bool IsWorkspaceVisible { get; }

    FrameworkElement? FindActivePage();
}
