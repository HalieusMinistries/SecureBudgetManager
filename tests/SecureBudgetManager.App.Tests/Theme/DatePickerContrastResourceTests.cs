using System.Globalization;
using System.Xml.Linq;
using SecureBudgetManager.Core.Layout;

namespace SecureBudgetManager.App.Tests.Theme;

public sealed class DatePickerContrastResourceTests
{
    [Fact]
    public void SharedDatePickerStyle_ReplacesWhiteTextBoxAndSystemCalendar()
    {
        var xaml = File.ReadAllText(ThemePath("DatePicker.xaml"));

        Assert.Contains("x:Key=\"FormDatePicker\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"FormDatePickerTextBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"FormCalendar\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"FormCalendarDayButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PART_TextBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PART_Button\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PART_Popup\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PART_Watermark\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PART_ContentHost\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Foreground=\"{StaticResource Brush.Text}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Background=\"{StaticResource Brush.Window}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Foreground=\"{StaticResource Brush.TextSecondary}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CalendarStyle\" Value=\"{StaticResource FormCalendar}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Style TargetType=\"DatePicker\" BasedOn=\"{StaticResource FormDatePicker}\"/>", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SystemColors.WindowBrush", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SystemColors.WindowTextBrush", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SystemColors.HighlightBrush", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void PlaceholderUsesSecondaryText_AtFullOpacity()
    {
        var xaml = File.ReadAllText(ThemePath("DatePicker.xaml"));
        var watermark = Slice(xaml, "x:Name=\"PART_Watermark\"", "x:Name=\"PART_ContentHost\"");

        Assert.Contains("Foreground=\"{StaticResource Brush.TextSecondary}\"", watermark, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"Watermarked\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Storyboard.TargetName=\"PART_Watermark\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Storyboard.TargetProperty=\"Opacity\" To=\"1\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("To=\"0.6\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void CalendarDaysAndSelectedDay_UseReadableThemeBrushes()
    {
        var xaml = File.ReadAllText(ThemePath("DatePicker.xaml"));
        var dayButton = Slice(xaml, "x:Key=\"FormCalendarDayButton\"", "x:Key=\"FormCalendarButton\"");

        Assert.Contains("x:Name=\"Selected\"", dayButton, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"Today\"", dayButton, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MouseOver\"", dayButton, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"Disabled\"", dayButton, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"Inactive\"", dayButton, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"BlackoutDay\"", dayButton, StringComparison.Ordinal);
        Assert.Contains("Brush.Accent", dayButton, StringComparison.Ordinal);
        Assert.Contains("Brush.Window", dayButton, StringComparison.Ordinal);
        Assert.Contains("Brush.TextMuted", dayButton, StringComparison.Ordinal);
        Assert.Contains("DayTitleTemplateResourceKey", xaml, StringComparison.Ordinal);
        Assert.Contains("PART_PreviousButton", xaml, StringComparison.Ordinal);
        Assert.Contains("PART_NextButton", xaml, StringComparison.Ordinal);
        Assert.Contains("PART_HeaderButton", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void DatePickerTextAndPlaceholder_MeetWcagAa()
    {
        var colors = LoadColors();

        Assert.True(FromRgb(colors["Color.Text"], colors["Color.Window"]) >= ContrastRatio.AaNormalText);
        Assert.True(FromRgb(colors["Color.TextSecondary"], colors["Color.Window"]) >= ContrastRatio.AaNormalText);
        Assert.True(FromRgb(colors["Color.Text"], colors["Color.Surface"]) >= ContrastRatio.AaNormalText);
        Assert.True(FromRgb(colors["Color.Window"], colors["Color.Accent"]) >= ContrastRatio.AaNormalText);
        Assert.True(FromRgb(colors["Color.TextSecondary"], colors["Color.Surface"]) >= ContrastRatio.AaNormalText);
    }

    [Theory]
    [InlineData("IncomeView.xaml")]
    [InlineData("ExpensesView.xaml")]
    [InlineData("PayrollView.xaml")]
    public void Views_DoNotOverrideDatePickerForeground(string fileName)
    {
        var xaml = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "SecureBudgetManager.App", "Views", fileName));
        Assert.DoesNotContain("DatePicker Foreground=", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<DatePicker Foreground=", xaml, StringComparison.Ordinal);
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
