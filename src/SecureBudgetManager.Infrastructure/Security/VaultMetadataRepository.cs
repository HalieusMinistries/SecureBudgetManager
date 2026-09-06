using System.Text.Json;
using System.Text.Json.Serialization;
using SecureBudgetManager.Core.Abstractions;
using SecureBudgetManager.Core.Security;

namespace SecureBudgetManager.Infrastructure.Security;

/// <summary>
/// Stores vault metadata as JSON beside the database. The file contains salts and AES-GCM wrapped
/// keys only; it never contains the master secret, the database key or any financial value.
/// </summary>
public sealed class VaultMetadataRepository : IVaultMetadataRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ILocalDataDirectory _directory;

    public VaultMetadataRepository(ILocalDataDirectory directory)
    {
        _directory = directory;
    }

    public bool Exists => File.Exists(_directory.GetVaultMetadataPath());

    public VaultMetadata Read()
    {
        var path = _directory.GetVaultMetadataPath();

        if (!File.Exists(path))
        {
            throw new VaultFormatException("No vault metadata was found. Set up the vault first.");
        }

        VaultMetadata? metadata;
        try
        {
            using var stream = File.OpenRead(path);
            metadata = JsonSerializer.Deserialize<VaultMetadata>(stream, SerializerOptions);
        }
        catch (JsonException)
        {
            throw new VaultFormatException("The vault metadata file is not readable. Restore a backup to continue.");
        }
        catch (IOException exception)
        {
            throw new EncryptionUnavailableException("The vault metadata file could not be read.", exception);
        }

        if (metadata is null)
        {
            throw new VaultFormatException("The vault metadata file is empty. Restore a backup to continue.");
        }

        metadata.Validate();
        return metadata;
    }

    public void Write(VaultMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        metadata.Validate();

        _directory.EnsureCreated();

        var path = _directory.GetVaultMetadataPath();
        var temporaryPath = path + ".tmp";

        using (var stream = File.Create(temporaryPath))
        {
            JsonSerializer.Serialize(stream, metadata, SerializerOptions);
            stream.Flush(flushToDisk: true);
        }

        // Replace atomically so a crash mid-write cannot leave unusable metadata.
        if (File.Exists(path))
        {
            File.Replace(temporaryPath, path, destinationBackupFileName: null);
        }
        else
        {
            File.Move(temporaryPath, path);
        }
    }
}
