using System.Windows;
using SecureBudgetManager.Core.Layout;

namespace SecureBudgetManager.App.Windows;

internal static class MainWindowPlacement
{
    public static void FitToCurrentMonitor(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var work = MonitorWorkArea.GetDipWorkArea(window);
        var fitted = WindowLayoutCalculator.FitToWorkArea(
            window.Left,
            window.Top,
            window.Width,
            window.Height,
            window.MinWidth,
            window.MinHeight,
            work.Left,
            work.Top,
            work.Width,
            work.Height);

        if (fitted.Width < window.MinWidth)
        {
            window.MinWidth = fitted.Width;
        }

        if (fitted.Height < window.MinHeight)
        {
            window.MinHeight = fitted.Height;
        }

        window.Width = fitted.Width;
        window.Height = fitted.Height;
        window.Left = fitted.Left;
        window.Top = fitted.Top;
    }
}
