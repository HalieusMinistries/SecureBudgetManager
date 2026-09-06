using SecureBudgetManager.Core.Abstractions;
using SecureBudgetManager.Core.Storage;

namespace SecureBudgetManager.App.Tests.Fakes;

public sealed class FakeBudgetRepository : IBudgetRepository
{
    public BudgetDocument Stored { get; set; } = new();

    public int SaveCount { get; private set; }

    public bool ThrowOnSave { get; set; }

    public TimeSpan SaveDelay { get; set; }

    public TaskCompletionSource<bool>? ContinueSave { get; set; }

    public Exception? SaveException { get; set; }

    public BudgetDocument Load() => Stored;

    public void Save(BudgetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.Validate();

        if (ContinueSave is not null)
        {
            ContinueSave.Task.GetAwaiter().GetResult();
        }

        if (SaveDelay > TimeSpan.Zero)
        {
            Thread.Sleep(SaveDelay);
        }

        if (ThrowOnSave)
        {
            throw SaveException ?? new InvalidOperationException("The encrypted store refused the save.");
        }

        SaveCount++;
        Stored = document;
    }

    public bool HasData() => !Stored.IsEmpty;

    public IReadOnlyList<AuditRecord> ReadAuditTrail(int maximum = 250) => [];
}
