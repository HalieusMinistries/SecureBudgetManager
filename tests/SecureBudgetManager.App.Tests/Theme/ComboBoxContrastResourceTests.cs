using System.Globalization;
using System.Xml.Linq;
using SecureBudgetManager.Core.Layout;

namespace SecureBudgetManager.App.Tests.Theme;

public sealed class ComboBoxContrastResourceTests
{
    [Fact]
    public void SharedComboBoxStyle_UsesDarkThemedTemplateNotSystemChrome()
    {
        var xaml = File.ReadAllText(ThemePath("Controls.xaml"));

        Assert.Contains("x:Key=\"FormComboBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"FormComboBoxItem\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Style TargetType=\"ComboBox\" BasedOn=\"{StaticResource FormComboBox}\"/>", xaml, StringComparison.Ordinal);
        Assert.Contains("<Style TargetType=\"ComboBoxItem\" BasedOn=\"{StaticResource FormComboBoxItem}\"/>", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PART_Popup\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DropDownBorder\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Background=\"{StaticResource Brush.Surface}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TextElement.Foreground=\"{StaticResource Brush.Text}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Fill=\"{StaticResource Brush.TextSecondary}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SystemColors.WindowBrush", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SystemColors.HighlightBrush", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SystemColors.WindowTextBrush", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ComboBoxItemStates_UseThemeBrushesForHighlightAndDisabled()
    {
        var xaml = File.ReadAllText(ThemePath("Controls.xaml"));
        var formItem = Slice(xaml, "x:Key=\"FormComboBoxItem\"", "x:Key=\"FormComboBox\"");

        Assert.Contains("Property=\"IsHighlighted\" Value=\"True\"", formItem, StringComparison.Ordinal);
        Assert.Contains("Property=\"IsSelected\" Value=\"True\"", formItem, StringComparison.Ordinal);
        Assert.Contains("Brush.AccentMuted", formItem, StringComparison.Ordinal);
        Assert.Contains("Brush.SurfaceRaised", formItem, StringComparison.Ordinal);
        Assert.Contains("Property=\"IsEnabled\" Value=\"False\"", formItem, StringComparison.Ordinal);
        Assert.Contains("Brush.TextMuted", formItem, StringComparison.Ordinal);
        Assert.Contains("Foreground\" Value=\"{StaticResource Brush.Text}\"", formItem, StringComparison.Ordinal);
    }

    [Fact]
    public void ThemeTextOnSurfaces_MeetsWcagAa()
    {
        var colors = LoadColors();

        Assert.True(FromRgb(colors["Color.Text"], colors["Color.Window"]) >= ContrastRatio.AaNormalText);
        Assert.True(FromRgb(colors["Color.Text"], colors["Color.Surface"]) >= ContrastRatio.AaNormalText);
        Assert.True(FromRgb(colors["Color.Text"], colors["Color.SurfaceRaised"]) >= ContrastRatio.AaNormalText);
        Assert.True(FromRgb(colors["Color.Text"], colors["Color.AccentMuted"]) >= ContrastRatio.AaNormalText);
        Assert.True(FromRgb(colors["Color.TextSecondary"], colors["Color.Window"]) >= ContrastRatio.AaNormalText);
        Assert.True(FromRgb(colors["Color.TextSecondary"], colors["Color.Surface"]) >= ContrastRatio.AaNormalText);
        Assert.True(FromRgb(colors["Color.TextMuted"], colors["Color.Window"]) >= 3.0);
        Assert.True(FromRgb(colors["Color.TextMuted"], colors["Color.Surface"]) >= 3.0);
    }

    [Theory]
    [InlineData("IncomeView.xaml")]
    [InlineData("ExpensesView.xaml")]
    [InlineData("PayrollView.xaml")]
    [InlineData("HouseholdView.xaml")]
    [InlineData("SettingsView.xaml")]
    [InlineData("DashboardView.xaml")]
    public void Views_DoNotOverrideComboBoxForeground(string fileName)
    {
        var xaml = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "SecureBudgetManager.App", "Views", fileName));
        Assert.DoesNotContain("ComboBox Foreground=", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<ComboBox Foreground=", xaml, StringComparison.Ordinal);
    }

    private static Dictionary<string, (byte R, byte G, byte B)> LoadColors()
    {
        XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var document = XDocument.Load(ThemePath("Colors.xaml"));
        var colors = new Dictionary<string, (byte R, byte G, byte B)>(StringComparer.Ordinal);

        foreach (var color in document.Descendants(ns + "Color"))
        {
            var key = color.Attribute(x + "Key")?.Value;
            var hex = color.Value.Trim();
            if (key is null || hex.Length < 7)
            {
                continue;
            }

            colors[key] = (
                byte.Parse(hex[1..3], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(hex[3..5], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(hex[5..7], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        }

        return colors;
    }

    private static double FromRgb((byte R, byte G, byte B) left, (byte R, byte G, byte B) right) =>
        ContrastRatio.FromRgb(left.R, left.G, left.B, right.R, right.G, right.B);

    private static string Slice(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, start);
        var endIndex = source.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        Assert.True(endIndex > startIndex, end);
        return source[startIndex..endIndex];
    }

    private static string ThemePath(string fileName) =>
        Path.Combine(FindRepoRoot(), "src", "SecureBudgetManager.App", "Themes", fileName);

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SecureBudgetManager.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find the repository root.");
    }
}
