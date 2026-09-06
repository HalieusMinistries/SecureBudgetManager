using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.Core.Planning;

/// <summary>
/// Everything needed to turn a car advert into a true cost. Anything left null stays an
/// unanswered question rather than being quietly assumed.
/// </summary>
public sealed record CarPurchaseInputs
{
    public required string Description { get; init; }

    public required DateOnly PurchaseDate { get; init; }

    /// <summary>The monthly payment being advertised or proposed.</summary>
    public Money AdvertisedMonthlyPayment { get; init; } = Money.Zero;

    public Money? VehiclePrice { get; init; }

    public Money? CashDownPayment { get; init; }

    public Money? TradeInValue { get; init; }

    /// <summary>Amount still owed on a car being traded in, which rolls into the new loan.</summary>
    public Money? OutstandingLoanOnTradeIn { get; init; }

    public decimal? SalesTaxPercent { get; init; }

    public Money? TitleAndRegistrationFees { get; init; }

    public Money? DealerAndDocumentFees { get; init; }

    public decimal? LoanAnnualPercentageRate { get; init; }

    public int? LoanTermMonths { get; init; }

    public Money? MonthlyInsuranceIncrease { get; init; }

    public Money? InsuranceDeductible { get; init; }

    public decimal? MilesDrivenPerMonth { get; init; }

    public decimal? MilesPerGallon { get; init; }

    public Money? FuelPricePerGallon { get; init; }

    public Money? AnnualMaintenanceCost { get; init; }

    public Money? MonthlyRepairReserve { get; init; }

    public Money? AnnualRegistrationRenewal { get; init; }

    public Money? AnnualInspectionCost { get; init; }

    public Money? MonthlyParkingCost { get; init; }

    public Money? MonthlyTollsAndWashes { get; init; }

    public decimal? AnnualDepreciationPercent { get; init; }

    public Money? MonthlyFinancingExtras { get; init; }

    public Money? MonthlyReplacementFund { get; init; }

    /// <summary>Return the household would have earned on the cash instead, as an annual percentage.</summary>
    public decimal? SavingsInterestRatePercent { get; init; }

    /// <summary>Monthly saving if another vehicle becomes unnecessary. Reduces the true cost.</summary>
    public Money? MonthlySavingFromReplacedVehicle { get; init; }

    /// <summary>Savings or extra debt repayment this money would otherwise have gone to.</summary>
    public Money? MonthlyOpportunityCost { get; init; }

    /// <summary>Used cars need a larger repair reserve, so the planner says so explicitly.</summary>
    public bool IsUsedVehicle { get; init; } = true;
}

public sealed record CarPurchasePlan
{
    public required Scenario Scenario { get; init; }

    public required IReadOnlyList<string> DerivedNotes { get; init; }

    public LoanSchedule? Loan { get; init; }

    /// <summary>Value the car is expected to have after the loan term, for negative-equity checks.</summary>
    public Money? ProjectedValueAtEndOfTerm { get; init; }

    public Money? NegativeEquityAtEndOfTerm { get; init; }
}

/// <summary>
/// Builds a car scenario from ordinary questions, deriving what can be derived and leaving the
/// rest as open questions.
///
/// The point is not to answer "can someone make a $235 payment?" but "what will this car truly cost,
/// what is missing, and what happens to the household afterwards?"
/// </summary>
public static class CarPurchasePlanner
{
    public static CarPurchasePlan Build(CarPurchaseInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputs.Description);

        var notes = new List<string>();
        var scenario = Scenario.FromTemplate(
            CostTemplateLibrary.CarOwnership,
            inputs.Description,
            inputs.PurchaseDate) with
        {
            AdvertisedAmount = inputs.AdvertisedMonthlyPayment,
            AdvertisedFrequency = Frequency.Monthly
        };

        scenario = ApplyPurchaseCosts(scenario, inputs, notes);
        var (withLoan, loan, projectedValue, negativeEquity) = ApplyLoan(scenario, inputs, notes);
        scenario = withLoan;
        scenario = ApplyRunningCosts(scenario, inputs, notes);
        scenario = ApplyNonCashCosts(scenario, inputs, notes, projectedValue);

        if (inputs.IsUsedVehicle && inputs.MonthlyRepairReserve is null)
        {
            notes.Add(
                "This is a used vehicle and no repair reserve has been entered. A used car typically " +
                "needs a larger reserve than a new one, so the true cost is understated until you add it.");
        }

        return new CarPurchasePlan
        {
            Scenario = scenario,
            DerivedNotes = notes,
            Loan = loan,
            ProjectedValueAtEndOfTerm = projectedValue,
            NegativeEquityAtEndOfTerm = negativeEquity
        };
    }

    private static Scenario ApplyPurchaseCosts(
        Scenario scenario,
        CarPurchaseInputs inputs,
        List<string> notes)
    {
        if (inputs.CashDownPayment is { } down)
        {
            scenario = scenario.WithLine("Cash down payment", down, Frequency.OneOff);
        }

        if (inputs.VehiclePrice is { } price && inputs.SalesTaxPercent is { } taxPercent)
        {
            var taxableAmount = inputs.TradeInValue is { } tradeIn
                ? Money.Max(Money.Zero, price - tradeIn)
                : price;

            var salesTax = (taxableAmount * (taxPercent / 100m)).Round();
            scenario = scenario.WithLine("Sales tax", salesTax, Frequency.OneOff);

            notes.Add(
                $"Sales tax of {salesTax.ToDisplayString()} was calculated at {taxPercent:0.##}% of " +
                $"{taxableAmount.ToDisplayString()}. Check whether your state taxes the trade-in value.");
        }

        if (inputs.TitleAndRegistrationFees is { } titleFees)
        {
            scenario = scenario.WithLine("Title and registration", titleFees, Frequency.OneOff);
        }

        if (inputs.DealerAndDocumentFees is { } dealerFees)
        {
            scenario = scenario.WithLine("Dealer and document fees", dealerFees, Frequency.OneOff);
        }

        return scenario;
    }

    private static (Scenario Scenario, LoanSchedule? Loan, Money? ProjectedValue, Money? NegativeEquity) ApplyLoan(
        Scenario scenario,
        CarPurchaseInputs inputs,
        List<string> notes)
    {
        if (inputs.VehiclePrice is not { } price
            || inputs.LoanAnnualPercentageRate is not { } apr
            || inputs.LoanTermMonths is not { } term)
        {
            if (!inputs.AdvertisedMonthlyPayment.IsZero)
            {
                // Fall back to the advertised payment so the plan is still usable.
                scenario = scenario.WithLine(
                    "Monthly loan payment",
                    inputs.AdvertisedMonthlyPayment,
                    Frequency.Monthly);

                notes.Add(
                    "The advertised monthly payment was used because the price, rate or term is missing. " +
                    "Total interest cannot be calculated without them, so the true cost is understated.");
            }

            return (scenario, null, null, null);
        }

        var financed = price;

        if (inputs.TradeInValue is { } tradeIn)
        {
            financed -= tradeIn;
        }

        if (inputs.CashDownPayment is { } down)
        {
            financed -= down;
        }

        // Negative equity on a trade-in is rolled into the new loan, which is easy to overlook.
        if (inputs.OutstandingLoanOnTradeIn is { } owed)
        {
            var equity = (inputs.TradeInValue ?? Money.Zero) - owed;
            if (equity.IsNegative)
            {
                financed += equity.Abs();
                notes.Add(
                    $"{equity.Abs().ToDisplayString()} of negative equity from the trade-in was added to the " +
                    "new loan. You will be paying interest on the old car as well as the new one.");
            }
        }

        // Sales tax and fees are commonly financed too.
        var salesTaxLine = scenario.Lines.First(line => line.Name == "Sales tax");
        if (salesTaxLine.CountsTowardsCost)
        {
            notes.Add(
                "Sales tax and fees are treated as paid up front. If you are financing them instead, " +
                "add them to the vehicle price so the interest is counted.");
        }

        financed = Money.Max(Money.Zero, financed);

        var terms = new LoanTerms
        {
            Principal = financed,
            AnnualPercentageRate = apr,
            TermMonths = term
        };

        var schedule = LoanMath.BuildSchedule(terms);

        scenario = scenario.WithLine("Monthly loan payment", schedule.MonthlyPayment, Frequency.Monthly);
        scenario = scenario.WithLine("Total interest", schedule.TotalInterest, Frequency.OneOff);

        notes.Add(
            $"Borrowing {financed.ToDisplayString()} at {apr:0.##}% over {term} months means a payment of " +
            $"{schedule.MonthlyPayment.ToDisplayString()} and {schedule.TotalInterest.ToDisplayString()} " +
            "of interest in total.");

        if (!inputs.AdvertisedMonthlyPayment.IsZero
            && Math.Abs(schedule.MonthlyPayment.Amount - inputs.AdvertisedMonthlyPayment.Amount) > 5m)
        {
            notes.Add(
                $"The advertised payment of {inputs.AdvertisedMonthlyPayment.ToDisplayString()} does not match " +
                $"the calculated {schedule.MonthlyPayment.ToDisplayString()}. " +
                "A quoted payment that looks lower usually means a longer term or a larger deposit.");
        }

        Money? projectedValue = null;
        Money? negativeEquity = null;

        if (inputs.AnnualDepreciationPercent is { } depreciation)
        {
            var years = term / 12m;
            projectedValue = LoanMath.DepreciatedValue(price, depreciation, years);

            var balanceAtEnd = schedule.BalanceAfter(term);
            var gap = (balanceAtEnd - projectedValue.Value).Round();

            if (gap > Money.Zero)
            {
                negativeEquity = gap;
                scenario = scenario.WithLine("Amount owed above the car's value", gap, Frequency.OneOff);

                notes.Add(
                    $"After {term} months the car is projected to be worth " +
                    $"{projectedValue.Value.ToDisplayString()} while you would still owe " +
                    $"{balanceAtEnd.ToDisplayString()}. That is {gap.ToDisplayString()} of negative equity.");
            }
            else
            {
                negativeEquity = Money.Zero;
                scenario = scenario.WithLineNotApplicable("Amount owed above the car's value");
            }
        }

        return (scenario, schedule, projectedValue, negativeEquity);
    }

    private static Scenario ApplyRunningCosts(
        Scenario scenario,
        CarPurchaseInputs inputs,
        List<string> notes)
    {
        if (inputs.MonthlyInsuranceIncrease is { } insurance)
        {
            scenario = scenario.WithLine("Premium increase", insurance, Frequency.Monthly);
        }

        if (inputs.InsuranceDeductible is { } deductible)
        {
            scenario = scenario.WithLine("Deductible reserve", deductible, Frequency.Annual);
            notes.Add(
                $"A {deductible.ToDisplayString()} deductible is money you must be able to find after an " +
                "accident. It is counted as an annual reserve, not an expected cost.");
        }

        if (inputs.MilesDrivenPerMonth is { } miles
            && inputs.MilesPerGallon is { } mpg
            && inputs.FuelPricePerGallon is { } fuelPrice
            && mpg > 0m)
        {
            var gallons = miles / mpg;
            var fuelCost = (fuelPrice * gallons).Round();
            scenario = scenario.WithLine("Fuel", fuelCost, Frequency.Monthly);

            notes.Add(
                $"Fuel is {fuelCost.ToDisplayString()} a month: {miles:0.#} miles at {mpg:0.#} mpg " +
                $"is {gallons:0.#} gallons at {fuelPrice.ToDisplayString()} each.");
        }

        if (inputs.AnnualMaintenanceCost is { } maintenance)
        {
            scenario = scenario.WithLine("Routine servicing", maintenance, Frequency.Annual);
        }

        if (inputs.MonthlyRepairReserve is { } repairs)
        {
            scenario = scenario.WithLine("Repair reserve", repairs, Frequency.Monthly);
        }

        if (inputs.AnnualRegistrationRenewal is { } registration)
        {
            scenario = scenario.WithLine("Annual renewal", registration, Frequency.Annual);
        }

        if (inputs.AnnualInspectionCost is { } inspection)
        {
            scenario = scenario.WithLine("Inspection and emissions", inspection, Frequency.Annual);
        }

        if (inputs.MonthlyParkingCost is { } parking)
        {
            scenario = scenario.WithLine("Parking and permits", parking, Frequency.Monthly);
        }

        if (inputs.MonthlyTollsAndWashes is { } tolls)
        {
            scenario = scenario.WithLine("Tolls, washes, roadside assistance", tolls, Frequency.Monthly);
        }

        if (inputs.MonthlyFinancingExtras is { } extras)
        {
            scenario = scenario.WithLine("GAP, warranties and add-ons", extras, Frequency.Monthly);
        }

        if (inputs.MonthlyReplacementFund is { } replacement)
        {
            scenario = scenario.WithLine("Replacement sinking fund", replacement, Frequency.Monthly);
        }

        if (inputs.MonthlySavingFromReplacedVehicle is { } saving)
        {
            // Recorded as a note rather than a negative cost line, because cost lines cannot be negative.
            scenario = scenario.WithLineNotApplicable("Savings from giving up another vehicle");
            notes.Add(
                $"Giving up another vehicle saves {saving.ToDisplayString()} a month. " +
                "Reduce or pause that vehicle's expenses in the Expenses page so the saving is real, " +
                "not just assumed.");
        }

        return scenario;
    }

    private static Scenario ApplyNonCashCosts(
        Scenario scenario,
        CarPurchaseInputs inputs,
        List<string> notes,
        Money? projectedValue)
    {
        if (inputs.VehiclePrice is { } price && inputs.AnnualDepreciationPercent is { } depreciationPercent)
        {
            var valueAfterOneYear = LoanMath.DepreciatedValue(price, depreciationPercent, 1m);
            var firstYearLoss = (price - valueAfterOneYear).Round();

            scenario = scenario.WithLine("Loss of resale value", firstYearLoss, Frequency.Annual);

            notes.Add(
                $"Depreciation of {firstYearLoss.ToDisplayString()} in the first year is the largest single " +
                "cost of most cars, even though no money leaves the account.");

            _ = projectedValue;
        }

        if (inputs.CashDownPayment is { } down
            && inputs.SavingsInterestRatePercent is { } savingsRate
            && down > Money.Zero
            && savingsRate > 0m)
        {
            var forgoneInterest = (down * (savingsRate / 100m)).Round();
            scenario = scenario.WithLine("Lost savings interest", forgoneInterest, Frequency.Annual);

            notes.Add(
                $"The {down.ToDisplayString()} deposit would have earned about " +
                $"{forgoneInterest.ToDisplayString()} a year at {savingsRate:0.##}%.");
        }

        if (inputs.MonthlyOpportunityCost is { } opportunity)
        {
            scenario = scenario.WithLine(
                "Savings or debt repayment forgone",
                opportunity,
                Frequency.Monthly);
        }

        return scenario;
    }

    /// <summary>
    /// Works backwards from a payment to what it can actually finance, which is the honest answer
    /// to "we can afford $235 a month".
    /// </summary>
    public static Money AffordableVehiclePrice(
        Money monthlyPayment,
        decimal annualPercentageRate,
        int termMonths,
        Money downPayment,
        decimal salesTaxPercent)
    {
        var financeable = LoanMath.PrincipalForPayment(monthlyPayment, annualPercentageRate, termMonths);
        var beforeTax = financeable + downPayment;

        // The advertised price plus tax must fit inside what can be financed.
        return (beforeTax / (1m + (salesTaxPercent / 100m))).Round();
    }
}
