using SecureBudgetManager.Core.Calculations;
using SecureBudgetManager.Core.Models;

namespace SecureBudgetManager.Tests.Calculations;

public sealed class CashFlowCalculatorTests
{
    [Fact]
    public void Sum_AddsValues()
    {
        var values = new[] { new Money(10.25m), new Money(4.75m) };

        var total = CashFlowCalculator.Sum(values);

        Assert.Equal(15.00m, total.Amount);
    }

    [Fact]
    public void Sum_EmptySequence_ReturnsZero()
    {
        var total = CashFlowCalculator.Sum(Array.Empty<Money>());

        Assert.Equal(Money.Zero, total);
    }

    [Fact]
    public void CreateSnapshot_SubtractsExpensesFromIncome()
    {
        var income = new[] { new Money(100m), new Money(20m) };
        var expenses = new[] { new Money(40m), new Money(15.50m) };

        var snapshot = CashFlowCalculator.CreateSnapshot(income, expenses);

        Assert.Equal(120.00m, snapshot.TotalIncome.Amount);
        Assert.Equal(55.50m, snapshot.TotalExpenses.Amount);
        Assert.Equal(64.50m, snapshot.Net.Amount);
        Assert.True(snapshot.IsSurplus);
        Assert.False(snapshot.IsDeficit);
    }

    [Fact]
    public void CreateSnapshot_WhenExpensesExceedIncome_IsDeficit()
    {
        var snapshot = CashFlowCalculator.CreateSnapshot(
            new[] { new Money(10m) },
            new[] { new Money(12.25m) });

        Assert.Equal(-2.25m, snapshot.Net.Amount);
        Assert.True(snapshot.IsDeficit);
        Assert.False(snapshot.IsSurplus);
    }

    [Fact]
    public void Sum_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => CashFlowCalculator.Sum(null!));
    }
}
