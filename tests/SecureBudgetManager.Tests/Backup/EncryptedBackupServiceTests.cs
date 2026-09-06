using System.Text;
using SecureBudgetManager.Core.Security;
using SecureBudgetManager.Infrastructure.Backup;
using SecureBudgetManager.Tests.TestSupport;

namespace SecureBudgetManager.Tests.Backup;

public sealed class EncryptedBackupServiceTests
{
    private const string Secret = "correct-horse-battery-staple";
    private static readonly byte[] PlaintextSqliteHeader = Encoding.ASCII.GetBytes("SQLite format 3\0");

    [Fact]
    public async Task BackupIsWrittenAsSqliteWithAManifest()
    {
        using var fixture = new VaultFixture();
        await fixture.InitialiseAsync(Secret);
        fixture.Store.RecordAuditEvent("household.created", "smoke test");

        var record = await fixture.Backups.CreateBackupAsync(fixture.Directory.GetDefaultBackupDirectory());

        Assert.True(File.Exists(record.BackupFilePath));
        Assert.True(File.Exists(record.ManifestFilePath));
        Assert.EndsWith(EncryptedBackupService.BackupExtension, record.BackupFilePath, StringComparison.Ordinal);

        var header = new byte[PlaintextSqliteHeader.Length];
        using (var stream = File.OpenRead(record.BackupFilePath))
        {
            stream.ReadExactly(header);
        }

        Assert.True(header.AsSpan().SequenceEqual(PlaintextSqliteHeader));
    }

    [Fact]
    public async Task ManifestRecordsNoHouseholdFigures()
    {
        using var fixture = new VaultFixture();
        await fixture.InitialiseAsync(Secret);

        var record = await fixture.Backups.CreateBackupAsync(fixture.Directory.GetDefaultBackupDirectory());
        var manifestText = await File.ReadAllTextAsync(record.ManifestFilePath);

        Assert.DoesNotContain(Secret, manifestText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(record.Manifest.Sha256, manifestText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AFreshBackupValidates()
    {
        using var fixture = new VaultFixture();
        await fixture.InitialiseAsync(Secret);

        var record = await fixture.Backups.CreateBackupAsync(fixture.Directory.GetDefaultBackupDirectory());
        var validation = await fixture.Backups.ValidateBackupAsync(record.BackupFilePath);

        Assert.True(validation.IsValid);
        Assert.Empty(validation.Problems);
    }

    [Fact]
    public async Task ATamperedBackupIsRejected()
    {
        using var fixture = new VaultFixture();
        await fixture.InitialiseAsync(Secret);

        var record = await fixture.Backups.CreateBackupAsync(fixture.Directory.GetDefaultBackupDirectory());

        var bytes = await File.ReadAllBytesAsync(record.BackupFilePath);
        bytes[^1] ^= 0xFF;
        await File.WriteAllBytesAsync(record.BackupFilePath, bytes);

        var validation = await fixture.Backups.ValidateBackupAsync(record.BackupFilePath);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Problems, problem => problem.Contains("hash", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ABackupWithoutAManifestCannotBeVerified()
    {
        using var fixture = new VaultFixture();
        await fixture.InitialiseAsync(Secret);

        var record = await fixture.Backups.CreateBackupAsync(fixture.Directory.GetDefaultBackupDirectory());
        File.Delete(record.ManifestFilePath);

        var validation = await fixture.Backups.ValidateBackupAsync(record.BackupFilePath);

        Assert.False(validation.IsValid);
    }

    [Fact]
    public async Task MissingBackupFileIsReportedClearly()
    {
        using var fixture = new VaultFixture();
        await fixture.InitialiseAsync(Secret);

        var validation = await fixture.Backups.ValidateBackupAsync(
            Path.Combine(fixture.Directory.GetRootPath(), "does-not-exist.sbmbak"));

        Assert.False(validation.IsValid);
    }

    [Fact]
    public async Task RestoreBringsBackDataRecordedBeforeTheBackup()
    {
        using var fixture = new VaultFixture();
        await fixture.InitialiseAsync(Secret);

        fixture.Store.RecordAuditEvent("household.created", "before backup");
        var record = await fixture.Backups.CreateBackupAsync(fixture.Directory.GetDefaultBackupDirectory());

        // Add something after the backup so the restore is observable.
        fixture.Store.RecordAuditEvent("household.changed", "after backup");
        Assert.Equal(2, fixture.Store.CountAuditEvents());

        await fixture.Backups.RestoreBackupAsync(record.BackupFilePath);
        fixture.Vault.Lock();

        var unlocked = await fixture.Vault.UnlockAsync(VaultFixture.Secret(Secret));

        Assert.True(unlocked.Succeeded);
        Assert.Equal(1, fixture.Store.CountAuditEvents());
    }

    [Fact]
    public async Task RestoreRefusesATamperedBackupAndKeepsTheLiveDatabase()
    {
        using var fixture = new VaultFixture();
        await fixture.InitialiseAsync(Secret);
        fixture.Store.RecordAuditEvent("household.created", "live data");

        var record = await fixture.Backups.CreateBackupAsync(fixture.Directory.GetDefaultBackupDirectory());

        var bytes = await File.ReadAllBytesAsync(record.BackupFilePath);
        bytes[^1] ^= 0xFF;
        await File.WriteAllBytesAsync(record.BackupFilePath, bytes);

        await Assert.ThrowsAsync<DatabaseCorruptException>(
            () => fixture.Backups.RestoreBackupAsync(record.BackupFilePath));

        // The live database must still be openable and intact.
        fixture.Vault.Lock();
        var unlocked = await fixture.Vault.UnlockAsync(VaultFixture.Secret(Secret));

        Assert.True(unlocked.Succeeded);
        Assert.Equal(1, fixture.Store.CountAuditEvents());
    }

    [Fact]
    public async Task BackupRequiresAnUnlockedVault()
    {
        using var fixture = new VaultFixture();
        await fixture.InitialiseAsync(Secret);
        fixture.Vault.Lock();

        await Assert.ThrowsAsync<VaultLockedException>(
            () => fixture.Backups.CreateBackupAsync(fixture.Directory.GetDefaultBackupDirectory()));
    }
}
