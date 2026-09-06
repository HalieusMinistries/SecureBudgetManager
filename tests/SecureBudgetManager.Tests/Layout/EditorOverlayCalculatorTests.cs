using SecureBudgetManager.Core.Layout;

namespace SecureBudgetManager.Tests.Layout;

public sealed class EditorOverlayCalculatorTests
{
    [Fact]
    public void FitToWorkArea_CentresPreferredOverlayOn1366By720()
    {
        var bounds = EditorOverlayCalculator.FitToWorkArea(1366, 720);

        Assert.Equal(720, bounds.Width);
        Assert.Equal(640, bounds.Height);
        Assert.Equal((1366 - 720) / 2d, bounds.Left);
        Assert.Equal((720 - 640) / 2d, bounds.Top);
        Assert.True(EditorOverlayCalculator.SaveAndCancelRemainReachable(bounds.Height));
    }

    [Fact]
    public void FitToWorkArea_ShrinksToStayInsideASmallerWorkArea()
    {
        var bounds = EditorOverlayCalculator.FitToWorkArea(400, 300);

        Assert.True(bounds.Width <= 400);
        Assert.True(bounds.Height <= 300);
        Assert.True(bounds.Left >= 0);
        Assert.True(bounds.Top >= 0);
        Assert.True(bounds.Left + bounds.Width <= 400);
        Assert.True(bounds.Top + bounds.Height <= 300);
        Assert.True(bounds.Width >= EditorOverlayCalculator.MinimumWidth - 40 || bounds.Width == 400 - (EditorOverlayCalculator.Margin * 2));
    }
}
