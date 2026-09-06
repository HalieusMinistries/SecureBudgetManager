using SecureBudgetManager.Core.Validation;

namespace SecureBudgetManager.Tests.Validation;

public sealed class MoneyValidatorTests
{
    [Fact]
    public void ValidateEnteredAmount_AcceptsZero()
    {
        var result = MoneyValidator.ValidateEnteredAmount(0m);

        Assert.True(result.IsValid);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void ValidateEnteredAmount_AcceptsWholeNumbers()
    {
        var result = MoneyValidator.ValidateEnteredAmount(1m);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateEnteredAmount_AcceptsTwoDecimalPlaces()
    {
        var result = MoneyValidator.ValidateEnteredAmount(19.99m);

        Assert.True(result.IsValid);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void ValidateEnteredAmount_RejectsNegativeValues()
    {
        var result = MoneyValidator.ValidateEnteredAmount(-0.01m);

        Assert.False(result.IsValid);
        Assert.Equal("Amount cannot be negative.", result.ErrorMessage);
    }

    [Fact]
    public void ValidateEnteredAmount_RejectsMoreThanTwoDecimalPlaces()
    {
        var result = MoneyValidator.ValidateEnteredAmount(1.234m);

        Assert.False(result.IsValid);
        Assert.Equal("Amount cannot have more than two decimal places.", result.ErrorMessage);
    }
}
