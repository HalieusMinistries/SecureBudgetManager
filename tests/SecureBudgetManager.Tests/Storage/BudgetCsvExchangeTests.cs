using SecureBudgetManager.Core.Expenses;
using SecureBudgetManager.Core.Household;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Storage;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.Tests.Storage;

public sealed class BudgetCsvExchangeTests
{
    [Fact]
    public void ExportThenMergeRoundTripsMembersAndSkipsDuplicates()
    {
        var member = new HouseholdMember { Id = Guid.NewGuid(), Name = "Sam" };
        var document = new BudgetDocument
        {
            Members = [member],
            Accounts =
            [
                new BankAccount { Id = Guid.NewGuid(), Name = "Checking", CurrentBalance = new Money(120m), IsPrimary = true }
            ],
            Expenses =
            [
                new ExpenseItem
                {
                    Id = Guid.NewGuid(),
                    Name = "Rent",
                    Category = ExpenseCategory.Housing,
                    ExpectedAmount = new Money(900m),
                    Frequency = Frequency.Monthly,
                    AnchorDueDate = new DateOnly(2026, 1, 1)
                }
            ]
        };

        var csv = BudgetCsvExchange.Export(document);
        var preview = BudgetCsvExchange.Preview(csv);
        Assert.Equal(1, preview.Members);
        Assert.Equal(1, preview.Accounts);
        Assert.Equal(1, preview.Expenses);

        var merged = BudgetCsvExchange.Merge(document, csv, out var skipped);
        Assert.Equal(document.Members.Count, merged.Members.Count);
        Assert.NotEmpty(skipped);
    }
}
