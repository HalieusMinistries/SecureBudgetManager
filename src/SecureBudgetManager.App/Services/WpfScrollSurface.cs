using System.Windows.Controls;
using SecureBudgetManager.Core.Capture;

namespace SecureBudgetManager.App.Services;

internal sealed class WpfScrollSurface : IScrollSurface
{
    private readonly ScrollViewer _viewer;

    public WpfScrollSurface(ScrollViewer viewer)
    {
        _viewer = viewer;
    }

    public double VerticalOffset => _viewer.VerticalOffset;

    public double HorizontalOffset => _viewer.HorizontalOffset;

    public double ExtentHeight => _viewer.ExtentHeight;

    public double ViewportHeight => _viewer.ViewportHeight;

    public double? ExplicitHeight
    {
        get => double.IsNaN(_viewer.Height) ? null : _viewer.Height;
        set => _viewer.Height = value ?? double.NaN;
    }

    public void ScrollTo(double verticalOffset, double horizontalOffset)
    {
        _viewer.ScrollToVerticalOffset(verticalOffset);
        _viewer.ScrollToHorizontalOffset(horizontalOffset);
        _viewer.UpdateLayout();
    }
}
