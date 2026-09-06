using SecureBudgetManager.Core.Capture;

namespace SecureBudgetManager.Tests.Capture;

public sealed class FullPageCapturePlannerTests
{
    [Fact]
    public void OriginIsAlwaysZero_EvenWhenThePageIsScrolled()
    {
        var layout = FullPageCapturePlanner.Create(
            contentWidth: 800,
            contentHeight: 2000,
            verticalScrollOffset: 640,
            viewportHeight: 500);

        Assert.Equal(0, layout.OriginX);
        Assert.Equal(0, layout.OriginY);
        Assert.Equal(2000, layout.ContentHeight);
        Assert.Equal(800, layout.ContentWidth);
        Assert.Equal(2000, Assert.Single(layout.Segments).Height);
        Assert.Equal(2000, layout.CoverageEnd);
    }

    [Fact]
    public void OutputHeight_EqualsMeasuredContent_NotViewport()
    {
        var layout = FullPageCapturePlanner.Create(720, 1880, verticalScrollOffset: 300, viewportHeight: 560);

        Assert.Equal(1880, layout.ContentHeight);
        Assert.NotEqual(560, layout.ContentHeight);
        Assert.Equal(1880, layout.CoverageEnd);
        Assert.Equal(1, layout.FileCount);
    }

    [Theory]
    [InlineData(1400)]
    [InlineData(1800)]
    [InlineData(2200)]
    [InlineData(2800)]
    public void NormalIncomeAndExpenseHeights_ProduceOneImage(double contentHeight)
    {
        var layout = FullPageCapturePlanner.Create(
            contentWidth: 760,
            contentHeight: contentHeight,
            verticalScrollOffset: 400,
            viewportHeight: 480);

        Assert.Equal(1, layout.FileCount);
        Assert.Equal(0, layout.OriginY);
        Assert.Equal(contentHeight, layout.Segments[0].Height);
        Assert.Equal(contentHeight, layout.CoverageEnd);
    }

    [Fact]
    public void TrailingCoverage_StopsAtMeasuredContent()
    {
        var layout = FullPageCapturePlanner.Create(640, 10_000, 0, 600, maximumTileHeight: 4096);

        Assert.Equal(10_000, layout.CoverageEnd);
        Assert.Equal(10_000, layout.Segments.Sum(segment => segment.Height));
        Assert.True(layout.FileCount > 1);
    }
}
