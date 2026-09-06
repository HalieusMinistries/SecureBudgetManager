namespace SecureBudgetManager.Core.Security;

public sealed record MasterSecretValidation(bool IsAcceptable, string? ErrorMessage, string? Warning)
{
    public static MasterSecretValidation Accepted(string? warning = null) => new(true, null, warning);

    public static MasterSecretValidation Rejected(string errorMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new(false, errorMessage, null);
    }
}

/// <summary>
/// Strength rules for the master secret. No secret value is ever retained or logged by this type.
/// </summary>
public static class MasterSecretPolicy
{
    public const int MinimumPasswordLength = 12;
    public const int MinimumPinLength = 8;

    private static readonly string[] UnacceptableSecrets =
    [
        "password",
        "passw0rd",
        "123456789012",
        "qwertyuiop",
        "letmeinplease",
        "securebudget",
        "budgetmanager"
    ];

    public static MasterSecretValidation Validate(ReadOnlySpan<char> secret, MasterSecretKind kind)
    {
        if (secret.IsEmpty)
        {
            return MasterSecretValidation.Rejected("Enter a master secret.");
        }

        if (secret.IndexOf('\0') >= 0)
        {
            return MasterSecretValidation.Rejected("The master secret cannot contain a null character.");
        }

        return kind switch
        {
            MasterSecretKind.Pin => ValidatePin(secret),
            MasterSecretKind.Password => ValidatePassword(secret),
            _ => MasterSecretValidation.Rejected("Unknown master secret kind.")
        };
    }

    private static MasterSecretValidation ValidatePin(ReadOnlySpan<char> secret)
    {
        if (secret.Length < MinimumPinLength)
        {
            return MasterSecretValidation.Rejected($"A PIN must be at least {MinimumPinLength} digits.");
        }

        foreach (var character in secret)
        {
            if (!char.IsAsciiDigit(character))
            {
                return MasterSecretValidation.Rejected("A PIN may contain digits only. Choose a password instead.");
            }
        }

        if (IsSingleRepeatedCharacter(secret) || IsSequentialRun(secret))
        {
            return MasterSecretValidation.Rejected("Choose a PIN that is not a repeated or sequential run of digits.");
        }

        return MasterSecretValidation.Accepted(
            "A digits-only PIN is far weaker than a passphrase. Anyone who copies the database file has unlimited offline guesses.");
    }

    private static MasterSecretValidation ValidatePassword(ReadOnlySpan<char> secret)
    {
        if (secret.Length < MinimumPasswordLength)
        {
            return MasterSecretValidation.Rejected($"A master password must be at least {MinimumPasswordLength} characters.");
        }

        foreach (var candidate in UnacceptableSecrets)
        {
            if (secret.Equals(candidate, StringComparison.OrdinalIgnoreCase))
            {
                return MasterSecretValidation.Rejected("That master password is too easy to guess.");
            }
        }

        if (IsSingleRepeatedCharacter(secret))
        {
            return MasterSecretValidation.Rejected("A master password cannot be one repeated character.");
        }

        var distinct = CountDistinct(secret);
        if (distinct < 5)
        {
            return MasterSecretValidation.Rejected("Use a master password with more variety of characters.");
        }

        return distinct < 10
            ? MasterSecretValidation.Accepted("Consider a longer passphrase of several unrelated words.")
            : MasterSecretValidation.Accepted();
    }

    private static bool IsSingleRepeatedCharacter(ReadOnlySpan<char> secret)
    {
        for (var i = 1; i < secret.Length; i++)
        {
            if (secret[i] != secret[0])
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSequentialRun(ReadOnlySpan<char> secret)
    {
        var ascending = true;
        var descending = true;

        for (var i = 1; i < secret.Length; i++)
        {
            if (secret[i] != secret[i - 1] + 1)
            {
                ascending = false;
            }

            if (secret[i] != secret[i - 1] - 1)
            {
                descending = false;
            }
        }

        return ascending || descending;
    }

    private static int CountDistinct(ReadOnlySpan<char> secret)
    {
        var seen = new HashSet<char>();
        foreach (var character in secret)
        {
            seen.Add(character);
        }

        return seen.Count;
    }
}
