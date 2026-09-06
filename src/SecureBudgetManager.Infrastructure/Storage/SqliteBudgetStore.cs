using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SecureBudgetManager.Core.Abstractions;
using SecureBudgetManager.Core.Security;
using SecureBudgetManager.Core.Storage;

namespace SecureBudgetManager.Infrastructure.Storage;

/// <summary>
/// Ordinary local SQLite household database. Saves run in a single transaction so a failed write
/// cannot replace the previous successful document.
/// </summary>
public sealed class SqliteBudgetStore : ILocalBudgetStore, IBudgetDatabase, IDisposable
{
    private static readonly byte[] SqliteHeader = Encoding.ASCII.GetBytes("SQLite format 3\0");

    private readonly ILocalDataDirectory _directory;
    private readonly ILogger<SqliteBudgetStore> _logger;
    private SqliteConnection? _connection;
    private bool _disposed;

    public SqliteBudgetStore(ILocalDataDirectory directory, ILogger<SqliteBudgetStore> logger)
    {
        _directory = directory;
        _logger = logger;
    }

    public bool IsOpen => _connection is not null;

    public int SchemaVersion { get; private set; }

    public string DatabasePath => _directory.GetDatabasePath();

    public void Open()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Close();
        SqliteProvider.EnsureInitialised();
        _directory.EnsureCreated();

        if (File.Exists(DatabasePath) && !LooksLikeSqlite(DatabasePath))
        {
            throw new EncryptionUnavailableException(
                "The household file is not an ordinary SQLite database. Move any leftover encrypted vault aside before opening.");
        }

        var isNewDatabase = !File.Exists(DatabasePath);
        var connection = new SqliteConnection(BuildConnectionString(DatabasePath));

        try
        {
            connection.Open();
            ApplySessionPragmas(connection);
            _connection = connection;
            SchemaVersion = ReadUserVersion(connection);
            ApplyPendingMigrations();

            _logger.LogInformation(
                "Local database opened. New file: {IsNew}. Schema version: {SchemaVersion}.",
                isNewDatabase,
                SchemaVersion);
        }
        catch
        {
            connection.Dispose();
            _connection = null;
            throw;
        }
    }

    public void Close()
    {
        if (_connection is null)
        {
            return;
        }

        _connection.Dispose();
        _connection = null;
        SchemaVersion = 0;
        SqliteConnection.ClearAllPools();
    }

    public DatabaseIntegrityReport CheckIntegrity()
    {
        var connection = RequireConnection();
        var problems = new List<string>();

        foreach (var row in ReadSingleColumn(connection, "PRAGMA integrity_check;"))
        {
            if (!string.Equals(row, "ok", StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"Database integrity: {row}");
            }
        }

        foreach (var row in ReadSingleColumn(connection, "PRAGMA foreign_key_check;"))
        {
            problems.Add($"Foreign key: {row}");
        }

        return problems.Count == 0
            ? DatabaseIntegrityReport.Healthy(SchemaVersion)
            : new DatabaseIntegrityReport(false, SchemaVersion, problems);
    }

    public void RecordAuditEvent(string eventName, string? detail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        SensitiveLogGuard.ThrowIfDisallowed(eventName);
        SensitiveLogGuard.ThrowIfDisallowed(detail);

        var connection = RequireConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO audit_log (occurred_utc, event_name, detail) VALUES ($occurred, $event, $detail);";
        command.Parameters.AddWithValue("$occurred", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$event", eventName);
        command.Parameters.AddWithValue("$detail", detail ?? (object)DBNull.Value);
        command.ExecuteNonQuery();
    }

    public void VacuumInto(string destinationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        var connection = RequireConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "VACUUM INTO $destination;";
        command.Parameters.AddWithValue("$destination", destinationPath);
        command.ExecuteNonQuery();
    }

    public T Read<T>(Func<SqliteConnection, T> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        return read(RequireConnection());
    }

    public void WriteTransaction(Action<SqliteConnection, SqliteTransaction> write)
    {
        ArgumentNullException.ThrowIfNull(write);

        var connection = RequireConnection();
        using var transaction = connection.BeginTransaction();

        try
        {
            write(connection, transaction);
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public int CountAuditEvents()
    {
        var connection = RequireConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM audit_log;";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Close();
        _disposed = true;
    }

    internal static string BuildConnectionString(string databasePath) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString();

    private static void ApplySessionPragmas(SqliteConnection connection)
    {
        ExecuteNonQuery(connection, "PRAGMA foreign_keys = ON;");
        ExecuteNonQuery(connection, "PRAGMA journal_mode = WAL;");
        ExecuteNonQuery(connection, "PRAGMA synchronous = FULL;");
    }

    private void ApplyPendingMigrations()
    {
        var connection = RequireConnection();
        var pending = SchemaMigrationPlanner.GetPending(BudgetSchema.Migrations, SchemaVersion);

        foreach (var migration in pending)
        {
            using var transaction = connection.BeginTransaction();
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = migration.Sql;
                command.ExecuteNonQuery();
            }

            using (var versionCommand = connection.CreateCommand())
            {
                versionCommand.Transaction = transaction;
                versionCommand.CommandText = FormattableString.Invariant(
                    $"PRAGMA user_version = {migration.Version};");
                versionCommand.ExecuteNonQuery();
            }

            transaction.Commit();
            SchemaVersion = migration.Version;
            _logger.LogInformation(
                "Applied schema migration {Version}: {Description}.",
                migration.Version,
                migration.Description);
        }
    }

    private static int ReadUserVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static IEnumerable<string> ReadSingleColumn(SqliteConnection connection, string sql)
    {
        var results = new List<string>();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (!reader.IsDBNull(0))
            {
                results.Add(reader.GetString(0));
            }
        }

        return results;
    }

    private static void ExecuteNonQuery(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static bool LooksLikeSqlite(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var header = new byte[SqliteHeader.Length];
        return stream.Read(header, 0, header.Length) == header.Length && header.AsSpan().SequenceEqual(SqliteHeader);
    }

    private SqliteConnection RequireConnection() =>
        _connection ?? throw new VaultLockedException();
}
