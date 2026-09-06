namespace SecureBudgetManager.App.Tests.Views;

public sealed class WorkspaceShellXamlTests
{
    [Fact]
    public void WorkspaceSidebar_UsesLocalOnlyLabel()
    {
        var xaml = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "SecureBudgetManager.App", "Views", "WorkspaceView.xaml"));

        Assert.Contains("LOCAL ONLY", xaml, StringComparison.Ordinal);
        Assert.Contains("Stored only on this computer", xaml, StringComparison.Ordinal);
        Assert.Contains("No cloud, banks or telemetry", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ENCRYPTED", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("computer\"/>", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ImplicitViewTemplates_AreWrappedSoUserControlsDoNotRecurse()
    {
        var xaml = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "SecureBudgetManager.App", "App.xaml"));

        var start = xaml.IndexOf("DataType=\"{x:Type vm:MainViewModel}\"", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var slice = xaml.Substring(start, Math.Min(180, xaml.Length - start));
        Assert.Contains("<Grid>", slice, StringComparison.Ordinal);
        Assert.Contains("<views:WorkspaceView/>", slice, StringComparison.Ordinal);
        Assert.Contains("</Grid>", slice, StringComparison.Ordinal);
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
