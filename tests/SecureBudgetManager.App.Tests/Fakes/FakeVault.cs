using SecureBudgetManager.Core.Abstractions;
using SecureBudgetManager.Core.Security;

namespace SecureBudgetManager.App.Tests.Fakes;

public sealed class FakeVault : IVaultService
{
    public VaultStatus Status { get; set; } = VaultStatus.Unlocked;

    public bool HasRecoveryKey => false;

    public bool UsesWindowsUserProtection => false;

    public bool RequiresMasterSecretUnlock => false;

    public event EventHandler<VaultStatus>? StatusChanged;

    public Task EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        Status = VaultStatus.Unlocked;
        StatusChanged?.Invoke(this, Status);
        return Task.CompletedTask;
    }

    public Task<UnlockResult> RevealAsync(CancellationToken cancellationToken = default)
    {
        Status = VaultStatus.Unlocked;
        StatusChanged?.Invoke(this, Status);
        return Task.FromResult(UnlockResult.Success());
    }

    public void Conceal() => Lock();

    public Task<VaultSetupResult> InitialiseWithWindowsUserAsync(CancellationToken cancellationToken = default)
    {
        Status = VaultStatus.Unlocked;
        StatusChanged?.Invoke(this, Status);
        return Task.FromResult(new VaultSetupResult(string.Empty, "Local-only storage does not use a recovery key."));
    }

    public Task<VaultSetupResult> InitialiseAsync(
        char[] secret,
        MasterSecretKind kind,
        CancellationToken cancellationToken = default)
    {
        Array.Clear(secret);
        _ = kind;
        Status = VaultStatus.Unlocked;
        StatusChanged?.Invoke(this, Status);
        return Task.FromResult(new VaultSetupResult(string.Empty, "Local-only storage does not use a master password."));
    }

    public Task<UnlockResult> UnlockWithWindowsUserAsync(CancellationToken cancellationToken = default) =>
        RevealAsync(cancellationToken);

    public Task<UnlockResult> UnlockAsync(char[] secret, CancellationToken cancellationToken = default)
    {
        Array.Clear(secret);
        Status = VaultStatus.Unlocked;
        StatusChanged?.Invoke(this, Status);
        return Task.FromResult(UnlockResult.Success());
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
        Array.Clear(currentSecret);
        Array.Clear(newSecret);
        return Task.FromException(new InvalidOperationException(
            "This programme stores data locally without a vault password."));
    }

    public void Lock()
    {
        Status = VaultStatus.Locked;
        StatusChanged?.Invoke(this, Status);
    }
}
