namespace SecureBudgetManager.Core.Models;

/// <summary>
/// Calculated totals for a household period. Contains no persistence and no personal identifiers.
/// </summary>
public sealed record CashFlowSnapshot(Money TotalIncome, Money TotalExpenses, Money Net)
{
    public bool IsSurplus => Net.Amount > 0m;

    public bool IsDeficit => Net.Amount < 0m;
}
