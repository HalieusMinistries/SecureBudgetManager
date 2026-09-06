using SecureBudgetManager.Core.Security;

namespace SecureBudgetManager.Tests.Security;

public sealed class SensitiveLogGuardTests
{
    [Theory]
    [InlineData("Workspace lock requested")]
    [InlineData("Encrypted persistence is not available in this phase.")]
    public void AllowsOperationalMessages(string text)
    {
        Assert.False(SensitiveLogGuard.ContainsDisallowedContent(text));
        SensitiveLogGuard.ThrowIfDisallowed(text);
    }

    [Theory]
    [InlineData("password=letmein")]
    [InlineData("encryption key material")]
    [InlineData("connection string for the database")]
    [InlineData("card number 4111111111111111")]
    public void BlocksSensitiveLookingText(string text)
    {
        Assert.True(SensitiveLogGuard.ContainsDisallowedContent(text));
        Assert.Throws<InvalidOperationException>(() => SensitiveLogGuard.ThrowIfDisallowed(text));
    }
}
