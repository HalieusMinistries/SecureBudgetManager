using Microsoft.Extensions.Logging;
using SecureBudgetManager.Core.Abstractions;
using SecureBudgetManager.Core.Security;

namespace SecureBudgetManager.Infrastructure.Security;

/// <summary>
/// Opens the local SQLite household database. Conceal() closes the connection so figures are not
/// shown; anyone with the Windows files can still read the database.
/// </summary>
public sealed class VaultService : IVaultService, IDisposable
{
    private readonly ILocalBudgetStore _store;
    private readonly ILogger<VaultService> _logger;
    private readonly object _gate = new();
    private VaultStatus _status = VaultStatus.Locked;
    private bool _disposed;

    public VaultService(ILocalBudgetStore store, ILogger<VaultService> logger)
    {
        _store = store;
        _logger = logger;
    }

    public VaultStatus Status => _status;

    public bool HasRecoveryKey => false;

    public bool UsesWindowsUserProtection => false;

    public bool RequiresMasterSecretUnlock => false;

    public event EventHandler<VaultStatus>? StatusChanged;

    public Task EnsureReadyAsync(CancellationToken cancellationToken = default) =>
        Task.Run(EnsureReady, cancellationToken);

    public Task<UnlockResult> RevealAsync(CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                EnsureReady();
                return UnlockResult.Success();
            },
            cancellationToken);

    public Task<VaultSetupResult> InitialiseWithWindowsUserAsync(CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                EnsureReady();
                return new VaultSetupResult(string.Empty, "Local-only storage does not use a recovery key.");
            },
            cancellationToken);

    public Task<VaultSetupResult> InitialiseAsync(
        char[] secret,
        MasterSecretKind kind,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(secret);
        _ = kind;
        return Task.Run(
            () =>
            {
                try
                {
                    EnsureReady();
                    return new VaultSetupResult(string.Empty, "Local-only storage does not use a master password.");
                }
                finally
                {
                    Array.Clear(secret);
                }
            },
            cancellationToken);
    }

    public Task<UnlockResult> UnlockWithWindowsUserAsync(CancellationToken cancellationToken = default) =>
        RevealAsync(cancellationToken);

    public Task<UnlockResult> UnlockAsync(char[] secret, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(secret);
        return Task.Run(
            () =>
            {
                try
                {
                    EnsureReady();
                    return UnlockResult.Success();
                }
                finally
                {
                    Array.Clear(secret);
                }
            },
            cancellationToken);
    }

    public Task<UnlockResult> UnlockWithRecoveryKeyAsync(
        string recoveryKey,
        CancellationToken cancellationToken = default)
    {
        _ = recoveryKey;
        return RevealAsync(cancellationToken);
    }

    public Task ChangeSecretAsync(
        char[] currentSecret,
        char[] newSecret,
        MasterSecretKind kind,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentSecret);
        ArgumentNullException.ThrowIfNull(newSecret);
        Array.Clear(currentSecret);
        Array.Clear(newSecret);
        _ = kind;
        _ = cancellationToken;
        return Task.FromException(new InvalidOperationException(
            "This programme stores data locally without a vault password."));
    }

    public void Conceal() => Lock();

    public void Lock()
    {
        lock (_gate)
        {
            _store.Close();
            SetStatus(VaultStatus.Locked);
        }

        _logger.LogInformation("Workspace concealed. The local database file remains readable on this computer.");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Lock();
        _disposed = true;
    }

    private void EnsureReady()
    {
        lock (_gate)
        {
            if (!_store.IsOpen)
            {
                _store.Open();
            }

            var integrity = _store.CheckIntegrity();
            if (!integrity.IsHealthy)
            {
                _store.Close();
                SetStatus(VaultStatus.Unavailable);
                throw new EncryptionUnavailableException(
                    "The local household database failed its integrity check. Restore a backup to continue.");
            }

            SetStatus(VaultStatus.Unlocked);
        }
    }

    private void SetStatus(VaultStatus status)
    {
        if (_status == status)
        {
            return;
        }

        _status = status;
        StatusChanged?.Invoke(this, status);
    }
}
