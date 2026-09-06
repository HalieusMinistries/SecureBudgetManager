using SecureBudgetManager.Core.CashFlow;
using SecureBudgetManager.Core.Expenses;
using SecureBudgetManager.Core.Models;

namespace SecureBudgetManager.Core.Planning;

public enum AffordabilityRating
{
    /// <summary>Fits with the desired reserve and savings intact.</summary>
    Comfortable = 0,

    /// <summary>Fits, but reduces discretionary money or savings.</summary>
    Manageable = 1,

    /// <summary>Little margin for irregular costs.</summary>
    Tight = 2,

    /// <summary>Causes a deficit, drains reserves, or depends on additional debt.</summary>
    Unsafe = 3,

    /// <summary>Important costs have not been entered, so no honest verdict is possible.</summary>
    InsufficientInformation = 4
}

/// <summary>
/// One rule that was checked, with the figures that decided it. Every rating must be explainable.
/// </summary>
public sealed record AffordabilityFinding(
    string Rule,
    bool Passed,
    AffordabilityRating Severity,
    string Explanation);

public sealed record AffordabilityAssessment
{
    public required AffordabilityRating Rating { get; init; }

    public required string Headline { get; init; }

    public required IReadOnlyList<AffordabilityFinding> Findings { get; init; }

    public required TrueCostBreakdown TrueCost { get; init; }

    /// <summary>Lowest projected balance once the decision is included.</summary>
    public required Money LowestProjectedBalance { get; init; }

    public required Money EmergencyFundAfterPurchase { get; init; }

    public required decimal EmergencyFundMonthsAfterPurchase { get; init; }

    public required Money DiscretionaryIncomeAfter { get; init; }

    /// <summary>Concrete changes that would make the decision affordable.</summary>
    public required IReadOnlyList<string> WhatMustChange { get; init; }

    public IReadOnlyList<AffordabilityFinding> FailedRules =>
        Findings.Where(finding => !finding.Passed).ToList();

    public string RatingLabel => Rating switch
    {
        AffordabilityRating.Comfortable => "Comfortable",
        AffordabilityRating.Manageable => "Manageable",
        AffordabilityRating.Tight => "Tight",
        AffordabilityRating.Unsafe => "Unsafe",
        AffordabilityRating.InsufficientInformation => "Insufficient information",
        _ => "Unknown"
    };
}

public sealed record AffordabilityInputs
{
    public required Scenario Scenario { get; init; }

    public required ScenarioCase Case { get; init; }

    /// <summary>Projection that already includes the scenario's recurring costs.</summary>
    public required CashFlowProjection ProjectionWithScenario { get; init; }

    /// <summary>Baseline projection without the scenario, for comparison.</summary>
    public CashFlowProjection? BaselineProjection { get; init; }

    public required Money EmergencyFundBalance { get; init; }

    /// <summary>Monthly total of essential spending, used to express the emergency fund in months.</summary>
    public required Money MonthlyEssentialSpending { get; init; }

    public required Money MonthlyNetIncome { get; init; }

    /// <summary>Existing monthly debt payments, before this decision.</summary>
    public Money ExistingMonthlyDebtPayments { get; init; } = Money.Zero;

    public Money MinimumBalanceReserve { get; init; } = Money.Zero;

    public Money MinimumBreathingRoom { get; init; } = new(200m);

    public decimal EmergencyFundTargetMonths { get; init; } = 3m;

    /// <summary>Set when income varies, which raises the bar for a comfortable verdict.</summary>
    public bool IncomeIsVariable { get; init; }

    /// <summary>Monthly retirement contributions the household wants to protect.</summary>
    public Money MonthlyRetirementContributions { get; init; } = Money.Zero;
}

/// <summary>
/// Decides whether a decision is affordable, and explains which figures decided it.
///
/// The central rule: a purchase is never called affordable merely because the monthly payment
/// fits. Cash flow, reserves, worst case, annual cost and retirement contributions all count.
/// </summary>
public static class AffordabilityEngine
{
    public static AffordabilityAssessment Assess(AffordabilityInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        var trueCost = TrueCostEngine.Calculate(inputs.Scenario, inputs.Case);
        var findings = new List<AffordabilityFinding>();
        var whatMustChange = new List<string>();

        // Rule 0: refuse to judge on incomplete information.
        if (!trueCost.HasEnoughInformation)
        {
            var missing = string.Join(", ", trueCost.MissingCriticalLines.Select(line => line.Name));
            findings.Add(new AffordabilityFinding(
                "Required costs entered",
                false,
                AffordabilityRating.InsufficientInformation,
                $"{trueCost.MissingCriticalLines.Count} important cost(s) have not been entered: {missing}."));

            whatMustChange.Add($"Enter the missing costs: {missing}.");

            return Build(
                AffordabilityRating.InsufficientInformation,
                "Not enough information to judge affordability.",
                findings,
                trueCost,
                inputs,
                whatMustChange);
        }

        findings.Add(new AffordabilityFinding(
            "Required costs entered",
            true,
            AffordabilityRating.Comfortable,
            "Every cost marked as important has a figure or has been ruled out."));

        var projection = inputs.ProjectionWithScenario;
        var lowest = projection.LowestBalance;

        // Rule 1: cash flow must never go negative.
        if (projection.GoesNegative)
        {
            var date = projection.FirstNegativeDate;
            findings.Add(new AffordabilityFinding(
                "Cash flow stays positive",
                false,
                AffordabilityRating.Unsafe,
                $"The projected balance falls below zero on {date:yyyy-MM-dd}, reaching " +
                $"{lowest.ToDisplayString()}. Covering that gap would mean using credit for ordinary living costs."));

            whatMustChange.Add(
                $"Find {lowest.Abs().ToDisplayString()} more before {date:yyyy-MM-dd}, " +
                "or move the start date past that shortfall.");
        }
        else
        {
            findings.Add(new AffordabilityFinding(
                "Cash flow stays positive",
                true,
                AffordabilityRating.Comfortable,
                $"The lowest projected balance is {lowest.ToDisplayString()}."));
        }

        // Rule 2: the minimum reserve must survive.
        var belowReserve = projection.DaysBelowReserve(inputs.MinimumBalanceReserve);
        if (inputs.MinimumBalanceReserve > Money.Zero && belowReserve.Count > 0)
        {
            findings.Add(new AffordabilityFinding(
                "Minimum balance reserve kept",
                false,
                AffordabilityRating.Tight,
                $"The balance drops below your {inputs.MinimumBalanceReserve.ToDisplayString()} reserve " +
                $"on {belowReserve.Count} day(s), lowest {lowest.ToDisplayString()}."));

            whatMustChange.Add(
                $"Reduce the monthly cost by about " +
                $"{(inputs.MinimumBalanceReserve - lowest).Abs().ToDisplayString()} to protect your reserve.");
        }
        else
        {
            findings.Add(new AffordabilityFinding(
                "Minimum balance reserve kept",
                true,
                AffordabilityRating.Comfortable,
                "The balance stays above your minimum reserve throughout."));
        }

        // Rule 3: essential bills must remain covered by income.
        var essentialsCovered = inputs.MonthlyNetIncome >= inputs.MonthlyEssentialSpending + trueCost.MonthlyCashCost;
        if (!essentialsCovered)
        {
            var shortfall = (inputs.MonthlyEssentialSpending + trueCost.MonthlyCashCost - inputs.MonthlyNetIncome)
                .Round();

            findings.Add(new AffordabilityFinding(
                "Essential bills stay covered",
                false,
                AffordabilityRating.Unsafe,
                $"Monthly income of {inputs.MonthlyNetIncome.ToDisplayString()} does not cover " +
                $"{inputs.MonthlyEssentialSpending.ToDisplayString()} of essentials plus " +
                $"{trueCost.MonthlyCashCost.ToDisplayString()} for this decision. " +
                $"You would be short {shortfall.ToDisplayString()} every month."));

            whatMustChange.Add($"Increase monthly income or cut costs by {shortfall.ToDisplayString()}.");
        }
        else
        {
            findings.Add(new AffordabilityFinding(
                "Essential bills stay covered",
                true,
                AffordabilityRating.Comfortable,
                "Income still covers every essential bill plus this decision."));
        }

        // Rule 4: the emergency fund must survive the up-front cost.
        var emergencyAfter = Money.Max(Money.Zero, inputs.EmergencyFundBalance - trueCost.UpfrontCash);
        var monthsAfter = inputs.MonthlyEssentialSpending.IsZero
            ? 0m
            : Math.Round(emergencyAfter.Amount / inputs.MonthlyEssentialSpending.Amount, 1);

        if (trueCost.UpfrontCash > inputs.EmergencyFundBalance)
        {
            findings.Add(new AffordabilityFinding(
                "Emergency fund survives the up-front cost",
                false,
                AffordabilityRating.Unsafe,
                $"The up-front cost of {trueCost.UpfrontCash.ToDisplayString()} exceeds your emergency fund " +
                $"of {inputs.EmergencyFundBalance.ToDisplayString()}, so it would have to be borrowed."));

            whatMustChange.Add(
                $"Save a further {(trueCost.UpfrontCash - inputs.EmergencyFundBalance).ToDisplayString()} " +
                "before committing, or reduce the deposit.");
        }
        else if (monthsAfter < inputs.EmergencyFundTargetMonths)
        {
            findings.Add(new AffordabilityFinding(
                "Emergency fund stays at target",
                false,
                AffordabilityRating.Tight,
                $"After paying {trueCost.UpfrontCash.ToDisplayString()} up front the emergency fund covers " +
                $"{monthsAfter:0.0} months of essentials, below your {inputs.EmergencyFundTargetMonths:0.#}-month target."));

            var needed = (inputs.MonthlyEssentialSpending * inputs.EmergencyFundTargetMonths) - emergencyAfter;
            whatMustChange.Add(
                $"Rebuild the emergency fund by {needed.Round().ToDisplayString()} to reach your target again.");
        }
        else
        {
            findings.Add(new AffordabilityFinding(
                "Emergency fund stays at target",
                true,
                AffordabilityRating.Comfortable,
                $"The emergency fund still covers {monthsAfter:0.0} months of essentials afterwards."));
        }

        // Rule 5: a reserve must exist for the irregular costs this decision creates.
        if (trueCost.RecommendedMonthlyReserve > Money.Zero)
        {
            var reserveIsFunded = trueCost.MonthlyCashCost >= trueCost.RecommendedMonthlyReserve;
            findings.Add(new AffordabilityFinding(
                "Reserve for irregular costs",
                reserveIsFunded,
                AffordabilityRating.Tight,
                $"This decision needs about {trueCost.RecommendedMonthlyReserve.ToDisplayString()} a month " +
                "set aside for maintenance, repairs, insurance and renewals. " +
                (reserveIsFunded
                    ? "That is included in the monthly cost above."
                    : "Make sure that is genuinely being saved, not just budgeted.")));
        }

        // Rule 6: retirement contributions must continue.
        if (inputs.MonthlyRetirementContributions > Money.Zero)
        {
            var discretionary = (inputs.MonthlyNetIncome
                                 - inputs.MonthlyEssentialSpending
                                 - trueCost.MonthlyCashCost).Round();

            var retirementSafe = discretionary >= inputs.MonthlyRetirementContributions;

            findings.Add(new AffordabilityFinding(
                "Retirement contributions continue",
                retirementSafe,
                AffordabilityRating.Manageable,
                retirementSafe
                    ? $"There is still room for {inputs.MonthlyRetirementContributions.ToDisplayString()} " +
                      "a month of retirement contributions."
                    : $"Only {discretionary.ToDisplayString()} a month would remain, less than the " +
                      $"{inputs.MonthlyRetirementContributions.ToDisplayString()} you contribute to retirement. " +
                      "Employer matching may be lost too."));

            if (!retirementSafe)
            {
                whatMustChange.Add("Protect retirement contributions by reducing the monthly cost.");
            }
        }

        // Rule 7: debt-to-income must stay reasonable.
        var totalDebtPayments = inputs.ExistingMonthlyDebtPayments + MonthlyDebtPortion(inputs.Scenario);
        if (!inputs.MonthlyNetIncome.IsZero)
        {
            var ratio = Math.Round(totalDebtPayments.Amount / inputs.MonthlyNetIncome.Amount * 100m, 1);
            var ratioIsSafe = ratio <= 36m;

            findings.Add(new AffordabilityFinding(
                "Debt-to-income stays reasonable",
                ratioIsSafe,
                ratio > 43m ? AffordabilityRating.Unsafe : AffordabilityRating.Tight,
                $"Debt payments would be {ratio:0.0}% of net income " +
                $"({totalDebtPayments.ToDisplayString()} of {inputs.MonthlyNetIncome.ToDisplayString()}). " +
                (ratioIsSafe ? "That is within the usual 36% guideline." : "Lenders treat above 36% as stretched.")));

            if (!ratioIsSafe)
            {
                whatMustChange.Add("Reduce total debt payments, or clear an existing balance first.");
            }
        }

        // Rule 8: the worst case must not be ruinous.
        var worstCase = TrueCostEngine.Calculate(inputs.Scenario, ScenarioCase.Worst);
        var discretionaryBefore = (inputs.MonthlyNetIncome - inputs.MonthlyEssentialSpending).Round();
        var worstCaseSurvivable = discretionaryBefore >= worstCase.MonthlyCashCost + inputs.MinimumBreathingRoom;

        findings.Add(new AffordabilityFinding(
            "Worst case is survivable",
            worstCaseSurvivable,
            AffordabilityRating.Tight,
            worstCaseSurvivable
                ? $"Even at the worst-case {worstCase.MonthlyCashCost.ToDisplayString()} a month there is " +
                  $"still {inputs.MinimumBreathingRoom.ToDisplayString()} of breathing room."
                : $"In the worst case this costs {worstCase.MonthlyCashCost.ToDisplayString()} a month, " +
                  $"leaving less than your {inputs.MinimumBreathingRoom.ToDisplayString()} of breathing room."));

        if (!worstCaseSurvivable)
        {
            whatMustChange.Add(
                $"Build the worst case into the plan: it is " +
                $"{(worstCase.MonthlyCashCost - trueCost.MonthlyCashCost).Round().ToDisplayString()} " +
                "a month more than the expected case.");
        }

        // Rule 9: variable income raises the bar.
        if (inputs.IncomeIsVariable)
        {
            var variableSafe = projection.Assumption == Income.IncomeEstimate.Conservative;
            findings.Add(new AffordabilityFinding(
                "Variable income allowed for",
                variableSafe,
                AffordabilityRating.Tight,
                variableSafe
                    ? "The projection uses the conservative income estimate, which is right for variable hours."
                    : "Your income varies but this projection does not use the conservative estimate. " +
                      "Re-check it on a low-hours week."));

            if (!variableSafe)
            {
                whatMustChange.Add("Re-run this against your conservative income estimate.");
            }
        }

        // Rule 10: enough discretionary income must remain.
        var remainingDiscretionary = (inputs.MonthlyNetIncome
                                      - inputs.MonthlyEssentialSpending
                                      - trueCost.MonthlyCashCost
                                      - inputs.MonthlyRetirementContributions).Round();

        var hasBreathingRoom = remainingDiscretionary >= inputs.MinimumBreathingRoom;

        findings.Add(new AffordabilityFinding(
            "Breathing room remains",
            hasBreathingRoom,
            AffordabilityRating.Tight,
            hasBreathingRoom
                ? $"{remainingDiscretionary.ToDisplayString()} a month would remain after everything."
                : $"Only {remainingDiscretionary.ToDisplayString()} a month would remain, below your " +
                  $"{inputs.MinimumBreathingRoom.ToDisplayString()} minimum."));

        if (!hasBreathingRoom)
        {
            whatMustChange.Add(
                $"Free up {(inputs.MinimumBreathingRoom - remainingDiscretionary).Round().ToDisplayString()} " +
                "a month to keep your usual breathing room.");
        }

        var rating = DetermineRating(findings);
        var headline = BuildHeadline(rating, trueCost, findings);

        return Build(rating, headline, findings, trueCost, inputs, whatMustChange);
    }

    private static AffordabilityRating DetermineRating(IReadOnlyList<AffordabilityFinding> findings)
    {
        var failures = findings.Where(finding => !finding.Passed).ToList();

        if (failures.Count == 0)
        {
            return AffordabilityRating.Comfortable;
        }

        if (failures.Any(failure => failure.Severity == AffordabilityRating.InsufficientInformation))
        {
            return AffordabilityRating.InsufficientInformation;
        }

        if (failures.Any(failure => failure.Severity == AffordabilityRating.Unsafe))
        {
            return AffordabilityRating.Unsafe;
        }

        // Several tight failures together are worse than one.
        var tightFailures = failures.Count(failure => failure.Severity == AffordabilityRating.Tight);
        if (tightFailures >= 2)
        {
            return AffordabilityRating.Tight;
        }

        return tightFailures == 1 ? AffordabilityRating.Tight : AffordabilityRating.Manageable;
    }

    private static string BuildHeadline(
        AffordabilityRating rating,
        TrueCostBreakdown trueCost,
        IReadOnlyList<AffordabilityFinding> findings)
    {
        var failureCount = findings.Count(finding => !finding.Passed);

        return rating switch
        {
            AffordabilityRating.Comfortable =>
                $"Comfortable. The true cost is {trueCost.MonthlyTrueCost.ToDisplayString()} a month and every check passed.",

            AffordabilityRating.Manageable =>
                $"Manageable. The true cost is {trueCost.MonthlyTrueCost.ToDisplayString()} a month, " +
                "but it reduces your discretionary money or savings.",

            AffordabilityRating.Tight =>
                $"Tight. The true cost is {trueCost.MonthlyTrueCost.ToDisplayString()} a month and " +
                $"{failureCount} check(s) leave little margin for irregular costs.",

            AffordabilityRating.Unsafe =>
                $"Unsafe. The true cost is {trueCost.MonthlyTrueCost.ToDisplayString()} a month and " +
                $"{failureCount} check(s) failed, including a deficit or a drained reserve.",

            AffordabilityRating.InsufficientInformation =>
                "Insufficient information. Important costs have not been entered yet.",

            _ => "Unknown."
        };
    }

    private static AffordabilityAssessment Build(
        AffordabilityRating rating,
        string headline,
        IReadOnlyList<AffordabilityFinding> findings,
        TrueCostBreakdown trueCost,
        AffordabilityInputs inputs,
        IReadOnlyList<string> whatMustChange)
    {
        var emergencyAfter = Money.Max(Money.Zero, inputs.EmergencyFundBalance - trueCost.UpfrontCash);
        var monthsAfter = inputs.MonthlyEssentialSpending.IsZero
            ? 0m
            : Math.Round(emergencyAfter.Amount / inputs.MonthlyEssentialSpending.Amount, 1);

        return new AffordabilityAssessment
        {
            Rating = rating,
            Headline = headline,
            Findings = findings,
            TrueCost = trueCost,
            LowestProjectedBalance = inputs.ProjectionWithScenario.LowestBalance,
            EmergencyFundAfterPurchase = emergencyAfter,
            EmergencyFundMonthsAfterPurchase = monthsAfter,
            DiscretionaryIncomeAfter = (inputs.MonthlyNetIncome
                                        - inputs.MonthlyEssentialSpending
                                        - trueCost.MonthlyCashCost).Round(),
            WhatMustChange = whatMustChange
        };
    }

    private static Money MonthlyDebtPortion(Scenario scenario) =>
        Money.Sum(scenario.Lines
            .Where(line => line.CountsTowardsCost && !line.IsNonCash)
            .Where(line => line.Area.Contains("Loan", StringComparison.OrdinalIgnoreCase)
                           || line.Area.Contains("Finance", StringComparison.OrdinalIgnoreCase))
            .Select(line => line.MonthlyAmount)).Round();
}
