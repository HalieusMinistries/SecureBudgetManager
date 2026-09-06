namespace SecureBudgetManager.Core.Security;

public enum VaultStatus
{
    /// <summary>No vault exists yet; the household must create a master secret.</summary>
    NotInitialised = 0,

    /// <summary>A vault exists but no key is held in memory.</summary>
    Locked = 1,

    /// <summary>The database key is held in memory and the database is open.</summary>
    Unlocked = 2,

    /// <summary>Encryption could not be guaranteed. No data may be read or written.</summary>
    Unavailable = 3
}
