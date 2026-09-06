namespace SecureBudgetManager.Core.Capture;

public sealed record CaptureSegment(int Index, double Offset, double Height);

/// <summary>
/// Splits a tall page into renderable tiles. Viewport height must not drive the output
/// size: a normal form is one image. Tiles are used only when the measured content
/// exceeds a genuine bitmap-dimension limit.
/// </summary>
public static class CaptureSegmentPlanner
{
    public const double DefaultMaximumTileHeight = 4096;

    public static IReadOnlyList<CaptureSegment> Plan(
        double contentHeight,
        double maximumTileHeight = DefaultMaximumTileHeight)
    {
        if (contentHeight <= 0)
        {
            return [];
        }

        if (maximumTileHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumTileHeight), "A tile must have a positive height.");
        }

        var segments = new List<CaptureSegment>();
        var offset = 0d;
        var index = 0;

        while (offset < contentHeight - 0.5)
        {
            var remaining = contentHeight - offset;
            var height = Math.Min(maximumTileHeight, remaining);
            segments.Add(new CaptureSegment(index, offset, height));
            offset += height;
            index++;
        }

        return segments;
    }
}
