using SecureBudgetManager.Core.Layout;

namespace SecureBudgetManager.Tests.Layout;

public sealed class WindowLayoutCalculatorTests
{
    [Fact]
    public void FitToWorkArea_KeepsRequestedSizeWhenItFits()
    {
        var bounds = WindowLayoutCalculator.FitToWorkArea(
            left: 100,
            top: 40,
            width: 1100,
            height: 650,
            minWidth: 900,
            minHeight: 560,
            workLeft: 0,
            workTop: 0,
            workWidth: 1366,
            workHeight: 720);

        Assert.Equal(100, bounds.Left);
        Assert.Equal(40, bounds.Top);
        Assert.Equal(1100, bounds.Width);
        Assert.Equal(650, bounds.Height);
    }

    [Fact]
    public void FitToWorkArea_ClampsOversizedWindowTo1366By720WorkArea()
    {
        var bounds = WindowLayoutCalculator.FitToWorkArea(
            left: -20,
            top: -10,
            width: 1280,
            height: 820,
            minWidth: 1024,
            minHeight: 640,
            workLeft: 0,
            workTop: 0,
            workWidth: 1366,
            workHeight: 720);

        Assert.Equal(0, bounds.Left);
        Assert.Equal(0, bounds.Top);
        Assert.Equal(1280, bounds.Width);
        Assert.Equal(720, bounds.Height);
    }

    [Fact]
    public void FitToWorkArea_ShiftsWindowFullyOntoSecondaryMonitorWorkArea()
    {
        var bounds = WindowLayoutCalculator.FitToWorkArea(
            left: 2500,
            top: 20,
            width: 1100,
            height: 650,
            minWidth: 900,
            minHeight: 560,
            workLeft: 1366,
            workTop: 0,
            workWidth: 1366,
            workHeight: 720);

        Assert.Equal(1632, bounds.Left);
        Assert.Equal(20, bounds.Top);
        Assert.Equal(1100, bounds.Width);
        Assert.Equal(650, bounds.Height);
    }

    [Fact]
    public void FitToWorkArea_DoesNotForceMinimumLargerThanWorkArea()
    {
        var bounds = WindowLayoutCalculator.FitToWorkArea(
            left: 0,
            top: 0,
            width: 1100,
            height: 650,
            minWidth: 900,
            minHeight: 560,
            workLeft: 0,
            workTop: 0,
            workWidth: 800,
            workHeight: 500);

        Assert.Equal(800, bounds.Width);
        Assert.Equal(500, bounds.Height);
        Assert.Equal(0, bounds.Left);
        Assert.Equal(0, bounds.Top);
    }

    [Fact]
    public void FitToWorkArea_CentersWhenOriginIsUnknown()
    {
        var bounds = WindowLayoutCalculator.FitToWorkArea(
            left: double.NaN,
            top: double.NaN,
            width: 1100,
            height: 650,
            minWidth: 900,
            minHeight: 560,
            workLeft: 0,
            workTop: 0,
            workWidth: 1366,
            workHeight: 720);

        Assert.Equal(133, bounds.Left);
        Assert.Equal(35, bounds.Top);
        Assert.Equal(1100, bounds.Width);
        Assert.Equal(650, bounds.Height);
    }

    [Fact]
    public void ClampSize_RaisesToMinimumWhenWorkAreaAllowsIt()
    {
        var size = WindowLayoutCalculator.ClampSize(800, minimum: 900, workExtent: 1366);

        Assert.Equal(900, size);
    }
}
