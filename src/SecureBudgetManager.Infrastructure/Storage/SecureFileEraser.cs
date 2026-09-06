using System.Security.Cryptography;

namespace SecureBudgetManager.Infrastructure.Storage;

/// <summary>
/// Best-effort overwrite before deletion.
///
/// Windows cannot guarantee erasure: SSD wear levelling, NTFS journalling, shadow copies and
/// page files may retain earlier copies. This reduces casual recovery only, and callers must not
/// present it to the user as a guarantee.
/// </summary>
public static class SecureFileEraser
{
    public static bool TryErase(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        try
        {
            var length = new FileInfo(path).Length;

            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                var remaining = length;

                while (remaining > 0)
                {
                    RandomNumberGenerator.Fill(buffer);
                    var chunk = (int)Math.Min(buffer.Length, remaining);
                    stream.Write(buffer, 0, chunk);
                    remaining -= chunk;
                }

                stream.Flush(flushToDisk: true);
            }

            File.Delete(path);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
