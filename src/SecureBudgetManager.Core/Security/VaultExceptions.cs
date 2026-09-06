namespace SecureBudgetManager.Core.Security;

/// <summary>
/// Base type for vault failures. Messages must never contain secrets or financial values.
/// </summary>
public abstract class VaultException : Exception
{
    protected VaultException(string message)
        : base(message)
    {
    }

    protected VaultException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Thrown when encryption cannot be guaranteed. The application must fail closed rather than continue.
/// </summary>
public sealed class EncryptionUnavailableException : VaultException
{
    public EncryptionUnavailableException(string message)
        : base(message)
    {
    }

    public EncryptionUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class VaultFormatException : VaultException
{
    public VaultFormatException(string message)
        : base(message)
    {
    }
}

public sealed class VaultLockedException : VaultException
{
    public VaultLockedException()
        : base("The vault is locked. Unlock it before reading or writing household data.")
    {
    }
}

public sealed class DatabaseCorruptException : VaultException
{
    public DatabaseCorruptException(string message)
        : base(message)
    {
    }
}
