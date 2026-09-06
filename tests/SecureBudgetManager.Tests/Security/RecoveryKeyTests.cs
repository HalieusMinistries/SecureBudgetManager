using SecureBudgetManager.Core.Security;

namespace SecureBudgetManager.Tests.Security;

public sealed class RecoveryKeyTests
{
    [Fact]
    public void GeneratesFullEntropyKeys()
    {
        var first = RecoveryKey.Generate();
        var second = RecoveryKey.Generate();

        Assert.Equal(RecoveryKey.ByteLength, first.Length);
        Assert.False(first.AsSpan().SequenceEqual(second));
    }

    [Fact]
    public void FormatThenParseRoundTrips()
    {
        var material = RecoveryKey.Generate();
        var formatted = RecoveryKey.Format(material);

        Assert.True(RecoveryKey.TryParse(formatted, out var parsed));
        Assert.True(material.AsSpan().SequenceEqual(parsed));
    }

    [Fact]
    public void FormatUsesReadableGroups()
    {
        var formatted = RecoveryKey.Format(new byte[RecoveryKey.ByteLength]);

        Assert.Contains('-', formatted);
        Assert.DoesNotContain(' ', formatted);
    }

    [Fact]
    public void ParseIgnoresSpacingAndCase()
    {
        var material = RecoveryKey.Generate();
        var formatted = RecoveryKey.Format(material);
        var messy = "  " + formatted.Replace("-", " ", StringComparison.Ordinal).ToLowerInvariant() + "  ";

        Assert.True(RecoveryKey.TryParse(messy, out var parsed));
        Assert.True(material.AsSpan().SequenceEqual(parsed));
    }

    [Fact]
    public void ParseAcceptsCrockfordSubstitutions()
    {
        // O is read as zero and I/L as one, so a mis-transcribed key still opens the vault.
        var material = new byte[RecoveryKey.ByteLength];
        var formatted = RecoveryKey.Format(material);
        var substituted = formatted.Replace('0', 'O');

        Assert.True(RecoveryKey.TryParse(substituted, out var parsed));
        Assert.True(material.AsSpan().SequenceEqual(parsed));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("too-short")]
    [InlineData("!!!!!-!!!!!-!!!!!-!!!!!-!!!!!-!!!!!-!!!!!-!!!!!-!!!!!-!!!!!-!!!!!")]
    public void RejectsMalformedKeys(string? text)
    {
        Assert.False(RecoveryKey.TryParse(text, out _));
    }

    [Fact]
    public void FormatRejectsWrongLength()
    {
        Assert.Throws<ArgumentException>(() => RecoveryKey.Format(new byte[8]));
    }
}
