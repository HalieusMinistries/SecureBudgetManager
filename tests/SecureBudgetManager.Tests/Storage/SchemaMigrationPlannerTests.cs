using SecureBudgetManager.Core.Storage;
using SecureBudgetManager.Infrastructure.Storage;

namespace SecureBudgetManager.Tests.Storage;

public sealed class SchemaMigrationPlannerTests
{
    private static readonly SchemaMigration[] Three =
    [
        new(1, "one", "SELECT 1;"),
        new(2, "two", "SELECT 2;"),
        new(3, "three", "SELECT 3;")
    ];

    [Fact]
    public void FreshDatabaseRunsEveryMigrationInOrder()
    {
        var pending = SchemaMigrationPlanner.GetPending(Three, currentVersion: 0);

        Assert.Equal([1, 2, 3], pending.Select(migration => migration.Version));
    }

    [Fact]
    public void PartiallyMigratedDatabaseRunsOnlyTheRemainder()
    {
        var pending = SchemaMigrationPlanner.GetPending(Three, currentVersion: 2);

        Assert.Single(pending);
        Assert.Equal(3, pending[0].Version);
    }

    [Fact]
    public void UpToDateDatabaseRunsNothing()
    {
        Assert.Empty(SchemaMigrationPlanner.GetPending(Three, currentVersion: 3));
    }

    [Fact]
    public void DatabaseFromANewerBuildIsRejectedRatherThanDowngraded()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => SchemaMigrationPlanner.GetPending(Three, currentVersion: 4));

        Assert.Contains("Upgrade Secure Budget Manager", exception.Message);
    }

    [Fact]
    public void GapsInTheMigrationSetAreRejected()
    {
        SchemaMigration[] withGap = [new(1, "one", "SELECT 1;"), new(3, "three", "SELECT 3;")];

        Assert.Throws<InvalidOperationException>(() => SchemaMigrationPlanner.GetPending(withGap, 0));
    }

    [Fact]
    public void DuplicateVersionsAreRejected()
    {
        SchemaMigration[] duplicated = [new(1, "one", "SELECT 1;"), new(1, "one again", "SELECT 1;")];

        Assert.Throws<InvalidOperationException>(() => SchemaMigrationPlanner.GetPending(duplicated, 0));
    }

    [Fact]
    public void ProductionSchemaIsContiguousAndOrdered()
    {
        var pending = SchemaMigrationPlanner.GetPending(BudgetSchema.Migrations, 0);

        Assert.Equal(BudgetSchema.LatestVersion, pending.Count);
        Assert.Equal(
            Enumerable.Range(1, BudgetSchema.LatestVersion),
            pending.Select(migration => migration.Version));
    }
}
