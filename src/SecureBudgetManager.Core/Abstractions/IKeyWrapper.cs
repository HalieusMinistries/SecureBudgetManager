using SecureBudgetManager.Core.Security;

namespace SecureBudgetManager.Core.Abstractions;

/// <summary>
/// Authenticated wrap and unwrap of the database key. Authentication failure is how an
/// incorrect master secret is detected, so no separate password verifier is stored.
/// </summary>
public interface IKeyWrapper
{
    KeyEnvelope Wrap(SecureKey keyEncryptionKey, SecureKey databaseKey, ReadOnlySpan<byte> salt);

    /// <summary>Returns null when authentication fails, which means the secret was wrong.</summary>
    SecureKey? TryUnwrap(SecureKey keyEncryptionKey, KeyEnvelope envelope);
}
