namespace SecureBudgetManager.Core.Security;

/// <summary>
/// Legacy vault metadata kept so old encrypted recovery files can still be identified.
/// The running programme no longer uses a vault password.
/// </summary>
public sealed record VaultMetadata
{
    public const int CurrentFormatVersion = 2;

    public int FormatVersion { get; init; } = 1;

    public MasterSecretKind SecretKind { get; init; } = MasterSecretKind.Password;

    public Argon2idParameters KeyDerivation { get; init; } = Argon2idParameters.Current;

    public KeyEnvelope? MasterEnvelope { get; init; }

    public KeyEnvelope? RecoveryEnvelope { get; init; }

    public DateTimeOffset CreatedUtc { get; init; }

    public DateTimeOffset SecretChangedUtc { get; init; }

    public int ConsecutiveFailedUnlocks { get; init; }

    public DateTimeOffset? LockoutUntilUtc { get; init; }

    public bool HasRecoveryKey => RecoveryEnvelope is not null;

    public void Validate()
    {
        if (FormatVersion < 1)
        {
            throw new VaultFormatException(
                $"This vault metadata format is not readable (format {FormatVersion}).");
        }

        KeyDerivation.Validate();
        MasterEnvelope?.Validate();
        RecoveryEnvelope?.Validate();
    }
}
