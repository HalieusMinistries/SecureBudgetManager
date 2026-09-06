using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Planning;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.Tests.Planning;

/// <summary>
/// The true-cost engine exists to answer one question honestly: what does this actually cost,
/// as opposed to what is advertised?
/// </summary>
public sealed class TrueCostEngineTests
{
    [Fact]
    public void AnUnansweredCriticalCostBlocksAnyVerdict()
    {
        var scenario = NewCarScenario();

        var breakdown = TrueCostEngine.Calculate(scenario, ScenarioCase.Expected);

        Assert.False(breakdown.HasEnoughInformation);
        Assert.NotEmpty(breakdown.MissingCriticalLines);
    }

    [Fact]
    public void AnsweringEveryCriticalCostAllowsAVerdict()
    {
        var scenario = FullyAnsweredCar();

        var breakdown = TrueCostEngine.Calculate(scenario, ScenarioCase.Expected);

        Assert.True(breakdown.HasEnoughInformation);
        Assert.Empty(breakdown.MissingCriticalLines);
    }

    [Fact]
    public void MarkingACostNotApplicableCountsAsAnswered()
    {
        var scenario = FullyAnsweredCar();
        var withoutFuel = scenario.WithLineNotApplicable("Fuel");

        var breakdown = TrueCostEngine.Calculate(withoutFuel, ScenarioCase.Expected);

        Assert.True(breakdown.HasEnoughInformation);
        Assert.True(breakdown.MonthlyCashCost < TrueCostEngine
            .Calculate(scenario, ScenarioCase.Expected).MonthlyCashCost);
    }

    [Fact]
    public void TheTrueMonthlyCostFarExceedsTheAdvertisedPayment()
    {
        var scenario = FullyAnsweredCar();

        var breakdown = TrueCostEngine.Calculate(scenario, ScenarioCase.Expected);

        Assert.Equal(new Money(235m), breakdown.AdvertisedMonthly);
        Assert.True(
            breakdown.MonthlyTrueCost > breakdown.AdvertisedMonthly,
            "A car with insurance, fuel and maintenance cannot cost only its loan payment.");

        Assert.True(breakdown.HiddenMonthlyCost > Money.Zero);
        Assert.True(breakdown.HiddenCostMultiple > 1m);
    }

    [Fact]
    public void OneOffCostsBecomeUpfrontCashRatherThanMonthlyCost()
    {
        var scenario = FullyAnsweredCar();

        var breakdown = TrueCostEngine.Calculate(scenario, ScenarioCase.Expected);

        // 2,000 deposit + 1,400 sales tax + 320 title + 180 dealer fees.
        Assert.Equal(new Money(3900m), breakdown.UpfrontCash);
    }

    [Fact]
    public void NonCashCostsAreExcludedFromTheCashFigureButIncludedInTheTrueCost()
    {
        var scenario = FullyAnsweredCar();

        var breakdown = TrueCostEngine.Calculate(scenario, ScenarioCase.Expected);

        Assert.True(
            breakdown.MonthlyTrueCost > breakdown.MonthlyCashCost,
            "Depreciation belongs in the true cost but must not appear as cash leaving the account.");

        Assert.Contains(breakdown.ByArea, area => area.IsNonCash);
    }

    [Fact]
    public void AnnualCostsAreSpreadIntoAMonthlyFigure()
    {
        var scenario = NewCarScenario()
            .WithLine("Annual renewal", new Money(120m), Frequency.Annual);

        var breakdown = TrueCostEngine.Calculate(scenario, ScenarioCase.Expected);
        var registration = breakdown.ByArea.Single(area => area.Area == "Registration");

        Assert.Equal(new Money(10m), registration.MonthlyAmount);
        Assert.Equal(new Money(120m), registration.AnnualAmount);
    }

    [Fact]
    public void IrregularCostsBecomeARecommendedReserve()
    {
        var scenario = FullyAnsweredCar();

        var breakdown = TrueCostEngine.Calculate(scenario, ScenarioCase.Expected);

        // Maintenance, repairs, insurance, registration and replacement are unpredictable, so
        // they need money set aside rather than a monthly bill line.
        Assert.True(breakdown.RecommendedMonthlyReserve > Money.Zero);
        Assert.True(breakdown.RecommendedMonthlyReserve <= breakdown.MonthlyCashCost);
    }

    [Fact]
    public void TheWorstCaseCostsMoreAndTheBestCaseLess()
    {
        var scenario = FullyAnsweredCar();
        var cases = TrueCostEngine.CalculateAllCases(scenario);

        Assert.True(cases[ScenarioCase.Best].MonthlyCashCost < cases[ScenarioCase.Expected].MonthlyCashCost);
        Assert.True(cases[ScenarioCase.Worst].MonthlyCashCost > cases[ScenarioCase.Expected].MonthlyCashCost);
    }

    [Fact]
    public void TheCaseMultiplierDoesNotScaleAOneOffDeposit()
    {
        var scenario = FullyAnsweredCar();
        var cases = TrueCostEngine.CalculateAllCases(scenario);

        // A deposit is a known number, so it must be identical in every case.
        Assert.Equal(cases[ScenarioCase.Expected].UpfrontCash, cases[ScenarioCase.Worst].UpfrontCash);
        Assert.Equal(cases[ScenarioCase.Expected].UpfrontCash, cases[ScenarioCase.Best].UpfrontCash);
    }

    [Fact]
    public void MultiYearTotalsIncludeTheUpfrontCash()
    {
        var scenario = FullyAnsweredCar();
        var breakdown = TrueCostEngine.Calculate(scenario, ScenarioCase.Expected);

        var oneYear = breakdown.CashCostOverYears(1);
        var threeYears = breakdown.CashCostOverYears(3);
        var fiveYears = breakdown.CashCostOverYears(5);

        Assert.Equal((breakdown.UpfrontCash + breakdown.AnnualCashCost).Round(), oneYear);
        Assert.True(threeYears > oneYear);
        Assert.True(fiveYears > threeYears);

        // Five years of a $235 payment is over $14,000 before a single other cost.
        Assert.True(fiveYears > new Money(14_000m));
    }

    [Fact]
    public void AreasAreOrderedByAnnualCostSoTheBiggestIsFirst()
    {
        var breakdown = TrueCostEngine.Calculate(FullyAnsweredCar(), ScenarioCase.Expected);

        var amounts = breakdown.ByArea.Select(area => area.AnnualAmount.Amount).ToList();

        Assert.Equal(amounts.OrderByDescending(amount => amount), amounts);
    }

    [Fact]
    public void ZeroYearsIsRefusedRatherThanReturningTheUpfrontCostAlone()
    {
        var breakdown = TrueCostEngine.Calculate(FullyAnsweredCar(), ScenarioCase.Expected);

        Assert.Throws<ArgumentOutOfRangeException>(() => breakdown.CashCostOverYears(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => breakdown.TrueCostOverYears(-1));
    }

    [Fact]
    public void AScenarioWithNoLinesIsRefused()
    {
        var scenario = new Scenario
        {
            Id = Guid.NewGuid(),
            Name = "Empty",
            Kind = ScenarioKind.Custom,
            ProposedStartDate = new DateOnly(2026, 3, 1),
            Lines = []
        };

        Assert.Throws<ArgumentException>(() => TrueCostEngine.Calculate(scenario, ScenarioCase.Expected));
    }

    internal static Scenario NewCarScenario() =>
        Scenario.FromTemplate(
            CostTemplateLibrary.CarOwnership,
            "Sam's car",
            new DateOnly(2026, 3, 1)) with
        {
            AdvertisedAmount = new Money(235m),
            AdvertisedFrequency = Frequency.Monthly
        };

    /// <summary>
    /// A realistic used car at the advertised $235 a month, with every critical cost answered.
    /// </summary>
    internal static Scenario FullyAnsweredCar() =>
        NewCarScenario()
            .WithLine("Cash down payment", new Money(2000m), Frequency.OneOff)
            .WithLine("Sales tax", new Money(1400m), Frequency.OneOff)
            .WithLine("Title and registration", new Money(320m), Frequency.OneOff)
            .WithLine("Dealer and document fees", new Money(180m), Frequency.OneOff)
            .WithLine("Monthly loan payment", new Money(235m), Frequency.Monthly)
            .WithLine("Total interest", new Money(2145m), Frequency.OneOff)
            .WithLine("Premium increase", new Money(95m), Frequency.Monthly)
            .WithLine("Deductible reserve", new Money(1000m), Frequency.Annual)
            .WithLine("Fuel", new Money(140m), Frequency.Monthly)
            .WithLine("Routine servicing", new Money(600m), Frequency.Annual)
            .WithLine("Repair reserve", new Money(75m), Frequency.Monthly)
            .WithLine("Annual renewal", new Money(96m), Frequency.Annual)
            .WithLine("Inspection and emissions", new Money(45m), Frequency.Annual)
            .WithLineNotApplicable("Parking and permits")
            .WithLine("Tolls, washes, roadside assistance", new Money(25m), Frequency.Monthly)
            .WithLine("Loss of resale value", new Money(2400m), Frequency.Annual)
            .WithLineNotApplicable("Amount owed above the car's value")
            .WithLineNotApplicable("GAP, warranties and add-ons")
            .WithLine("Replacement sinking fund", new Money(50m), Frequency.Monthly)
            .WithLine("Lost savings interest", new Money(90m), Frequency.Annual)
            .WithLine("Savings or debt repayment forgone", new Money(100m), Frequency.Monthly)
            .WithLineNotApplicable("Savings from giving up another vehicle");
}
