using System.Security.Cryptography;
using System.Text;

namespace SecureBudgetManager.Core.Security;

/// <summary>
/// A 256-bit recovery secret rendered in Crockford base32 groups.
/// The key is high entropy, so it is stretched with HKDF rather than Argon2id.
/// </summary>
public static class RecoveryKey
{
    public const int ByteLength = 32;
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const int GroupSize = 5;

    public static byte[] Generate()
    {
        var material = new byte[ByteLength];
        RandomNumberGenerator.Fill(material);
        return material;
    }

    public static string Format(ReadOnlySpan<byte> material)
    {
        if (material.Length != ByteLength)
        {
            throw new ArgumentException($"A recovery key must be {ByteLength} bytes.", nameof(material));
        }

        var encoded = Encode(material);
        var builder = new StringBuilder(encoded.Length + (encoded.Length / GroupSize));

        for (var i = 0; i < encoded.Length; i++)
        {
            if (i > 0 && i % GroupSize == 0)
            {
                builder.Append('-');
            }

            builder.Append(encoded[i]);
        }

        return builder.ToString();
    }

    public static bool TryParse(string? text, out byte[] material)
    {
        material = [];

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var symbols = new List<int>(64);
        foreach (var raw in text)
        {
            if (raw is '-' or ' ' or '\t' or '\r' or '\n')
            {
                continue;
            }

            var index = MapSymbol(raw);
            if (index < 0)
            {
                return false;
            }

            symbols.Add(index);
        }

        if (symbols.Count * 5 < ByteLength * 8)
        {
            return false;
        }

        var decoded = new byte[ByteLength];
        var bitBuffer = 0;
        var bitCount = 0;
        var written = 0;

        foreach (var symbol in symbols)
        {
            bitBuffer = (bitBuffer << 5) | symbol;
            bitCount += 5;

            if (bitCount < 8)
            {
                continue;
            }

            bitCount -= 8;
            if (written >= decoded.Length)
            {
                return false;
            }

            decoded[written++] = (byte)((bitBuffer >> bitCount) & 0xFF);
        }

        if (written != ByteLength)
        {
            return false;
        }

        material = decoded;
        return true;
    }

    private static string Encode(ReadOnlySpan<byte> material)
    {
        var builder = new StringBuilder(((material.Length * 8) + 4) / 5);
        var bitBuffer = 0;
        var bitCount = 0;

        foreach (var value in material)
        {
            bitBuffer = (bitBuffer << 8) | value;
            bitCount += 8;

            while (bitCount >= 5)
            {
                bitCount -= 5;
                builder.Append(Alphabet[(bitBuffer >> bitCount) & 0x1F]);
            }
        }

        if (bitCount > 0)
        {
            builder.Append(Alphabet[(bitBuffer << (5 - bitCount)) & 0x1F]);
        }

        return builder.ToString();
    }

    private static int MapSymbol(char raw)
    {
        var upper = char.ToUpperInvariant(raw);

        // Crockford base32 treats these as their visually similar digits.
        upper = upper switch
        {
            'O' => '0',
            'I' or 'L' => '1',
            'U' => 'V',
            _ => upper
        };

        return Alphabet.IndexOf(upper, StringComparison.Ordinal);
    }
}
