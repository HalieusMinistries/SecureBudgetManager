using System.Xml.Linq;

namespace SecureBudgetManager.Tests.Layout;

public sealed class WindowChromePolicyTests
{
    [Fact]
    public void MainWindow_UsesStandardResizableChrome()
    {
        var xaml = File.ReadAllText(GetMainWindowXamlPath());

        Assert.Contains("Title=\"Secure Budget Manager\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Width=\"1100\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Height=\"650\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"900\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"560\"", xaml, StringComparison.Ordinal);
        Assert.Contains("WindowStartupLocation=\"CenterScreen\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ResizeMode=\"CanResize\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SizeToContent=\"Manual\"", xaml, StringComparison.Ordinal);
        Assert.Contains("WindowStyle=\"SingleBorderWindow\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("WindowStyle=\"None\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowsTransparency=\"True\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("WindowChrome", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MaxWidth", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MaxHeight", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SizeToContent=\"WidthAndHeight\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SizeToContent=\"Width\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SizeToContent=\"Height\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void AppManifest_DeclaresPerMonitorV2Only()
    {
        var manifest = XDocument.Load(GetAppManifestPath());
        XNamespace windowsSettings = "http://schemas.microsoft.com/SMI/2016/WindowsSettings";
        XNamespace legacyDpi = "http://schemas.microsoft.com/SMI/2005/WindowsSettings";

        var dpiAwareness = manifest.Descendants(windowsSettings + "dpiAwareness").Single().Value;
        var dpiAware = manifest.Descendants(legacyDpi + "dpiAware").Single().Value;

        Assert.Equal("PerMonitorV2", dpiAwareness);
        Assert.Equal("true/pm", dpiAware);
    }

    [Fact]
    public void AppProject_DoesNotDuplicateDpiMode()
    {
        var csproj = File.ReadAllText(GetAppProjectPath());

        Assert.DoesNotContain("ApplicationHighDpiMode", csproj, StringComparison.Ordinal);
        Assert.Contains("<ApplicationManifest>app.manifest</ApplicationManifest>", csproj, StringComparison.Ordinal);
    }

    private static string GetMainWindowXamlPath() =>
        Path.Combine(FindRepoRoot(), "src", "SecureBudgetManager.App", "MainWindow.xaml");

    private static string GetAppManifestPath() =>
        Path.Combine(FindRepoRoot(), "src", "SecureBudgetManager.App", "app.manifest");

    private static string GetAppProjectPath() =>
        Path.Combine(FindRepoRoot(), "src", "SecureBudgetManager.App", "SecureBudgetManager.App.csproj");

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

        throw new InvalidOperationException("Could not locate the repository root from the test output directory.");
    }
}
