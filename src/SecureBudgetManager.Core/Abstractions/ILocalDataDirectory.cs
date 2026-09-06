namespace SecureBudgetManager.Core.Abstractions;

/// <summary>
/// Resolves on-device file locations. Must never return a network or cloud-synchronised location.
/// </summary>
public interface ILocalDataDirectory
{
    string GetRootPath();

    string GetDatabasePath();

    string GetVaultMetadataPath();

    string GetDefaultBackupDirectory();

    void EnsureCreated();
}
