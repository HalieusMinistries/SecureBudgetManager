using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SecureBudgetManager.Core.Abstractions;
using SecureBudgetManager.Core.Security;
using SecureBudgetManager.Core.Storage;
using SecureBudgetManager.Infrastructure.Storage;

namespace SecureBudgetManager.Infrastructure.Backup;

/// <summary>
/// Creates a local SQLite backup with a hash manifest. The copy contains household information
/// and is not encrypted. Nothing is uploaded.
/// </summary>
public sealed class EncryptedBackupService : ISecureBackupService
{
    public const string BackupExtension = ".sbmbak";
    public const string ManifestExtension = ".manifest.json";

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly ILocalBudgetStore _store;
    private readonly ILocalDataDirectory _directory;
    private readonly ILogger<EncryptedBackupService> _logger;
    private readonly TimeProvider _timeProvider;

    public EncryptedBackupService(
        ILocalBudgetStore store,
        ILocalDataDirectory directory,
        ILogger<EncryptedBackupService> logger,
        TimeProvider timeProvider)
    {
        _store = store;
        _directory = directory;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public Task<BackupRecord> CreateBackupAsync(
        string destinationDirectory,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => CreateBackup(destinationDirectory), cancellationToken);

    public Task<BackupValidationResult> ValidateBackupAsync(
        string backupFilePath,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => ValidateBackup(backupFilePath), cancellationToken);

    public Task RestoreBackupAsync(string backupFilePath, CancellationToken cancellationToken = default) =>
        Task.Run(() => RestoreBackup(backupFilePath), cancellationToken);

    private BackupRecord CreateBackup(string destinationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);

        if (_store is not SqliteBudgetStore sqliteStore || !_store.IsOpen)
        {
            throw new VaultLockedException();
        }

        Directory.CreateDirectory(destinationDirectory);

        var timestamp = _timeProvider.GetUtcNow();
        var fileName = FormattableString.Invariant(
            $"household-{timestamp:yyyyMMdd-HHmmss}{BackupExtension}");
        var backupPath = Path.Combine(destinationDirectory, fileName);

        if (File.Exists(backupPath))
        {
            File.Delete(backupPath);
        }

        sqliteStore.VacuumInto(backupPath);
        VerifyBackupIsSqlite(backupPath);

        var manifest = new BackupManifest
        {
            ManifestVersion = BackupManifest.CurrentVersion,
            FileName = fileName,
            Sha256 = ComputeSha256(backupPath),
            SizeBytes = new FileInfo(backupPath).Length,
            SchemaVersion = _store.SchemaVersion,
            CreatedUtc = timestamp
        };

        var manifestPath = backupPath + ManifestExtension;
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, SerializerOptions));

        // Size and schema version only. No household figures are recorded.
        _logger.LogInformation(
            "Local database backup written. Schema version: {SchemaVersion}. Size: {SizeBytes} bytes.",
            manifest.SchemaVersion,
            manifest.SizeBytes);

        return new BackupRecord(backupPath, manifestPath, manifest);
    }

    private BackupValidationResult ValidateBackup(string backupFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupFilePath);

        if (!File.Exists(backupFilePath))
        {
            return BackupValidationResult.Invalid("The backup file does not exist.");
        }

        var manifestPath = backupFilePath + ManifestExtension;
        if (!File.Exists(manifestPath))
        {
            return BackupValidationResult.Invalid("The backup manifest is missing, so the backup cannot be verified.");
        }

        BackupManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<BackupManifest>(
                File.ReadAllText(manifestPath),
                SerializerOptions);
        }
        catch (JsonException)
        {
            return BackupValidationResult.Invalid("The backup manifest is not readable.");
        }

        if (manifest is null)
        {
            return BackupValidationResult.Invalid("The backup manifest is empty.");
        }

        var problems = new List<string>();

        if (manifest.ManifestVersion > BackupManifest.CurrentVersion)
        {
            problems.Add("The backup was written by a newer version of Secure Budget Manager.");
        }

        var actualSize = new FileInfo(backupFilePath).Length;
        if (actualSize != manifest.SizeBytes)
        {
            problems.Add("The backup file size does not match its manifest.");
        }

        if (!string.Equals(ComputeSha256(backupFilePath), manifest.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            problems.Add("The backup file hash does not match its manifest, so the file has changed or is damaged.");
        }

        if (manifest.SchemaVersion > BudgetSchema.LatestVersion)
        {
            problems.Add("The backup uses a newer database schema than this build understands.");
        }

        if (!IsSqliteDatabase(backupFilePath))
        {
            problems.Add("The backup file is not a readable SQLite database and will not be restored.");
        }

        return problems.Count == 0
            ? BackupValidationResult.Valid(manifest)
            : new BackupValidationResult(false, problems, manifest);
    }

    private void RestoreBackup(string backupFilePath)
    {
        var validation = ValidateBackup(backupFilePath);
        if (!validation.IsValid)
        {
            throw new DatabaseCorruptException(
                "The backup failed validation and was not restored: " + string.Join(" ", validation.Problems));
        }

        var databasePath = _store.DatabasePath;
        var rollbackPath = databasePath + ".rollback";

        _store.Close();

        if (File.Exists(rollbackPath))
        {
            File.Delete(rollbackPath);
        }

        if (File.Exists(databasePath))
        {
            File.Move(databasePath, rollbackPath);
        }

        try
        {
            File.Copy(backupFilePath, databasePath, overwrite: false);
            DeleteSidecarFiles(databasePath);
            _store.Open();

            _logger.LogInformation(
                "Local database backup restored. Schema version: {SchemaVersion}.",
                validation.Manifest?.SchemaVersion);
        }
        catch
        {
            if (File.Exists(rollbackPath))
            {
                if (File.Exists(databasePath))
                {
                    File.Delete(databasePath);
                }

                File.Move(rollbackPath, databasePath);
            }

            throw;
        }
    }

    private static void DeleteSidecarFiles(string databasePath)
    {
        // A restored database must not inherit the previous write-ahead log.
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            var sidecar = databasePath + suffix;
            if (File.Exists(sidecar))
            {
                File.Delete(sidecar);
            }
        }
    }

    private static void VerifyBackupIsSqlite(string backupPath)
    {
        if (IsSqliteDatabase(backupPath))
        {
            return;
        }

        throw new EncryptionUnavailableException(
            "The backup is not a readable SQLite database, so it was not kept as a household backup.");
    }

    private static bool IsSqliteDatabase(string path)
    {
        const string PlaintextHeader = "SQLite format 3\0";

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        var header = new byte[PlaintextHeader.Length];
        var read = stream.Read(header, 0, header.Length);
        if (read < header.Length)
        {
            return false;
        }

        for (var i = 0; i < PlaintextHeader.Length; i++)
        {
            if (header[i] != (byte)PlaintextHeader[i])
            {
                return false;
            }
        }

        return true;
    }

    private static string ComputeSha256(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }
}
