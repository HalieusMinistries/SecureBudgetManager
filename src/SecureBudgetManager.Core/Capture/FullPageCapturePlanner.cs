namespace SecureBudgetManager.Core.Capture;

/// <summary>
/// Measured full-page capture bounds. Origin is always the top of the content,
/// never the current scroll offset. Output height is the measured content height,
/// never the viewport height.
/// </summary>
public sealed record FullPageCaptureLayout(
    double OriginX,
    double OriginY,
    double ContentWidth,
    double ContentHeight,
    IReadOnlyList<CaptureSegment> Segments)
{
    public int FileCount => Segments.Count;

    public double CoverageEnd => Segments.Count == 0 ? 0 : Segments[^1].Offset + Segments[^1].Height;
}

public static class FullPageCapturePlanner
{
    public static FullPageCaptureLayout Create(
        double contentWidth,
        double contentHeight,
        double verticalScrollOffset,
        double viewportHeight,
        double maximumTileHeight = CaptureSegmentPlanner.DefaultMaximumTileHeight)
    {
        _ = verticalScrollOffset;
        _ = viewportHeight;

        if (contentWidth <= 0 || contentHeight <= 0)
        {
            return new FullPageCaptureLayout(0, 0, 0, 0, []);
        }

        var segments = CaptureSegmentPlanner.Plan(contentHeight, maximumTileHeight);
        return new FullPageCaptureLayout(0, 0, contentWidth, contentHeight, segments);
    }
}
