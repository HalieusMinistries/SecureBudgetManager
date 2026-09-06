namespace SecureBudgetManager.Core.Storage;

/// <summary>
/// A privacy-safe audit entry. Amounts and secrets are never stored here.
/// </summary>
public sealed record AuditRecord(
    DateTime OccurredUtc,
    string EventName,
    string? Detail,
    string? Operation = null,
    string? RecordType = null,
    string? RecordId = null,
    string? UserNote = null);
