using SecureBudgetManager.Core.Debt;
using SecureBudgetManager.Core.Models;

namespace SecureBudgetManager.Tests.Debt;

public sealed class PayoffPlannerTests
{
    private static readonly DateOnly Start = new(2026, 3, 1);

    [Fact]
    public void NoDebtsClearImmediately()
    {
        var plan = PayoffPlanner.Plan([], new Money(500m), PayoffStrategy.Avalanche, Start);

        Assert.Equal(0, plan.MonthsToClear);
        Assert.Equal(Money.Zero, plan.TotalInterest);
        Assert.True(plan.ClearedWithinHorizon);
    }

    [Fact]
    public void ABudgetBelowTheCombinedMinimumsIsRefused()
    {
        var exception = Assert.Throws<ArgumentException>(() => PayoffPlanner.Plan(
            Debts(),
            new Money(50m),
            PayoffStrategy.Avalanche,
            Start));

        Assert.Contains("below the combined minimum payments", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryDebtIsClearedAndOrderIsRecorded()
    {
        var plan = PayoffPlanner.Plan(Debts(), new Money(700m), PayoffStrategy.Avalanche, Start);

        Assert.True(plan.ClearedWithinHorizon);
        Assert.Equal(3, plan.Order.Count);
        Assert.True(plan.MonthsToClear > 0);
    }

    [Fact]
    public void AvalancheClearsTheHighestRateFirst()
    {
        var plan = PayoffPlanner.Plan(Debts(), new Money(700m), PayoffStrategy.Avalanche, Start);

        // Store card at 27.9% is the most expensive, so it goes first.
        Assert.Equal("Store card", plan.Order[0].Name);
    }

    [Fact]
    public void TheTwoStrategiesTargetDifferentDebtsFirst()
    {
        // A small cheap loan against a large expensive card is the case where the two strategies
        // genuinely disagree, so it is the only honest way to test the targeting.
        var debts = new List<DebtAccount>
        {
            Debt("Small loan", new Money(1500m), 5m, new Money(40m)),
            Debt("Expensive card", new Money(3000m), 27m, new Money(75m))
        };

        var snowball = PayoffPlanner.Plan(debts, new Money(600m), PayoffStrategy.Snowball, Start);
        var avalanche = PayoffPlanner.Plan(debts, new Money(600m), PayoffStrategy.Avalanche, Start);

        Assert.Equal("Small loan", snowball.Order[0].Name);
        Assert.Equal("Expensive card", avalanche.Order[0].Name);
    }

    [Fact]
    public void TheOrderRecordsWhenEachDebtWasClearedNotWhichWasTargeted()
    {
        // A debt with a small balance can finish first on its minimum payment alone, even under
        // avalanche, so the order is a record of outcomes rather than of intent.
        var plan = PayoffPlanner.Plan(Debts(), new Money(700m), PayoffStrategy.Avalanche, Start);

        var clearedMonths = plan.Order.Select(entry => entry.ClearedInMonth).ToList();

        Assert.Equal(clearedMonths.OrderBy(month => month), clearedMonths);
    }

    [Fact]
    public void AvalancheCostsNoMoreInterestThanSnowball()
    {
        var comparison = PayoffPlanner.Compare(Debts(), new Money(700m), Start);

        Assert.True(comparison.Avalanche.TotalInterest <= comparison.Snowball.TotalInterest);
        Assert.False(comparison.InterestSavedByAvalanche.IsNegative);
        Assert.False(string.IsNullOrWhiteSpace(comparison.Recommendation));
    }

    [Fact]
    public void TheComparisonSaysWhenTheChoiceBarelyMatters()
    {
        // Two debts at the same rate: strategy makes almost no difference, and the advice
        // should say so rather than pretending one is clearly better.
        var similar = new List<DebtAccount>
        {
            Debt("Card A", new Money(1000m), 18m, new Money(40m)),
            Debt("Card B", new Money(1100m), 18m, new Money(44m))
        };

        var comparison = PayoffPlanner.Compare(similar, new Money(400m), Start);

        Assert.Contains("almost the same", comparison.Recommendation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void APromotionalRateIsHonouredUntilItExpires()
    {
        var promotional = new List<DebtAccount>
        {
            Debt("Balance transfer", new Money(3000m), 24.9m, new Money(80m)) with
            {
                PromotionalRate = 0m,
                PromotionalRateEnds = new DateOnly(2027, 3, 1)
            }
        };

        var noPromotion = new List<DebtAccount>
        {
            Debt("Balance transfer", new Money(3000m), 24.9m, new Money(80m))
        };

        var withPromotion = PayoffPlanner.Plan(promotional, new Money(150m), PayoffStrategy.Avalanche, Start);
        var without = PayoffPlanner.Plan(noPromotion, new Money(150m), PayoffStrategy.Avalanche, Start);

        Assert.True(withPromotion.TotalInterest < without.TotalInterest);
    }

    [Fact]
    public void TheRateInForceSwitchesOnTheExpiryDate()
    {
        var debt = Debt("Card", new Money(1000m), 22.9m, new Money(40m)) with
        {
            PromotionalRate = 0m,
            PromotionalRateEnds = new DateOnly(2026, 6, 1)
        };

        Assert.Equal(0m, debt.RateOn(new DateOnly(2026, 5, 31)));
        Assert.Equal(22.9m, debt.RateOn(new DateOnly(2026, 6, 1)));
    }

    [Fact]
    public void APromotionalRateWithoutAnEndDateIsRefused()
    {
        var debt = Debt("Card", new Money(1000m), 22.9m, new Money(40m)) with
        {
            PromotionalRate = 0m
        };

        Assert.Throws<ArgumentException>(debt.Validate);
    }

    [Fact]
    public void ABiggerBudgetClearsDebtSoonerAndCostsLessInterest()
    {
        var slow = PayoffPlanner.Plan(Debts(), new Money(400m), PayoffStrategy.Avalanche, Start);
        var fast = PayoffPlanner.Plan(Debts(), new Money(900m), PayoffStrategy.Avalanche, Start);

        Assert.True(fast.MonthsToClear < slow.MonthsToClear);
        Assert.True(fast.TotalInterest < slow.TotalInterest);
    }

    [Fact]
    public void PayingExtraOnOneDebtSavesInterestAndTime()
    {
        var card = Debt("Credit card", new Money(4000m), 22.9m, new Money(120m));

        var result = PayoffPlanner.ModelExtraPayment(card, new Money(100m));

        Assert.True(result.BaselineClears);
        Assert.True(result.AcceleratedClears);
        Assert.True(result.AcceleratedMonths < result.BaselineMonths);
        Assert.True(result.InterestSaved > Money.Zero);
        Assert.Equal(result.BaselineMonths - result.AcceleratedMonths, result.MonthsSaved);
        Assert.Contains("sooner", result.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PayingNothingExtraChangesNothing()
    {
        var card = Debt("Credit card", new Money(4000m), 22.9m, new Money(120m));

        var result = PayoffPlanner.ModelExtraPayment(card, Money.Zero);

        Assert.Equal(0, result.MonthsSaved);
        Assert.Equal(Money.Zero, result.InterestSaved);
        Assert.Equal(result.BaselineMonths, result.AcceleratedMonths);
    }

    [Fact]
    public void DebtToIncomeIsReportedAsAPercentage()
    {
        var ratio = PayoffPlanner.DebtToIncomeRatio(Debts(), new Money(4000m));

        // 120 + 60 + 210 of minimums against 4,000 of income.
        Assert.Equal(9.8m, ratio);
    }

    [Fact]
    public void DebtToIncomeIsZeroRatherThanInfiniteWithNoIncome()
    {
        Assert.Equal(0m, PayoffPlanner.DebtToIncomeRatio(Debts(), Money.Zero));
    }

    [Fact]
    public void AMinimumThatDoesNotCoverTheInterestIsSaidOutLoud()
    {
        // $50 a month against $249 of monthly interest means the balance never falls. Quoting a
        // term here would be a lie, so the result says so instead.
        var hopeless = Debt("Trap card", new Money(10_000m), 29.9m, new Money(50m));

        var result = PayoffPlanner.ModelExtraPayment(hopeless, new Money(500m));

        Assert.False(result.BaselineClears);
        Assert.True(result.AcceleratedClears);
        Assert.Equal(Money.Zero, result.InterestSaved);
        Assert.Equal(0, result.MonthsSaved);
        Assert.Contains("does not cover", result.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("never fall", result.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnExtraPaymentTooSmallToCoverTheInterestIsAlsoReported()
    {
        var hopeless = Debt("Trap card", new Money(10_000m), 29.9m, new Money(50m));

        var result = PayoffPlanner.ModelExtraPayment(hopeless, new Money(20m));

        Assert.False(result.BaselineClears);
        Assert.False(result.AcceleratedClears);
        Assert.Contains("still not fall", result.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ANegativeExtraPaymentIsRefused()
    {
        var card = Debt("Credit card", new Money(4000m), 22.9m, new Money(120m));

        Assert.Throws<ArgumentException>(() => PayoffPlanner.ModelExtraPayment(card, new Money(-50m)));
    }

    [Fact]
    public void ANegativeBalanceIsRefused()
    {
        var debt = Debt("Impossible", new Money(-100m), 10m, new Money(10m));

        Assert.Throws<ArgumentException>(debt.Validate);
    }

    [Fact]
    public void ADueDayOutsideTheMonthIsRefused()
    {
        var debt = Debt("Card", new Money(100m), 10m, new Money(10m)) with { DueDayOfMonth = 45 };

        Assert.Throws<ArgumentException>(debt.Validate);
    }

    private static List<DebtAccount> Debts() =>
    [
        Debt("Car loan", new Money(9800m), 7.4m, new Money(210m)),
        Debt("Credit card", new Money(2480m), 22.9m, new Money(120m)),
        Debt("Store card", new Money(600m), 27.9m, new Money(60m))
    ];

    private static DebtAccount Debt(string name, Money balance, decimal apr, Money minimum) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Kind = DebtKind.CreditCard,
        Balance = balance,
        AnnualPercentageRate = apr,
        MinimumPayment = minimum
    };
}
