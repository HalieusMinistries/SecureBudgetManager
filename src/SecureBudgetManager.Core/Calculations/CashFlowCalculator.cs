using SecureBudgetManager.Core.Models;

namespace SecureBudgetManager.Core.Calculations;

public static class CashFlowCalculator
{
    public static Money Sum(IEnumerable<Money> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var total = Money.Zero;
        foreach (var value in values)
        {
            total += value;
        }

        return total;
    }

    public static CashFlowSnapshot CreateSnapshot(IEnumerable<Money> income, IEnumerable<Money> expenses)
    {
        ArgumentNullException.ThrowIfNull(income);
        ArgumentNullException.ThrowIfNull(expenses);

        var totalIncome = Sum(income);
        var totalExpenses = Sum(expenses);
        return new CashFlowSnapshot(totalIncome, totalExpenses, totalIncome - totalExpenses);
    }
}
