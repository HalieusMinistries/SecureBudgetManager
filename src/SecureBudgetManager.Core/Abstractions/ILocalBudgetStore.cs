using SecureBudgetManager.Core.Storage;

namespace SecureBudgetManager.Core.Abstractions;

/// <summary>
/// The local household SQLite database. Saves are transactional. A failed write does not replace
/// the previous successful document.
/// </summary>
public interface ILocalBudgetStore
{
    bool IsOpen { get; }

    int SchemaVersion { get; }

    string DatabasePath { get; }

    /// <summary>Opens (creating if necessary) the local database and applies pending migrations.</summary>
    void Open();

    void Close();

    DatabaseIntegrityReport CheckIntegrity();
}
