namespace SecureBudgetManager.Core.Storage;

/// <summary>
/// One forward-only schema step. Applied inside a transaction and recorded via PRAGMA user_version.
/// </summary>
public sealed record SchemaMigration(int Version, string Description, string Sql)
{
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Version);
        ArgumentException.ThrowIfNullOrWhiteSpace(Description);
        ArgumentException.ThrowIfNullOrWhiteSpace(Sql);
    }
}

public static class SchemaMigrationPlanner
{
    /// <summary>
    /// Returns the migrations that still need to run, in order, validating that the set is contiguous from 1.
    /// </summary>
    public static IReadOnlyList<SchemaMigration> GetPending(
        IEnumerable<SchemaMigration> migrations,
        int currentVersion)
    {
        ArgumentNullException.ThrowIfNull(migrations);
        ArgumentOutOfRangeException.ThrowIfNegative(currentVersion);

        var ordered = migrations.OrderBy(migration => migration.Version).ToList();

        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].Validate();

            if (ordered[i].Version != i + 1)
            {
                throw new InvalidOperationException(
                    "Schema migrations must be numbered contiguously from 1 with no duplicates.");
            }
        }

        if (currentVersion > ordered.Count)
        {
            throw new InvalidOperationException(
                $"The database schema is version {currentVersion} but this build only knows {ordered.Count}. " +
                "Upgrade Secure Budget Manager before opening this database.");
        }

        return ordered.Where(migration => migration.Version > currentVersion).ToList();
    }
}
