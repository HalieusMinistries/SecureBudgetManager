namespace SecureBudgetManager.Core.Storage;

public sealed record DatabaseIntegrityReport(bool IsHealthy, int SchemaVersion, IReadOnlyList<string> Problems)
{
    public static DatabaseIntegrityReport Healthy(int schemaVersion) => new(true, schemaVersion, []);

    public string Summary => IsHealthy
        ? "The local database passed its integrity checks."
        : $"The database reported {Problems.Count} integrity problem(s).";
}
