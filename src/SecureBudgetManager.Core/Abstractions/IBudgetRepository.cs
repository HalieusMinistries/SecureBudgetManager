using SecureBudgetManager.Core.Storage;

namespace SecureBudgetManager.Core.Abstractions;

/// <summary>
/// Loads and saves the household dataset. Saves are transactional: the whole document is written
/// or nothing is.
/// </summary>
public interface IBudgetRepository
{
    BudgetDocument Load();

    /// <summary>
    /// Validates and writes the document in one transaction. Throws if the vault is locked, so a
    /// save can never silently succeed against a closed database.
    /// </summary>
    void Save(BudgetDocument document);

    bool HasData();

    /// <summary>Recent encrypted-store audit rows. Details are counts and operation names only.</summary>
    IReadOnlyList<AuditRecord> ReadAuditTrail(int maximum = 250);
}
