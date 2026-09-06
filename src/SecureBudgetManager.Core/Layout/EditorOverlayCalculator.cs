namespace SecureBudgetManager.Core.Layout;

/// <summary>
/// Sizes the shared record editor so Save and Cancel stay on a 1366×720 working area.
/// </summary>
public static class EditorOverlayCalculator
{
    public const double PreferredWidth = 720;
    public const double PreferredHeight = 640;
    public const double MinimumWidth = 360;
    public const double MinimumHeight = 280;
    public const double Margin = 24;

    public static WindowBounds FitToWorkArea(double workWidth, double workHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(workWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(workHeight);

        var width = Math.Min(PreferredWidth, Math.Max(MinimumWidth, workWidth - (Margin * 2)));
        var height = Math.Min(PreferredHeight, Math.Max(MinimumHeight, workHeight - (Margin * 2)));
        width = Math.Min(width, workWidth);
        height = Math.Min(height, workHeight);
        var left = Math.Max(0, (workWidth - width) / 2);
        var top = Math.Max(0, (workHeight - height) / 2);
        return new WindowBounds(left, top, width, height);
    }

    public static bool SaveAndCancelRemainReachable(double panelHeight) =>
        panelHeight >= MinimumHeight && panelHeight <= PreferredHeight;
}
