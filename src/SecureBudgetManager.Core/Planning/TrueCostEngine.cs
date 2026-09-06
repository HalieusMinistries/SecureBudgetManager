using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.Core.Planning;

public sealed record TrueCostBreakdown
{
    public required ScenarioCase Case { get; init; }

    /// <summary>What the seller quotes, converted to a monthly figure for comparison.</summary>
    public required Money AdvertisedMonthly { get; init; }

    /// <summary>Cash needed before the first payment: deposit, taxes, fees, setup.</summary>
    public required Money UpfrontCash { get; init; }

    /// <summary>Recurring cash cost per month, excluding non-cash items.</summary>
    public required Money MonthlyCashCost { get; init; }

    /// <summary>Monthly cost including depreciation and opportunity cost.</summary>
    public required Money MonthlyTrueCost { get; init; }

    public required Money AnnualCashCost { get; init; }

    public required Money AnnualTrueCost { get; init; }

    /// <summary>Recommended monthly reserve for the irregular costs in this decision.</summary>
    public required Money RecommendedMonthlyReserve { get; init; }

    public required IReadOnlyList<CostAreaTotal> ByArea { get; init; }

    public required IReadOnlyList<CostLine> MissingLines { get; init; }

    public required IReadOnlyList<CostLine> MissingCriticalLines { get; init; }

    /// <summary>How much the true cost exceeds the advertised figure.</summary>
    public Money HiddenMonthlyCost => (MonthlyTrueCost - AdvertisedMonthly).Round();

    public decimal HiddenCostMultiple => AdvertisedMonthly.IsZero
        ? 0m
        : Math.Round(MonthlyTrueCost.Amount / AdvertisedMonthly.Amount, 2);

    public bool HasEnoughInformation => MissingCriticalLines.Count == 0;

    /// <summary>Total cash out over a number of years, including up-front cash.</summary>
    public Money CashCostOverYears(int years)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(years);
        return (UpfrontCash + (AnnualCashCost * years)).Round();
    }

    public Money TrueCostOverYears(int years)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(years);
        return (UpfrontCash + (AnnualTrueCost * years)).Round();
    }
}

public sealed record CostAreaTotal(string Area, Money MonthlyAmount, Money AnnualAmount, bool IsNonCash);

/// <summary>
/// Turns a cost checklist into the real monthly cost of a decision.
///
/// Two separations matter. Cash costs are kept apart from non-cash costs such as depreciation, so
/// the cash-flow calendar stays truthful while the true cost of ownership stays honest. Irregular
/// costs are converted into a recommended monthly reserve, because a tyre replacement does not
/// arrive in convenient monthly instalments.
/// </summary>
public static class TrueCostEngine
{
    /// <summary>Costs that arrive unpredictably and therefore need a reserve rather than a budget line.</summary>
    private static readonly string[] ReserveAreas =
    [
        "Repairs",
        "Maintenance",
        "Registration",
        "Insurance",
        "Replacement",
        "Health",
        "Exposure"
    ];

    public static TrueCostBreakdown Calculate(Scenario scenario, ScenarioCase scenarioCase)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        scenario.Validate();

        var multiplier = CaseMultiplier(scenario, scenarioCase);

        var upfront = Money.Zero;
        var monthlyCash = Money.Zero;
        var monthlyNonCash = Money.Zero;
        var reserve = Money.Zero;
        var areaTotals = new Dictionary<string, (decimal Monthly, decimal Annual, bool NonCash)>();

        foreach (var line in scenario.Lines)
        {
            if (!line.CountsTowardsCost)
            {
                continue;
            }

            // One-off costs are not scaled by the case multiplier: a deposit is a known number.
            if (line.Frequency == Frequency.OneOff)
            {
                if (line.IsNonCash)
                {
                    // Total interest and negative equity are real costs but not cash at purchase.
                    continue;
                }

                upfront += line.UpfrontAmount;
                Accumulate(areaTotals, line.Area, 0m, line.UpfrontAmount.Amount, line.IsNonCash);
                continue;
            }

            var scaled = line.MonthlyAmount * multiplier;

            if (line.IsNonCash)
            {
                monthlyNonCash += scaled;
            }
            else
            {
                monthlyCash += scaled;
            }

            if (IsReserveArea(line.Area) && !line.IsNonCash)
            {
                reserve += scaled;
            }

            Accumulate(areaTotals, line.Area, scaled.Amount, scaled.Amount * 12m, line.IsNonCash);
        }

        var monthlyTrue = monthlyCash + monthlyNonCash;

        var byArea = areaTotals
            .Select(entry => new CostAreaTotal(
                entry.Key,
                new Money(entry.Value.Monthly).Round(),
                new Money(entry.Value.Annual).Round(),
                entry.Value.NonCash))
            .OrderByDescending(area => area.AnnualAmount.Amount)
            .ToList();

        var missing = scenario.Lines.Where(line => line.IsMissing).ToList();

        return new TrueCostBreakdown
        {
            Case = scenarioCase,
            AdvertisedMonthly = FrequencyConverter
                .ToMonthly(scenario.AdvertisedAmount, scenario.AdvertisedFrequency)
                .Round(),
            UpfrontCash = upfront.Round(),
            MonthlyCashCost = monthlyCash.Round(),
            MonthlyTrueCost = monthlyTrue.Round(),
            AnnualCashCost = (monthlyCash * 12m).Round(),
            AnnualTrueCost = (monthlyTrue * 12m).Round(),
            RecommendedMonthlyReserve = reserve.Round(),
            ByArea = byArea,
            MissingLines = missing,
            MissingCriticalLines = missing.Where(line => line.IsCritical).ToList()
        };
    }

    /// <summary>Calculates all three cases at once for side-by-side comparison.</summary>
    public static IReadOnlyDictionary<ScenarioCase, TrueCostBreakdown> CalculateAllCases(Scenario scenario) =>
        Enum.GetValues<ScenarioCase>()
            .ToDictionary(scenarioCase => scenarioCase, scenarioCase => Calculate(scenario, scenarioCase));

    private static decimal CaseMultiplier(Scenario scenario, ScenarioCase scenarioCase) => scenarioCase switch
    {
        ScenarioCase.Best => 1m - (scenario.BestCaseReductionPercent / 100m),
        ScenarioCase.Expected => 1m,
        ScenarioCase.Worst => 1m + (scenario.WorstCaseUpliftPercent / 100m),
        _ => 1m
    };

    private static bool IsReserveArea(string area) =>
        ReserveAreas.Any(reserveArea => area.Contains(reserveArea, StringComparison.OrdinalIgnoreCase));

    private static void Accumulate(
        Dictionary<string, (decimal Monthly, decimal Annual, bool NonCash)> totals,
        string area,
        decimal monthly,
        decimal annual,
        bool nonCash)
    {
        if (totals.TryGetValue(area, out var existing))
        {
            totals[area] = (existing.Monthly + monthly, existing.Annual + annual, existing.NonCash && nonCash);
        }
        else
        {
            totals[area] = (monthly, annual, nonCash);
        }
    }
}
