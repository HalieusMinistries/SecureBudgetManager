using SecureBudgetManager.Core.Security;

namespace SecureBudgetManager.Tests.Security;

public sealed class MasterSecretPolicyTests
{
    [Fact]
    public void AcceptsAStrongPassphrase()
    {
        var result = MasterSecretPolicy.Validate("correct-horse-battery-staple", MasterSecretKind.Password);

        Assert.True(result.IsAcceptable);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void RejectsShortPassword()
    {
        var result = MasterSecretPolicy.Validate("short1!", MasterSecretKind.Password);

        Assert.False(result.IsAcceptable);
        Assert.Contains("at least 12", result.ErrorMessage);
    }

    [Fact]
    public void RejectsWellKnownPassword()
    {
        var result = MasterSecretPolicy.Validate("password", MasterSecretKind.Password);

        Assert.False(result.IsAcceptable);
    }

    [Fact]
    public void RejectsRepeatedCharacterPassword()
    {
        var result = MasterSecretPolicy.Validate("aaaaaaaaaaaaaaaa", MasterSecretKind.Password);

        Assert.False(result.IsAcceptable);
    }

    [Fact]
    public void RejectsLowVarietyPassword()
    {
        var result = MasterSecretPolicy.Validate("abababababab", MasterSecretKind.Password);

        Assert.False(result.IsAcceptable);
    }

    [Fact]
    public void AcceptsLongPinButWarnsItIsWeaker()
    {
        var result = MasterSecretPolicy.Validate("90271845", MasterSecretKind.Pin);

        Assert.True(result.IsAcceptable);
        Assert.False(string.IsNullOrWhiteSpace(result.Warning));
    }

    [Fact]
    public void RejectsShortPin()
    {
        var result = MasterSecretPolicy.Validate("1234", MasterSecretKind.Pin);

        Assert.False(result.IsAcceptable);
    }

    [Fact]
    public void RejectsSequentialPin()
    {
        var result = MasterSecretPolicy.Validate("12345678", MasterSecretKind.Pin);

        Assert.False(result.IsAcceptable);
    }

    [Fact]
    public void RejectsNonDigitsInPinMode()
    {
        var result = MasterSecretPolicy.Validate("1234abcd", MasterSecretKind.Pin);

        Assert.False(result.IsAcceptable);
    }

    [Fact]
    public void RejectsEmptySecret()
    {
        var result = MasterSecretPolicy.Validate(string.Empty, MasterSecretKind.Password);

        Assert.False(result.IsAcceptable);
    }
}
