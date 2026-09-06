using System.Xml.Linq;
using SecureBudgetManager.Core.Models;

namespace SecureBudgetManager.App.Tests.Architecture;

public sealed class CoreRemainsUiFreeTests
{
    [Fact]
    public void CoreAssemblyDoesNotReferenceWpf()
    {
        var referenced = typeof(Money).Assembly
            .GetReferencedAssemblies()
            .Select(name => name.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain(referenced, name => name.Contains("PresentationFramework", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(referenced, name => name.Contains("PresentationCore", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(referenced, name => name.Contains("WindowsBase", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(referenced, name => name.Contains("System.Xaml", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AppProjectDoesNotAddNetworkOrTelemetryPackages()
    {
        var project = Path.Combine(FindRepoRoot(), "src", "SecureBudgetManager.App", "SecureBudgetManager.App.csproj");
        var ids = XDocument.Load(project)
            .Descendants("PackageReference")
            .Select(element => element.Attribute("Include")?.Value ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain(ids, id => id.Contains("Http", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(ids, id => id.Contains("ApplicationInsights", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(ids, id => id.Contains("OpenTelemetry", StringComparison.OrdinalIgnoreCase));
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
