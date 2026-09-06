using SecureBudgetManager.Core.Models;

namespace SecureBudgetManager.Core.Abstractions;

/// <summary>
/// Locks the workspace, discarding the in-memory database key.
/// </summary>
public interface IApplicationLock
{
    bool IsLocked { get; }

    ApplicationLockResult RequestLock(string reason);
}
