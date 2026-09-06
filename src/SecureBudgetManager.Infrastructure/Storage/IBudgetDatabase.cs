using Microsoft.Data.Sqlite;

namespace SecureBudgetManager.Infrastructure.Storage;

/// <summary>
/// Transactional access to the open local database. Internal to the infrastructure layer so
/// SQL never leaks into the domain or the user interface.
/// </summary>
public interface IBudgetDatabase
{
    bool IsOpen { get; }

    T Read<T>(Func<SqliteConnection, T> read);

    void WriteTransaction(Action<SqliteConnection, SqliteTransaction> write);

    void RecordAuditEvent(string eventName, string? detail);
}
