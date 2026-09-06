namespace SecureBudgetManager.Core.Security;

/// <summary>
/// Escalating delay applied after failed unlock attempts.
/// There is deliberately no permanent lockout: the household must never be locked out of its own data,
/// and offline brute force is bounded by Argon2id rather than by this policy.
/// </summary>
public static class UnlockThrottlePolicy
{
    public const int FreeAttempts = 2;

    public static readonly TimeSpan MaximumDelay = TimeSpan.FromMinutes(15);

    public static TimeSpan GetDelayAfterFailures(int consecutiveFailures)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(consecutiveFailures);

        if (consecutiveFailures <= FreeAttempts)
        {
            return TimeSpan.Zero;
        }

        var step = consecutiveFailures - FreeAttempts;
        var seconds = 5d * Math.Pow(2, step - 1);

        // Compare before constructing the TimeSpan: a large failure count overflows the double.
        if (double.IsInfinity(seconds) || seconds >= MaximumDelay.TotalSeconds)
        {
            return MaximumDelay;
        }

        return TimeSpan.FromSeconds(seconds);
    }

    public static DateTimeOffset? GetLockoutExpiry(int consecutiveFailures, DateTimeOffset failedAtUtc)
    {
        var delay = GetDelayAfterFailures(consecutiveFailures);
        return delay == TimeSpan.Zero ? null : failedAtUtc + delay;
    }

    public static TimeSpan GetRemainingLockout(DateTimeOffset? lockoutUntilUtc, DateTimeOffset nowUtc)
    {
        if (lockoutUntilUtc is null || lockoutUntilUtc <= nowUtc)
        {
            return TimeSpan.Zero;
        }

        return lockoutUntilUtc.Value - nowUtc;
    }

    public static bool IsLockedOut(DateTimeOffset? lockoutUntilUtc, DateTimeOffset nowUtc) =>
        GetRemainingLockout(lockoutUntilUtc, nowUtc) > TimeSpan.Zero;
}
