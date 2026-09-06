namespace SecureBudgetManager.Core.Capture;

/// <summary>
/// Saved scroll and size values so a capture can always put the page back as the user left it.
/// </summary>
public sealed record ScrollCaptureState(
    double VerticalOffset,
    double HorizontalOffset,
    double? ExplicitHeight);

public interface IScrollSurface
{
    double VerticalOffset { get; }

    double HorizontalOffset { get; }

    double ExtentHeight { get; }

    double ViewportHeight { get; }

    double? ExplicitHeight { get; set; }

    void ScrollTo(double verticalOffset, double horizontalOffset);
}

/// <summary>
/// Records and restores every scroll surface touched during a capture.
/// </summary>
public sealed class ScrollCaptureSession : IDisposable
{
    private readonly List<(IScrollSurface Surface, ScrollCaptureState State)> _saved = [];
    private bool _restored;

    public IReadOnlyList<ScrollCaptureState> SavedStates => _saved.ConvertAll(entry => entry.State);

    public void Remember(IScrollSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        _saved.Add((surface, new ScrollCaptureState(surface.VerticalOffset, surface.HorizontalOffset, surface.ExplicitHeight)));
    }

    public void Restore()
    {
        if (_restored)
        {
            return;
        }

        for (var i = _saved.Count - 1; i >= 0; i--)
        {
            var (surface, state) = _saved[i];
            surface.ExplicitHeight = state.ExplicitHeight;
            surface.ScrollTo(state.VerticalOffset, state.HorizontalOffset);
        }

        _restored = true;
    }

    public void Dispose() => Restore();
}
