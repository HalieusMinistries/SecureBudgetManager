using SecureBudgetManager.Core.Storage;

namespace SecureBudgetManager.Core.Abstractions;

/// <summary>
/// Local SQLite backup with a hash manifest. Backups contain household information and are never uploaded.
/// </summary>
public interface ISecureBackupService
{
    Task<BackupRecord> CreateBackupAsync(string destinationDirectory, CancellationToken cancellationToken = default);

    Task<BackupValidationResult> ValidateBackupAsync(string backupFilePath, CancellationToken cancellationToken = default);

    /// <summary>Validates the backup, then replaces the live database, keeping a rollback copy.</summary>
    Task RestoreBackupAsync(string backupFilePath, CancellationToken cancellationToken = default);
}
