using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Planning;

namespace SecureBudgetManager.Tests.Planning;

/// <summary>
/// The car planner is the reference planning feature. The question it answers is not
/// "can someone make a $235 payment?" but "what will this car truly cost us?"
/// </summary>
public sealed class CarPurchasePlannerTests
{
    [Fact]
    public void TheAdvertisedPaymentAloneProducesAWarningAboutMissingCosts()
    {
        var plan = CarPurchasePlanner.Build(new CarPurchaseInputs
        {
            Description = "Used sedan",
            PurchaseDate = new DateOnly(2026, 3, 1),
            AdvertisedMonthlyPayment = new Money(235m)
        });

        var breakdown = TrueCostEngine.Calculate(plan.Scenario, ScenarioCase.Expected);

        Assert.False(breakdown.HasEnoughInformation);
        Assert.Contains(plan.DerivedNotes, note => note.Contains("total interest cannot be calculated",
            StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AUsedCarWithNoRepairReserveIsCalledOut()
    {
        var plan = CarPurchasePlanner.Build(new CarPurchaseInputs
        {
            Description = "Used sedan",
            PurchaseDate = new DateOnly(2026, 3, 1),
            AdvertisedMonthlyPayment = new Money(235m),
            IsUsedVehicle = true
        });

        Assert.Contains(plan.DerivedNotes, note => note.Contains("used vehicle", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TheLoanPaymentAndTotalInterestAreDerivedFromThePriceRateAndTerm()
    {
        var plan = CarPurchasePlanner.Build(TypicalCar());

        var schedule = Assert.IsType<LoanSchedule>(plan.Loan);

        // 16,000 financed at 9.5% over 60 months.
        Assert.Equal(60, schedule.TermMonths);
        Assert.True(schedule.MonthlyPayment > new Money(330m));
        Assert.True(schedule.MonthlyPayment < new Money(345m));
        Assert.True(schedule.TotalInterest > new Money(4000m));
    }

    [Fact]
    public void AnAdvertisedPaymentBelowTheRealPaymentIsFlagged()
    {
        var plan = CarPurchasePlanner.Build(TypicalCar());

        // The dealer quoted $235 but the arithmetic gives about $336.
        Assert.Contains(
            plan.DerivedNotes,
            note => note.Contains("does not match", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SalesTaxIsCalculatedAndExplained()
    {
        var plan = CarPurchasePlanner.Build(TypicalCar());

        var salesTax = plan.Scenario.Lines.Single(line => line.Name == "Sales tax");

        // 6% of the 20,000 price less the 2,000 trade-in.
        Assert.Equal(new Money(1080m), salesTax.Amount);
        Assert.Contains(plan.DerivedNotes, note => note.Contains("Sales tax of", StringComparison.Ordinal));
    }

    [Fact]
    public void FuelIsDerivedFromMileageEconomyAndPrice()
    {
        var plan = CarPurchasePlanner.Build(TypicalCar());

        var fuel = plan.Scenario.Lines.Single(line => line.Name == "Fuel");

        // 900 miles at 28 mpg is 32.14 gallons at $3.40.
        Assert.Equal(new Money(109.29m), fuel.Amount);
        Assert.Contains(plan.DerivedNotes, note => note.Contains("Fuel is", StringComparison.Ordinal));
    }

    [Fact]
    public void NegativeEquityOnATradeInIsRolledIntoTheNewLoanAndExplained()
    {
        var plan = CarPurchasePlanner.Build(TypicalCar() with
        {
            TradeInValue = new Money(2000m),
            OutstandingLoanOnTradeIn = new Money(5000m)
        });

        var schedule = Assert.IsType<LoanSchedule>(plan.Loan);

        Assert.Contains(
            plan.DerivedNotes,
            note => note.Contains("negative equity from the trade-in", StringComparison.OrdinalIgnoreCase));

        // The $3,000 shortfall must be financed, so the payment rises.
        var withoutShortfall = CarPurchasePlanner.Build(TypicalCar()).Loan!;
        Assert.True(schedule.MonthlyPayment > withoutShortfall.MonthlyPayment);
    }

    [Fact]
    public void OwingMoreThanTheCarIsWorthIsDetected()
    {
        var plan = CarPurchasePlanner.Build(TypicalCar() with
        {
            LoanTermMonths = 84,
            AnnualDepreciationPercent = 20m
        });

        Assert.NotNull(plan.ProjectedValueAtEndOfTerm);
        Assert.NotNull(plan.NegativeEquityAtEndOfTerm);
    }

    [Fact]
    public void DepreciationIsRecordedAsANonCashCost()
    {
        var plan = CarPurchasePlanner.Build(TypicalCar());

        var depreciation = plan.Scenario.Lines.Single(line => line.Name == "Loss of resale value");

        Assert.True(depreciation.IsNonCash);
        Assert.Equal(CostLineState.Provided, depreciation.State);

        // 15% of a 20,000 car is 3,000 in the first year.
        Assert.Equal(new Money(3000m), depreciation.Amount);
    }

    [Fact]
    public void ForgoneSavingsInterestOnTheDepositIsCounted()
    {
        var plan = CarPurchasePlanner.Build(TypicalCar());

        var lost = plan.Scenario.Lines.Single(line => line.Name == "Lost savings interest");

        // 4% on a 2,000 deposit.
        Assert.Equal(new Money(80m), lost.Amount);
        Assert.True(lost.IsNonCash);
    }

    [Fact]
    public void GivingUpAnotherVehicleIsNotSilentlyTreatedAsASaving()
    {
        var plan = CarPurchasePlanner.Build(TypicalCar() with
        {
            MonthlySavingFromReplacedVehicle = new Money(180m)
        });

        var line = plan.Scenario.Lines.Single(item => item.Name == "Savings from giving up another vehicle");

        // A cost line cannot be negative, so the saving becomes an instruction rather than a fudge.
        Assert.Equal(CostLineState.NotApplicable, line.State);
        Assert.Contains(
            plan.DerivedNotes,
            note => note.Contains("so the saving is real", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ADeductibleIsTreatedAsAReserveNotAnExpectedCost()
    {
        var plan = CarPurchasePlanner.Build(TypicalCar());

        Assert.Contains(
            plan.DerivedNotes,
            note => note.Contains("not an expected cost", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AFullyAnsweredCarProducesACompleteTrueCost()
    {
        var plan = CarPurchasePlanner.Build(TypicalCar());

        var breakdown = TrueCostEngine.Calculate(plan.Scenario, ScenarioCase.Expected);

        Assert.True(breakdown.HasEnoughInformation);
        Assert.True(breakdown.MonthlyTrueCost > new Money(600m));
        Assert.True(breakdown.UpfrontCash > new Money(3000m));
    }

    [Fact]
    public void WorkingBackwardsFromAPaymentGivesAnHonestPrice()
    {
        // The real answer to "we can afford $235 a month".
        var price = CarPurchasePlanner.AffordableVehiclePrice(
            monthlyPayment: new Money(235m),
            annualPercentageRate: 9.5m,
            termMonths: 60,
            downPayment: new Money(2000m),
            salesTaxPercent: 6m);

        Assert.True(price > new Money(12_000m));
        Assert.True(price < new Money(13_500m));
    }

    [Fact]
    public void ALongerTermBuysAMoreExpensiveCarAtTheSamePayment()
    {
        var overFive = CarPurchasePlanner.AffordableVehiclePrice(
            new Money(235m), 9.5m, 60, new Money(2000m), 6m);

        var overSeven = CarPurchasePlanner.AffordableVehiclePrice(
            new Money(235m), 9.5m, 84, new Money(2000m), 6m);

        // This is how a payment is made to "fit", and why total interest must be shown.
        Assert.True(overSeven > overFive);
    }

    [Fact]
    public void ADescriptionIsRequired()
    {
        Assert.Throws<ArgumentException>(() => CarPurchasePlanner.Build(new CarPurchaseInputs
        {
            Description = "  ",
            PurchaseDate = new DateOnly(2026, 3, 1)
        }));
    }

    private static CarPurchaseInputs TypicalCar() => new()
    {
        Description = "2019 sedan",
        PurchaseDate = new DateOnly(2026, 3, 1),
        AdvertisedMonthlyPayment = new Money(235m),
        VehiclePrice = new Money(20_000m),
        CashDownPayment = new Money(2000m),
        TradeInValue = new Money(2000m),
        SalesTaxPercent = 6m,
        TitleAndRegistrationFees = new Money(320m),
        DealerAndDocumentFees = new Money(180m),
        LoanAnnualPercentageRate = 9.5m,
        LoanTermMonths = 60,
        MonthlyInsuranceIncrease = new Money(95m),
        InsuranceDeductible = new Money(1000m),
        MilesDrivenPerMonth = 900m,
        MilesPerGallon = 28m,
        FuelPricePerGallon = new Money(3.4m),
        AnnualMaintenanceCost = new Money(600m),
        MonthlyRepairReserve = new Money(75m),
        AnnualRegistrationRenewal = new Money(96m),
        AnnualInspectionCost = new Money(45m),
        MonthlyTollsAndWashes = new Money(25m),
        AnnualDepreciationPercent = 15m,
        MonthlyReplacementFund = new Money(50m),
        SavingsInterestRatePercent = 4m,
        MonthlyOpportunityCost = new Money(100m),
        IsUsedVehicle = true
    };
}
