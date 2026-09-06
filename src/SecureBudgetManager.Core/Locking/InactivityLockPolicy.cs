namespace SecureBudgetManager.Core.Locking;

/// <summary>
/// Decides when an idle session must be locked. Pure logic so it can be tested without a UI timer.
/// </summary>
public sealed class InactivityLockPolicy
{
    public static readonly TimeSpan MinimumTimeout = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MaximumTimeout = TimeSpan.FromHours(2);
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(10);

    public InactivityLockPolicy(TimeSpan timeout)
    {
        Timeout = Clamp(timeout);
    }

    public TimeSpan Timeout { get; }

    public static TimeSpan Clamp(TimeSpan timeout)
    {
        if (timeout < MinimumTimeout)
        {
            return MinimumTimeout;
        }

        return timeout > MaximumTimeout ? MaximumTimeout : timeout;
    }

    public bool ShouldLock(DateTimeOffset lastActivityUtc, DateTimeOffset nowUtc) =>
        GetIdleTime(lastActivityUtc, nowUtc) >= Timeout;

    public TimeSpan GetTimeUntilLock(DateTimeOffset lastActivityUtc, DateTimeOffset nowUtc)
    {
        var remaining = Timeout - GetIdleTime(lastActivityUtc, nowUtc);
        return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
    }

    private static TimeSpan GetIdleTime(DateTimeOffset lastActivityUtc, DateTimeOffset nowUtc)
    {
        var idle = nowUtc - lastActivityUtc;

        // A backwards clock change must never extend the session.
        return idle < TimeSpan.Zero ? TimeSpan.Zero : idle;
    }
}
