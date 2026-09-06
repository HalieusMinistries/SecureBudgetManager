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
        Assert.Contains("EditorOverlay", xaml, StringComparison.Ordinal);
        Assert.Contains("IsEditorVisible", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductPlanningAndGuidanceEditorsUseTheSharedOverlay()
    {
        var root = FindRepoRoot();
        var overlay = File.ReadAllText(Path.Combine(root, "src", "SecureBudgetManager.App", "Views", "Editors", "EditorOverlay.xaml"));
        var products = File.ReadAllText(Path.Combine(root, "src", "SecureBudgetManager.App", "Views", "ProductsView.xaml"));
        var planning = File.ReadAllText(Path.Combine(root, "src", "SecureBudgetManager.App", "Views", "PlanningView.xaml"));
        var guidance = File.ReadAllText(Path.Combine(root, "src", "SecureBudgetManager.App", "Views", "LocalGuidanceView.xaml"));

        Assert.Contains("ProductEditorForm", overlay, StringComparison.Ordinal);
        Assert.Contains("PlanningEditorForm", overlay, StringComparison.Ordinal);
        Assert.Contains("GuidanceEditorForm", overlay, StringComparison.Ordinal);
        Assert.Contains("BeginAddCommand", products, StringComparison.Ordinal);
        Assert.Contains("BeginEditCommand", products, StringComparison.Ordinal);
        Assert.Contains("LeftDoubleClick", products, StringComparison.Ordinal);
        Assert.DoesNotContain("Command=\"{Binding SaveProductCommand}\"", products, StringComparison.Ordinal);
        Assert.Contains("BeginAddCommand", planning, StringComparison.Ordinal);
        Assert.Contains("BeginEditCommand", planning, StringComparison.Ordinal);
        Assert.DoesNotContain("True-cost car planner", planning, StringComparison.Ordinal);
        Assert.Contains("Save locality", guidance, StringComparison.Ordinal);
        Assert.Contains("BeginAddCommand", guidance, StringComparison.Ordinal);
        Assert.DoesNotContain("Enter or edit a figure", guidance, StringComparison.Ordinal);
    }

    [Fact]
    public void TransferAndForeignAccountEditorsUseTheSharedOverlay()
    {
        var root = FindRepoRoot();
        var overlay = File.ReadAllText(Path.Combine(root, "src", "SecureBudgetManager.App", "Views", "Editors", "EditorOverlay.xaml"));
        var transfers = File.ReadAllText(Path.Combine(root, "src", "SecureBudgetManager.App", "Views", "InternationalTransfersView.xaml"));
        var accounts = File.ReadAllText(Path.Combine(root, "src", "SecureBudgetManager.App", "Views", "ForeignAccountsView.xaml"));

        Assert.Contains("InternationalTransferEditorForm", overlay, StringComparison.Ordinal);
        Assert.Contains("ForeignAccountEditorForm", overlay, StringComparison.Ordinal);
        Assert.Contains("BeginAddTransferCommand", transfers, StringComparison.Ordinal);
        Assert.Contains("BeginEditTransferCommand", transfers, StringComparison.Ordinal);
        Assert.Contains("SelectTransferCommand", transfers, StringComparison.Ordinal);
        Assert.Contains("LeftDoubleClick", transfers, StringComparison.Ordinal);
        Assert.DoesNotContain("Command=\"{Binding SaveTransferCommand}\"", transfers, StringComparison.Ordinal);
        Assert.Contains("BeginAddCommand", accounts, StringComparison.Ordinal);
        Assert.Contains("BeginEditCommand", accounts, StringComparison.Ordinal);
        Assert.Contains("SelectAccountCommand", accounts, StringComparison.Ordinal);
        Assert.DoesNotContain("Command=\"{Binding SaveAccountCommand}\"", accounts, StringComparison.Ordinal);
        Assert.Contains("Possible reporting reminders", accounts, StringComparison.Ordinal);
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
