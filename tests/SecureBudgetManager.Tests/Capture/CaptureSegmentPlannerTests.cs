using SecureBudgetManager.Core.Capture;

namespace SecureBudgetManager.Tests.Capture;

public sealed class CaptureSegmentPlannerTests
{
    [Fact]
    public void MeasuredContent_ShorterThanLimit_ProducesOneSegmentFromOrigin()
    {
        var segments = CaptureSegmentPlanner.Plan(contentHeight: 1800);

        var segment = Assert.Single(segments);
        Assert.Equal(0, segment.Index);
        Assert.Equal(0, segment.Offset);
        Assert.Equal(1800, segment.Height);
    }

    [Fact]
    public void ExtremelyTallPage_SplitsOnlyAtBitmapLimitWithoutGaps()
    {
        var segments = CaptureSegmentPlanner.Plan(contentHeight: 10_000, maximumTileHeight: 4096);

        Assert.Equal(3, segments.Count);
        Assert.Equal(0, segments[0].Offset);
        Assert.Equal(4096, segments[0].Height);
        Assert.Equal(4096, segments[1].Offset);
        Assert.Equal(4096, segments[1].Height);
        Assert.Equal(8192, segments[2].Offset);
        Assert.Equal(1808, segments[2].Height);

        var covered = segments.Sum(segment => segment.Height);
        Assert.Equal(10_000, covered);
        Assert.Equal(Enumerable.Range(0, segments.Count), segments.Select(segment => segment.Index));
    }

    [Fact]
    public void EmptyOrInvalidMeasurements_ProduceNoSegments()
    {
        Assert.Empty(CaptureSegmentPlanner.Plan(0));
        Assert.Empty(CaptureSegmentPlanner.Plan(-10));
    }
}
