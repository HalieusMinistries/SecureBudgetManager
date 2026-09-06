namespace SecureBudgetManager.Core.Layout;

/// <summary>
/// WCAG 2 contrast between two sRGB colours. Used to keep theme text readable.
/// </summary>
public static class ContrastRatio
{
    public const double AaNormalText = 4.5;

    public static double FromRgb(byte r1, byte g1, byte b1, byte r2, byte g2, byte b2)
    {
        var lighter = Math.Max(RelativeLuminance(r1, g1, b1), RelativeLuminance(r2, g2, b2));
        var darker = Math.Min(RelativeLuminance(r1, g1, b1), RelativeLuminance(r2, g2, b2));
        return (lighter + 0.05) / (darker + 0.05);
    }

    public static double RelativeLuminance(byte r, byte g, byte b)
    {
        return (0.2126 * Channel(r)) + (0.7152 * Channel(g)) + (0.0722 * Channel(b));
    }

    private static double Channel(byte value)
    {
        var scaled = value / 255d;
        return scaled <= 0.04045 ? scaled / 12.92 : Math.Pow((scaled + 0.055) / 1.055, 2.4);
    }
}
