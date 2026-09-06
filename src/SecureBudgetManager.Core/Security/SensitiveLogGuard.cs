using System.Text.RegularExpressions;

namespace SecureBudgetManager.Core.Security;

/// <summary>
/// Blocks log text that appears to contain secrets or financial account data.
/// This is a last-line safety check; callers must still never write sensitive values to logs.
/// </summary>
public static class SensitiveLogGuard
{
    private static readonly Regex LongDigitRun = new(@"\b\d{13,19}\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly string[] DisallowedTokens =
    [
        "password",
        "passphrase",
        "private key",
        "connection string",
        "encryption key",
        "secret key",
        "api key",
        "bank account",
        "routing number",
        "card number"
    ];

    public static bool ContainsDisallowedContent(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (DisallowedTokens.Any(token => text.Contains(token, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return LongDigitRun.IsMatch(text);
    }

    public static void ThrowIfDisallowed(string? text)
    {
        if (ContainsDisallowedContent(text))
        {
            throw new InvalidOperationException("Refusing to process text that appears to contain sensitive data.");
        }
    }
}
