using SecureBudgetManager.Core.Abstractions;

namespace SecureBudgetManager.Infrastructure.Storage;

/// <summary>
/// Resolves per-user local paths under LocalApplicationData, which is not roamed or cloud-synchronised.
/// </summary>
public sealed class LocalDataDirectory : ILocalDataDirectory
{
    public const string ApplicationFolderName = "SecureBudgetManager";
    public const string DatabaseFileName = "household.sbmdb";
    public const string VaultMetadataFileName = "vault.json";
    public const string BackupFolderName = "Backups";

    public const string DataRootEnvironmentVariable = "SECURE_BUDGET_MANAGER_DATA_ROOT";

    public string GetRootPath()
    {
        var overrideRoot = Environment.GetEnvironmentVariable(DataRootEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overrideRoot))
        {
            return Path.GetFullPath(overrideRoot);
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            throw new InvalidOperationException("The local application data folder is not available on this computer.");
        }

        return Path.Combine(localAppData, ApplicationFolderName);
    }

    public string GetDatabasePath() => Path.Combine(GetRootPath(), DatabaseFileName);

    public string GetVaultMetadataPath() => Path.Combine(GetRootPath(), VaultMetadataFileName);

    public string GetDefaultBackupDirectory() => Path.Combine(GetRootPath(), BackupFolderName);

    public void EnsureCreated() => Directory.CreateDirectory(GetRootPath());
}
