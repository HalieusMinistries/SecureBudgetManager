using SecureBudgetManager.Core.Capture;

namespace SecureBudgetManager.Tests.Capture;

public sealed class ScrollCaptureSessionTests
{
    [Fact]
    public void Restore_PutsOffsetsAndHeightBackAfterSuccess()
    {
        var surface = new FakeScrollSurface(120, 8, 400);

        using (var session = new ScrollCaptureSession())
        {
            session.Remember(surface);
            surface.ScrollTo(900, 40);
            surface.ExplicitHeight = 2000;
            session.Restore();
        }

        Assert.Equal(120, surface.VerticalOffset);
        Assert.Equal(8, surface.HorizontalOffset);
        Assert.Equal(400, surface.ExplicitHeight);
    }

    [Fact]
    public void Dispose_RestoresAfterFailure()
    {
        var surface = new FakeScrollSurface(40, 0, null);

        try
        {
            using var session = new ScrollCaptureSession();
            session.Remember(surface);
            surface.ScrollTo(600, 12);
            surface.ExplicitHeight = 1800;
            throw new InvalidOperationException("Capture failed.");
        }
        catch (InvalidOperationException)
        {
            // Expected: the session must still restore in Dispose.
        }

        Assert.Equal(40, surface.VerticalOffset);
        Assert.Equal(0, surface.HorizontalOffset);
        Assert.Null(surface.ExplicitHeight);
    }

    private sealed class FakeScrollSurface : IScrollSurface
    {
        public FakeScrollSurface(double vertical, double horizontal, double? height)
        {
            VerticalOffset = vertical;
            HorizontalOffset = horizontal;
            ExplicitHeight = height;
        }

        public double VerticalOffset { get; private set; }

        public double HorizontalOffset { get; private set; }

        public double ExtentHeight => 2000;

        public double ViewportHeight => 400;

        public double? ExplicitHeight { get; set; }

        public void ScrollTo(double verticalOffset, double horizontalOffset)
        {
            VerticalOffset = verticalOffset;
            HorizontalOffset = horizontalOffset;
        }
    }
}
