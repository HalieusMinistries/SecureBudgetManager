using System.Security.Cryptography;

namespace SecureBudgetManager.Core.Security;

/// <summary>
/// Holds key material in a pinned buffer that is zeroed on disposal.
/// Pinning prevents the garbage collector from leaving copies behind when it relocates objects.
/// </summary>
public sealed class SecureKey : IDisposable
{
    private readonly byte[] _buffer;
    private bool _disposed;

    private SecureKey(byte[] pinnedBuffer)
    {
        _buffer = pinnedBuffer;
    }

    public int Length => _buffer.Length;

    public static SecureKey CreateFrom(ReadOnlySpan<byte> material)
    {
        if (material.IsEmpty)
        {
            throw new ArgumentException("Key material cannot be empty.", nameof(material));
        }

        var buffer = GC.AllocateArray<byte>(material.Length, pinned: true);
        material.CopyTo(buffer);
        return new SecureKey(buffer);
    }

    public static SecureKey CreateRandom(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);

        var buffer = GC.AllocateArray<byte>(length, pinned: true);
        RandomNumberGenerator.Fill(buffer);
        return new SecureKey(buffer);
    }

    public ReadOnlySpan<byte> AsSpan()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _buffer;
    }

    /// <summary>
    /// Lower-case hexadecimal form, required by SQLCipher raw-key pragmas.
    /// The caller must clear the returned array as soon as the pragma has been issued.
    /// </summary>
    public char[] ToHexCharArray()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var hex = GC.AllocateArray<char>(_buffer.Length * 2, pinned: true);
        const string Digits = "0123456789abcdef";
        for (var i = 0; i < _buffer.Length; i++)
        {
            hex[i * 2] = Digits[_buffer[i] >> 4];
            hex[(i * 2) + 1] = Digits[_buffer[i] & 0x0F];
        }

        return hex;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(_buffer);
        _disposed = true;
    }
}
