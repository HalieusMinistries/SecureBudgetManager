namespace SecureBudgetManager.Core.Layout;

public readonly record struct WindowBounds(double Left, double Top, double Width, double Height);

/// <summary>
/// Fits a window rectangle to a monitor working area. Values are in the same units (typically DIPs).
/// </summary>
public static class WindowLayoutCalculator
{
    public static WindowBounds FitToWorkArea(
        double left,
        double top,
        double width,
        double height,
        double minWidth,
        double minHeight,
        double workLeft,
        double workTop,
        double workWidth,
        double workHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(workWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(workHeight);

        var fittedWidth = ClampSize(width, minWidth, workWidth);
        var fittedHeight = ClampSize(height, minHeight, workHeight);

        var fittedLeft = Align(left, fittedWidth, workLeft, workWidth);
        var fittedTop = Align(top, fittedHeight, workTop, workHeight);

        return new WindowBounds(fittedLeft, fittedTop, fittedWidth, fittedHeight);
    }

    public static double ClampSize(double requested, double minimum, double workExtent)
    {
        var size = requested;
        if (double.IsNaN(size) || size <= 0)
        {
            size = Math.Min(minimum, workExtent);
        }

        if (workExtent >= minimum)
        {
            size = Math.Max(size, minimum);
        }

        return Math.Min(size, workExtent);
    }

    private static double Align(double requestedOrigin, double size, double workOrigin, double workExtent)
    {
        if (double.IsNaN(requestedOrigin))
        {
            return workOrigin + Math.Max(0, (workExtent - size) / 2);
        }

        var maxOrigin = workOrigin + workExtent - size;
        if (maxOrigin < workOrigin)
        {
            return workOrigin;
        }

        return Math.Clamp(requestedOrigin, workOrigin, maxOrigin);
    }
}
