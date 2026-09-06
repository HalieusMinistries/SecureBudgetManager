using SecureBudgetManager.Core.Help;

namespace SecureBudgetManager.Tests.Help;

public sealed class FinanceGlossaryTests
{
    [Fact]
    public void EveryRequiredHouseholdFinanceTermIsExplainedInNeutralLanguage()
    {
        var terms = FinanceGlossary.All.Select(entry => entry.Term).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("Gross pay", terms);
        Assert.Contains("Safe-to-spend", terms);
        Assert.Contains("Reimbursement", terms);
        Assert.Contains("Utah withholding", terms);
        Assert.DoesNotContain(FinanceGlossary.All, entry =>
            entry.Explanation.Contains("you must", StringComparison.OrdinalIgnoreCase)
            || entry.Explanation.Contains("shame", StringComparison.OrdinalIgnoreCase));
        Assert.True(FinanceGlossary.All.Count >= 25);
    }
}
