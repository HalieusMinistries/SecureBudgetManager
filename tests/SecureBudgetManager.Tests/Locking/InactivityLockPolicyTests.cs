using SecureBudgetManager.Core.Locking;

namespace SecureBudgetManager.Tests.Locking;

public sealed class InactivityLockPolicyTests
{
    private static readonly DateTimeOffset Start = new(2026, 5, 4, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void DoesNotLockBeforeTheTimeout()
    {
        var policy = new InactivityLockPolicy(TimeSpan.FromMinutes(10));

        Assert.False(policy.ShouldLock(Start, Start.AddMinutes(9)));
    }

    [Fact]
    public void LocksOnceTheTimeoutIsReached()
    {
        var policy = new InactivityLockPolicy(TimeSpan.FromMinutes(10));

        Assert.True(policy.ShouldLock(Start, Start.AddMinutes(10)));
        Assert.True(policy.ShouldLock(Start, Start.AddHours(3)));
    }

    [Fact]
    public void ClampsUnreasonableTimeouts()
    {
        Assert.Equal(InactivityLockPolicy.MinimumTimeout, new InactivityLockPolicy(TimeSpan.Zero).Timeout);
        Assert.Equal(InactivityLockPolicy.MaximumTimeout, new InactivityLockPolicy(TimeSpan.FromDays(1)).Timeout);
        Assert.Equal(
            InactivityLockPolicy.MinimumTimeout,
            new InactivityLockPolicy(TimeSpan.FromMinutes(-5)).Timeout);
    }

    [Fact]
    public void ReportsTimeRemaining()
    {
        var policy = new InactivityLockPolicy(TimeSpan.FromMinutes(10));

        Assert.Equal(TimeSpan.FromMinutes(4), policy.GetTimeUntilLock(Start, Start.AddMinutes(6)));
        Assert.Equal(TimeSpan.Zero, policy.GetTimeUntilLock(Start, Start.AddMinutes(30)));
    }

    [Fact]
    public void ClockGoingBackwardsDoesNotExtendTheSession()
    {
        var policy = new InactivityLockPolicy(TimeSpan.FromMinutes(10));

        // A backwards jump must not be treated as negative idle time.
        Assert.False(policy.ShouldLock(Start, Start.AddMinutes(-30)));
        Assert.Equal(policy.Timeout, policy.GetTimeUntilLock(Start, Start.AddMinutes(-30)));
    }
}
