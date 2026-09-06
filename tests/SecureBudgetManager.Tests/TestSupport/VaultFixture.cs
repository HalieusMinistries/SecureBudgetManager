using Microsoft.Extensions.Logging.Abstractions;
using SecureBudgetManager.Core.Abstractions;
using SecureBudgetManager.Infrastructure.Backup;
using SecureBudgetManager.Infrastructure.Security;
using SecureBudgetManager.Infrastructure.Storage;

namespace SecureBudgetManager.Tests.TestSupport;

/// <summary>
/// Builds a real local SQLite store against a throwaway directory.
/// </summary>
public sealed class VaultFixture : IDisposable
{
    private bool _disposed;

    public VaultFixture()
    {
        Directory = new TempVaultDirectory();
        Time = new FixedTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        MetadataRepository = new VaultMetadataRepository(Directory);
        Store = new SqliteBudgetStore(Directory, NullLogger<SqliteBudgetStore>.Instance);
        Vault = new VaultService(Store, NullLogger<VaultService>.Instance);
        Backups = new EncryptedBackupService(
            Store,
            Directory,
            NullLogger<EncryptedBackupService>.Instance,
            Time);
    }

    public TempVaultDirectory Directory { get; }

    public FixedTimeProvider Time { get; }

    public VaultMetadataRepository MetadataRepository { get; }

    public SqliteBudgetStore Store { get; }

    public VaultService Vault { get; }

    public EncryptedBackupService Backups { get; }

    public static char[] Secret(string text) => text.ToCharArray();

    public async Task<VaultSetupResult> InitialiseAsync(string secret = "unused-local-store")
    {
        _ = secret;
        await Vault.EnsureReadyAsync();
        return new VaultSetupResult(string.Empty, null);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Vault.Dispose();
        Store.Dispose();
        Directory.Dispose();
        _disposed = true;
    }
}
