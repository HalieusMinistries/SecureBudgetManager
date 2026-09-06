namespace SecureBudgetManager.App.Tests.Capture;

public sealed class CaptureSourceGuardTests
{
    [Fact]
    public void CaptureService_WritesDirectlyAndNeverUsesTempOrClipboard()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "src",
            "SecureBudgetManager.App",
            "Services",
            "WpfFullPageCaptureService.cs"));

        Assert.Contains("new FileStream(path, FileMode.Create", source, StringComparison.Ordinal);
        Assert.Contains("PngBitmapEncoder", source, StringComparison.Ordinal);
        Assert.Contains("RenderTargetBitmap", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetTempPath", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetTempFileName", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Path.GetTemp", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Clipboard", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CopyFromScreen", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CaptureCommand_DoesNotLogPathsOrFinancialValues()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "src",
            "SecureBudgetManager.App",
            "Services",
            "FullPageCaptureCommand.cs"));

        Assert.Contains("Files written: {FileCount}", source, StringComparison.Ordinal);
        Assert.DoesNotContain("destinationPath", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LogInformation(\"{Path}", source, StringComparison.Ordinal);
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
