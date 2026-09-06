using SecureBudgetManager.Core.Abstractions;

namespace SecureBudgetManager.App.Tests.Fakes;

public sealed class TempVaultDirectory : ILocalDataDirectory, IDisposable
{
    private readonly string _root;
    private bool _disposed;

    public TempVaultDirectory()
    {
        _root = Path.Combine(Path.GetTempPath(), "sbm-app-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public string GetRootPath() => _root;

    public string GetDatabasePath() => Path.Combine(_root, "household.sbmdb");

    public string GetVaultMetadataPath() => Path.Combine(_root, "vault.json");

    public string GetDefaultBackupDirectory() => Path.Combine(_root, "Backups");

    public void EnsureCreated() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }
}
