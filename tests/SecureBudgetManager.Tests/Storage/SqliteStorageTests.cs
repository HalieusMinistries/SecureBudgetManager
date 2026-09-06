using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using SecureBudgetManager.Core.Security;
using SecureBudgetManager.Infrastructure.Storage;
using SecureBudgetManager.Tests.TestSupport;

namespace SecureBudgetManager.Tests.Storage;

public sealed class SqliteStorageTests
{
    private static readonly byte[] PlaintextSqliteHeader = Encoding.ASCII.GetBytes("SQLite format 3\0");

    [Fact]
    public void OpenedDatabaseFileIsOrdinarySqlite()
    {
        using var directory = new TempVaultDirectory();
        using var store = new SqliteBudgetStore(directory, NullLogger<SqliteBudgetStore>.Instance);

        store.Open();
        store.Close();

        var header = ReadHeader(directory.GetDatabasePath());
        Assert.True(header.AsSpan().SequenceEqual(PlaintextSqliteHeader));
    }

    [Fact]
    public void MigrationsBringAFreshDatabaseToTheLatestSchema()
    {
        using var directory = new TempVaultDirectory();
        using var store = new SqliteBudgetStore(directory, NullLogger<SqliteBudgetStore>.Instance);

        store.Open();

        Assert.Equal(BudgetSchema.LatestVersion, store.SchemaVersion);
    }

    [Fact]
    public void ReopeningDoesNotReapplyMigrations()
    {
        using var directory = new TempVaultDirectory();

        using (var first = new SqliteBudgetStore(directory, NullLogger<SqliteBudgetStore>.Instance))
        {
            first.Open();
            first.RecordAuditEvent("workspace.opened", "first run");
            first.Close();
        }

        using var second = new SqliteBudgetStore(directory, NullLogger<SqliteBudgetStore>.Instance);
        second.Open();

        Assert.Equal(BudgetSchema.LatestVersion, second.SchemaVersion);
        Assert.Equal(1, second.CountAuditEvents());
    }

    [Fact]
    public void LeftoverEncryptedFileIsRefused()
    {
        using var directory = new TempVaultDirectory();
        File.WriteAllBytes(directory.GetDatabasePath(), [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F]);

        using var store = new SqliteBudgetStore(directory, NullLogger<SqliteBudgetStore>.Instance);

        Assert.Throws<EncryptionUnavailableException>(store.Open);
    }

    [Fact]
    public void RawSqliteCanReadTheDatabase()
    {
        using var directory = new TempVaultDirectory();

        using (var store = new SqliteBudgetStore(directory, NullLogger<SqliteBudgetStore>.Instance))
        {
            store.Open();
            store.RecordAuditEvent("household.created", null);
            store.Close();
        }

        using var connection = new SqliteConnection($"Data Source={directory.GetDatabasePath()};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM audit_log;";

        Assert.Equal(1L, command.ExecuteScalar());
    }

    [Fact]
    public void IntegrityCheckPassesOnAHealthyDatabase()
    {
        using var directory = new TempVaultDirectory();
        using var store = new SqliteBudgetStore(directory, NullLogger<SqliteBudgetStore>.Instance);

        store.Open();
        var report = store.CheckIntegrity();

        Assert.True(report.IsHealthy);
        Assert.Empty(report.Problems);
        Assert.Equal(BudgetSchema.LatestVersion, report.SchemaVersion);
    }

    [Fact]
    public void OperationsOnAClosedStoreAreRefused()
    {
        using var directory = new TempVaultDirectory();
        using var store = new SqliteBudgetStore(directory, NullLogger<SqliteBudgetStore>.Instance);

        Assert.False(store.IsOpen);
        Assert.Throws<VaultLockedException>(() => store.CheckIntegrity());
        Assert.Throws<VaultLockedException>(() => store.RecordAuditEvent("test", null));
    }

    [Fact]
    public void AuditTrailRefusesToRecordAnythingSecretLooking()
    {
        using var directory = new TempVaultDirectory();
        using var store = new SqliteBudgetStore(directory, NullLogger<SqliteBudgetStore>.Instance);

        store.Open();

        Assert.ThrowsAny<Exception>(() => store.RecordAuditEvent("unlock", "password=hunter2"));
        Assert.Equal(0, store.CountAuditEvents());
    }

    private static byte[] ReadHeader(string path)
    {
        using var stream = File.OpenRead(path);
        var header = new byte[PlaintextSqliteHeader.Length];
        stream.ReadExactly(header);
        return header;
    }
}
