using SecureBudgetManager.Core.Storage;

namespace SecureBudgetManager.App.Services;

/// <summary>
/// In-memory household document for the open session. The only persistence path is
/// <see cref="IBudgetRepository"/>, which writes to the local SQLite database.
/// </summary>
public interface IBudgetSession
{
    bool IsOpen { get; }

    bool HasUnsavedChanges { get; }

    bool IsSaving { get; }

    string? LastError { get; }

    BudgetDocument Document { get; }

    event EventHandler? Changed;

    /// <summary>Loads the household document after the local database is open.</summary>
    bool Open();

    /// <summary>
    /// Drops the in-memory document. Called on lock so financial figures cannot remain on screen
    /// or in view models after the vault is closed.
    /// </summary>
    void Clear();

    /// <summary>
    /// Replaces the in-memory document after domain validation. Does not write to disk until
    /// <see cref="SaveAsync"/> succeeds.
    /// </summary>
    bool TryReplace(BudgetDocument document, out string? error);

    bool CanUndo { get; }

    /// <summary>Restores the previous in-memory document. Does not write until SaveAsync.</summary>
    bool Undo();

    Task<bool> SaveAsync(CancellationToken cancellationToken = default);
}
