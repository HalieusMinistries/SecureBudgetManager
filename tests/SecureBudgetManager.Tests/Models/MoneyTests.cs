using SecureBudgetManager.Core.Models;

namespace SecureBudgetManager.Tests.Models;

public sealed class MoneyTests
{
    [Fact]
    public void AdditionAndSubtraction_UseExactDecimalAmounts()
    {
        var left = new Money(10.10m);
        var right = new Money(2.20m);

        Assert.Equal(12.30m, (left + right).Amount);
        Assert.Equal(7.90m, (left - right).Amount);
    }

    [Fact]
    public void Comparison_OrdersByAmount()
    {
        Assert.True(new Money(2m) > new Money(1m));
        Assert.True(new Money(1m) < new Money(2m));
        Assert.True(new Money(2m) >= new Money(2m));
        Assert.Equal(0, new Money(3m).CompareTo(new Money(3m)));
    }
}
