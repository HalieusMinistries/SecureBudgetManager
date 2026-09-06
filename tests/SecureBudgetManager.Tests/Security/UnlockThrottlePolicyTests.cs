using SecureBudgetManager.Core.Security;

namespace SecureBudgetManager.Tests.Security;

public sealed class UnlockThrottlePolicyTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void FirstAttemptsAreNotDelayed(int failures)
    {
        Assert.Equal(TimeSpan.Zero, UnlockThrottlePolicy.GetDelayAfterFailures(failures));
    }

    [Fact]
    public void DelayEscalatesAfterTheFreeAttempts()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), UnlockThrottlePolicy.GetDelayAfterFailures(3));
        Assert.Equal(TimeSpan.FromSeconds(10), UnlockThrottlePolicy.GetDelayAfterFailures(4));
        Assert.Equal(TimeSpan.FromSeconds(20), UnlockThrottlePolicy.GetDelayAfterFailures(5));
    }

    [Fact]
    public void DelayIsCapped()
    {
        Assert.Equal(UnlockThrottlePolicy.MaximumDelay, UnlockThrottlePolicy.GetDelayAfterFailures(50));
    }

    [Fact]
    public void LockoutExpires()
    {
        var now = new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero);
        var expiry = UnlockThrottlePolicy.GetLockoutExpiry(3, now);

        Assert.NotNull(expiry);
        Assert.True(UnlockThrottlePolicy.IsLockedOut(expiry, now));
        Assert.False(UnlockThrottlePolicy.IsLockedOut(expiry, now.AddSeconds(6)));
    }

    [Fact]
    public void NoLockoutForFewFailures()
    {
        var now = DateTimeOffset.UnixEpoch;

        Assert.Null(UnlockThrottlePolicy.GetLockoutExpiry(1, now));
    }

    [Fact]
    public void NegativeFailureCountIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => UnlockThrottlePolicy.GetDelayAfterFailures(-1));
    }
}
