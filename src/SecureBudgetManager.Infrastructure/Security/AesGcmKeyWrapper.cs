using System.Security.Cryptography;
using SecureBudgetManager.Core.Abstractions;
using SecureBudgetManager.Core.Security;

namespace SecureBudgetManager.Infrastructure.Security;

/// <summary>
/// Wraps the database key with AES-256-GCM. The GCM tag authenticates the envelope, so a wrong
/// master secret is detected by decryption failure and no password verifier needs to be stored.
/// </summary>
public sealed class AesGcmKeyWrapper : IKeyWrapper
{
    private const int NonceLength = 12;
    private const int TagLength = 16;

    public KeyEnvelope Wrap(SecureKey keyEncryptionKey, SecureKey databaseKey, ReadOnlySpan<byte> salt)
    {
        ArgumentNullException.ThrowIfNull(keyEncryptionKey);
        ArgumentNullException.ThrowIfNull(databaseKey);

        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var payload = new byte[databaseKey.Length + TagLength];

        using var aes = new AesGcm(keyEncryptionKey.AsSpan(), TagLength);
        aes.Encrypt(
            nonce,
            databaseKey.AsSpan(),
            payload.AsSpan(0, databaseKey.Length),
            payload.AsSpan(databaseKey.Length, TagLength));

        return new KeyEnvelope(salt.ToArray(), nonce, payload);
    }

    public SecureKey? TryUnwrap(SecureKey keyEncryptionKey, KeyEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(keyEncryptionKey);
        ArgumentNullException.ThrowIfNull(envelope);
        envelope.Validate();

        var plaintextLength = envelope.Ciphertext.Length - TagLength;
        var plaintext = new byte[plaintextLength];

        try
        {
            using var aes = new AesGcm(keyEncryptionKey.AsSpan(), TagLength);
            aes.Decrypt(
                envelope.Nonce,
                envelope.Ciphertext.AsSpan(0, plaintextLength),
                envelope.Ciphertext.AsSpan(plaintextLength, TagLength),
                plaintext);

            return SecureKey.CreateFrom(plaintext);
        }
        catch (AuthenticationTagMismatchException)
        {
            // Wrong master secret, or the envelope was tampered with.
            return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }
}
