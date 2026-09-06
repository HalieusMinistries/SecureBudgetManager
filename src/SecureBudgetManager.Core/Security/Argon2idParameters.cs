namespace SecureBudgetManager.Core.Security;

/// <summary>
/// Argon2id cost parameters. Stored with the vault so they can be raised later without breaking existing databases.
/// </summary>
public sealed record Argon2idParameters
{
    public const int CurrentVersion = 1;

    public static Argon2idParameters Current { get; } = new()
    {
        Version = CurrentVersion,
        MemoryKibibytes = 65536,
        Iterations = 3,
        DegreeOfParallelism = 2,
        SaltLength = 16,
        KeyLength = 32
    };

    public int Version { get; init; } = CurrentVersion;

    public int MemoryKibibytes { get; init; } = 65536;

    public int Iterations { get; init; } = 3;

    public int DegreeOfParallelism { get; init; } = 2;

    public int SaltLength { get; init; } = 16;

    public int KeyLength { get; init; } = 32;

    public void Validate()
    {
        if (MemoryKibibytes < 8192)
        {
            throw new InvalidOperationException("Argon2id memory cost is too low for a master password.");
        }

        if (Iterations < 2)
        {
            throw new InvalidOperationException("Argon2id iteration count is too low for a master password.");
        }

        if (DegreeOfParallelism < 1)
        {
            throw new InvalidOperationException("Argon2id parallelism must be at least one.");
        }

        if (SaltLength < 16)
        {
            throw new InvalidOperationException("Argon2id salt must be at least 16 bytes.");
        }

        if (KeyLength != 32)
        {
            throw new InvalidOperationException("A 32-byte derived key is required.");
        }
    }
}
