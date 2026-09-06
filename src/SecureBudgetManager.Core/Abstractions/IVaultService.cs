using SecureBudgetManager.Core.Security;

namespace SecureBudgetManager.Core.Abstractions;

public sealed record VaultSetupResult(string RecoveryKey, string? Warning);

/// <summary>
/// Opens and conceals the local household database. Concealment hides the workspace; it is not
/// database encryption.
/// </summary>
public interface IVaultService
{
    VaultStatus Status { get; }

    bool HasRecoveryKey { get; }

    bool UsesWindowsUserProtection { get; }

    bool RequiresMasterSecretUnlock { get; }

    event EventHandler<VaultStatus>? StatusChanged;

    Task EnsureReadyAsync(CancellationToken cancellationToken = default);

    Task<UnlockResult> RevealAsync(CancellationToken cancellationToken = default);

    void Conceal();

    Task<VaultSetupResult> InitialiseWithWindowsUserAsync(CancellationToken cancellationToken = default);

    Task<VaultSetupResult> InitialiseAsync(char[] secret, MasterSecretKind kind, CancellationToken cancellationToken = default);

    Task<UnlockResult> UnlockWithWindowsUserAsync(CancellationToken cancellationToken = default);

    Task<UnlockResult> UnlockAsync(char[] secret, CancellationToken cancellationToken = default);

    Task<UnlockResult> UnlockWithRecoveryKeyAsync(string recoveryKey, CancellationToken cancellationToken = default);

    Task ChangeSecretAsync(char[] currentSecret, char[] newSecret, MasterSecretKind kind, CancellationToken cancellationToken = default);

    void Lock();
}
