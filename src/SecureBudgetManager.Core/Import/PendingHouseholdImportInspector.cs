using System.Text.Json;
using SecureBudgetManager.Core.Storage;

namespace SecureBudgetManager.Core.Import;

public sealed record PendingImportOffer(
    string SourcePath,
    DateTime CreatedLocal,
    long ByteLength,
    ImportPreview Preview,
    ImportVerificationReport Verification);

/// <summary>
/// Reads a household payload without writing the local database.
/// </summary>
public static class PendingHouseholdImportInspector
{
    public static PendingImportOffer Inspect(string path, BudgetDocument existing, DateOnly today)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(existing);

        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new FileNotFoundException("The pending household import file was not found.", path);
        }

        var json = File.ReadAllText(path);
        var payload = HouseholdImportSerializer.Parse(json);
        var incoming = payload.ToDocument();
        var merge = HouseholdImportMerger.Merge(existing, incoming);
        var verification = HouseholdImportVerifier.Verify(merge.Document, merge.Preview, payload.Verification, today);

        return new PendingImportOffer(
            path,
            info.CreationTime,
            info.Length,
            merge.Preview,
            verification);
    }

    public static HouseholdImportPayload ParseFile(string path)
    {
        var json = File.ReadAllText(path);
        return HouseholdImportSerializer.Parse(json);
    }

    public static bool LooksLikePendingPayload(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            _ = HouseholdImportSerializer.Parse(File.ReadAllText(path));
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or IOException)
        {
            return false;
        }
    }
}
