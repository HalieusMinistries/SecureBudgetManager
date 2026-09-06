using System.Xml.Linq;
using SecureBudgetManager.Core.Models;

namespace SecureBudgetManager.Tests.Security;

public sealed class SecurityBoundaryTests
{
    private static readonly string[] DisallowedAssemblyNameFragments =
    [
        "PresentationFramework",
        "PresentationCore",
        "WindowsBase",
        "System.Xaml",
        "Sqlite",
        "SQLite",
        "SQLCipher",
        "Http.Client"
    ];

    [Fact]
    public void Core_MustNotReferenceWpfSqliteOrHttpClientAssemblies()
    {
        var referenced = typeof(Money).Assembly
            .GetReferencedAssemblies()
            .Select(name => name.Name ?? string.Empty)
            .ToArray();

        foreach (var fragment in DisallowedAssemblyNameFragments)
        {
            Assert.DoesNotContain(referenced, name => name.Contains(fragment, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void InfrastructureProject_MustUseOrdinarySqlite()
    {
        var packageIds = ReadPackageIds(GetInfrastructureProjectPath());

        Assert.Contains(packageIds, id => id.Equals("SQLitePCLRaw.bundle_e_sqlite3", StringComparison.Ordinal));
        Assert.DoesNotContain(packageIds, id => id.Contains("sqlcipher", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(packageIds, id => id.Contains("ProtectedData", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NoProject_MustReferenceNetworkOrTelemetryPackages()
    {
        var projects = Directory.GetFiles(
            Path.Combine(FindRepoRoot(), "src"),
            "*.csproj",
            SearchOption.AllDirectories);

        Assert.NotEmpty(projects);

        foreach (var project in projects)
        {
            var packageIds = ReadPackageIds(project);

            Assert.DoesNotContain(packageIds, id => id.Contains("Http", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(packageIds, id => id.Contains("Azure", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(packageIds, id => id.Contains("Aws", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(packageIds, id => id.Contains("ApplicationInsights", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(packageIds, id => id.Contains("OpenTelemetry", StringComparison.OrdinalIgnoreCase));
        }
    }

    private static string[] ReadPackageIds(string projectPath) =>
        XDocument.Load(projectPath)
            .Descendants("PackageReference")
            .Select(element => element.Attribute("Include")?.Value ?? string.Empty)
            .ToArray();

    [Fact]
    public void Source_MustNotUseNetworkClientsOrHardCodedSecrets()
    {
        var sourceRoot = Path.Combine(FindRepoRoot(), "src");
        var files = Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories);

        Assert.NotEmpty(files);

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("HttpClient", text, StringComparison.Ordinal);
            Assert.DoesNotContain("WebClient", text, StringComparison.Ordinal);
            Assert.DoesNotContain("RestClient", text, StringComparison.Ordinal);
            Assert.DoesNotContain("TelemetryClient", text, StringComparison.Ordinal);
            Assert.DoesNotContain("BEGIN RSA PRIVATE KEY", text, StringComparison.Ordinal);
        }
    }

    private static string GetInfrastructureProjectPath()
    {
        return Path.Combine(
            FindRepoRoot(),
            "src",
            "SecureBudgetManager.Infrastructure",
            "SecureBudgetManager.Infrastructure.csproj");
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

        throw new InvalidOperationException("Could not locate the repository root from the test output directory.");
    }
}
