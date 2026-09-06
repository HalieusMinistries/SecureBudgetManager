using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using SecureBudgetManager.Core.Abstractions;
using SecureBudgetManager.Core.Security;

namespace SecureBudgetManager.Infrastructure.Security;

public sealed class Argon2idKeyDerivation : IKeyDerivation
{
    private static readonly byte[] RecoveryInfo = Encoding.UTF8.GetBytes("SecureBudgetManager/recovery-kek/v1");

    public SecureKey DeriveKeyEncryptionKey(
        ReadOnlySpan<char> secret,
        ReadOnlySpan<byte> salt,
        Argon2idParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        parameters.Validate();

        if (salt.Length < parameters.SaltLength)
        {
            throw new ArgumentException("The supplied salt is shorter than the configured length.", nameof(salt));
        }

        var secretBytes = EncodeSecret(secret);

        try
        {
            using var argon2 = new Argon2id(secretBytes)
            {
                Salt = salt.ToArray(),
                DegreeOfParallelism = parameters.DegreeOfParallelism,
                Iterations = parameters.Iterations,
                MemorySize = parameters.MemoryKibibytes
            };

            var derived = argon2.GetBytes(parameters.KeyLength);
            try
            {
                return SecureKey.CreateFrom(derived);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(derived);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secretBytes);
        }
    }

    public SecureKey DeriveFromRecoveryKey(ReadOnlySpan<byte> recoveryKey, ReadOnlySpan<byte> salt)
    {
        if (recoveryKey.Length != RecoveryKey.ByteLength)
        {
            throw new ArgumentException("The recovery key has an unexpected length.", nameof(recoveryKey));
        }

        var derived = new byte[32];
        try
        {
            HKDF.DeriveKey(HashAlgorithmName.SHA256, recoveryKey, derived, salt, RecoveryInfo);
            return SecureKey.CreateFrom(derived);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(derived);
        }
    }

    private static byte[] EncodeSecret(ReadOnlySpan<char> secret)
    {
        var byteCount = Encoding.UTF8.GetByteCount(secret);
        var buffer = new byte[byteCount];
        Encoding.UTF8.GetBytes(secret, buffer);
        return buffer;
    }
}
