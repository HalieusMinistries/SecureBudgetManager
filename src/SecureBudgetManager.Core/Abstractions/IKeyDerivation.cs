using SecureBudgetManager.Core.Security;

namespace SecureBudgetManager.Core.Abstractions;

/// <summary>
/// Stretches a low-entropy master secret into a key-encryption key.
/// Implementations must use Argon2id and must never persist or log the secret.
/// </summary>
public interface IKeyDerivation
{
    SecureKey DeriveKeyEncryptionKey(ReadOnlySpan<char> secret, ReadOnlySpan<byte> salt, Argon2idParameters parameters);

    /// <summary>
    /// Stretches an already high-entropy recovery key. HKDF is sufficient here; Argon2id is not required.
    /// </summary>
    SecureKey DeriveFromRecoveryKey(ReadOnlySpan<byte> recoveryKey, ReadOnlySpan<byte> salt);
}
