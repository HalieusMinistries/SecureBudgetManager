using System.Xml.Linq;

namespace SecureBudgetManager.App.Tests.Capture;

public sealed class FullPageCaptureRoutingTests
{
    [Fact]
    public void MainWindow_BindsCtrlSToCaptureFullPage()
    {
        var xaml = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "SecureBudgetManager.App", "MainWindow.xaml"));

        Assert.Contains("Key=\"S\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Modifiers=\"Control\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding CaptureFullPageCommand}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Command=\"{Binding SaveCommand}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_ExposesDiscoverableCaptureCommandWithShortcut()
    {
        var xaml = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "SecureBudgetManager.App", "Views", "SettingsView.xaml"));

        Assert.Contains("Content=\"Capture full page\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"Ctrl+S\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CaptureFullPageCommand", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthenticationViews_DoNotHostCaptureCommand()
    {
        var unlock = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "SecureBudgetManager.App", "Views", "UnlockView.xaml"));
        var setup = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "SecureBudgetManager.App", "Views", "SetupView.xaml"));

        Assert.DoesNotContain("CaptureFullPage", unlock, StringComparison.Ordinal);
        Assert.DoesNotContain("Capture full page", unlock, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CaptureFullPage", setup, StringComparison.Ordinal);
        Assert.DoesNotContain("Capture full page", setup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SaveFileDialog_DoesNotPresetAUserFolder()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "src",
            "SecureBudgetManager.App",
            "Services",
            "WpfCaptureDialogs.cs"));

        Assert.Contains("new SaveFileDialog", source, StringComparison.Ordinal);
        Assert.Contains("OverwritePrompt = true", source, StringComparison.Ordinal);
        Assert.DoesNotContain("InitialDirectory", source, StringComparison.Ordinal);
        Assert.Contains("This screenshot contains household financial information.", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AppProject_DoesNotReferenceScreenshotLibraries()
    {
        var project = XDocument.Load(Path.Combine(FindRepoRoot(), "src", "SecureBudgetManager.App", "SecureBudgetManager.App.csproj"));
        var ids = project.Descendants("PackageReference")
            .Select(element => element.Attribute("Include")?.Value ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain(ids, id => id.Contains("Playwright", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(ids, id => id.Contains("Puppeteer", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(ids, id => id.Contains("Selenium", StringComparison.OrdinalIgnoreCase));
    }

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
