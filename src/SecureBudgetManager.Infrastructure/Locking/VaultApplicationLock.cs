using Microsoft.Extensions.Logging;
using SecureBudgetManager.Core.Abstractions;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Security;

namespace SecureBudgetManager.Infrastructure.Locking;

/// <summary>
/// Conceals the workspace by closing the local database connection. This is not encryption.
/// </summary>
public sealed class VaultApplicationLock : IApplicationLock
{
    private readonly IVaultService _vault;
    private readonly ILogger<VaultApplicationLock> _logger;

    public VaultApplicationLock(IVaultService vault, ILogger<VaultApplicationLock> logger)
    {
        _vault = vault;
        _logger = logger;
    }

    public bool IsLocked => _vault.Status != VaultStatus.Unlocked;

    public ApplicationLockResult RequestLock(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (_vault.Status == VaultStatus.NotInitialised)
        {
            return new ApplicationLockResult(
                true,
                "There is nothing to conceal yet.");
        }

        if (_vault.Status != VaultStatus.Unlocked)
        {
            return new ApplicationLockResult(true, "The workspace is already locked.");
        }

        _vault.Lock();

        // The reason is an application-supplied label such as "inactivity", never user data.
        _logger.LogInformation("Workspace locked. Reason: {Reason}.", reason);

        return new ApplicationLockResult(true, "Figures are hidden. This is not database encryption.");
    }
}
