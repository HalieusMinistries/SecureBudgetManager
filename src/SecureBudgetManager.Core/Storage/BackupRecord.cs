namespace SecureBudgetManager.Core.Storage;

/// <summary>
/// Manifest for a local database backup. Holds no household figures: only a file hash, sizes and versions.
/// </summary>
public sealed record BackupManifest
{
    public const int CurrentVersion = 1;

    public int ManifestVersion { get; init; } = CurrentVersion;

    public required string FileName { get; init; }

    public required string Sha256 { get; init; }

    public long SizeBytes { get; init; }

    public int SchemaVersion { get; init; }

    public DateTimeOffset CreatedUtc { get; init; }
}

public sealed record BackupRecord(string BackupFilePath, string ManifestFilePath, BackupManifest Manifest);

public sealed record BackupValidationResult(bool IsValid, IReadOnlyList<string> Problems, BackupManifest? Manifest)
{
    public static BackupValidationResult Valid(BackupManifest manifest) => new(true, [], manifest);

    public static BackupValidationResult Invalid(params string[] problems) => new(false, problems, null);
}
