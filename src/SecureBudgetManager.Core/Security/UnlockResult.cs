namespace SecureBudgetManager.Core.Security;

public enum UnlockFailureReason
{
    None = 0,
    IncorrectSecret = 1,
    ThrottledTryLater = 2,
    NotInitialised = 3,
    NoRecoveryKeyConfigured = 4,
    EncryptionUnavailable = 5,
    DatabaseCorrupt = 6,
    WindowsUserUnavailable = 7
}

public sealed record UnlockResult(
    bool Succeeded,
    UnlockFailureReason Reason,
    string Message,
    TimeSpan RetryAfter)
{
    public static UnlockResult Success() =>
        new(true, UnlockFailureReason.None, "Vault unlocked.", TimeSpan.Zero);

    public static UnlockResult Failure(UnlockFailureReason reason, string message, TimeSpan retryAfter = default) =>
        new(false, reason, message, retryAfter);
}
