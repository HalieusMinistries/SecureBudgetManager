using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Tax;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.Core.Benefits;

public enum BenefitKind
{
    Medical = 0,
    Dental = 1,
    Vision = 2,
    CriticalIllness = 3,
    Accident = 4,
    HospitalIndemnity = 5,
    Life = 6,
    AccidentalDeathAndDismemberment = 7,
    LegalPlan = 8,
    Other = 9
}

public enum CoverageLevel
{
    EmployeeOnly = 0,
    EmployeePlusSpouse = 1,
    EmployeePlusChildren = 2,
    Family = 3
}

/// <summary>
/// One employee benefit, shown individually rather than folded into a single unexplained deduction.
/// </summary>
public sealed record BenefitPlan
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required BenefitKind Kind { get; init; }

    public required Guid MemberId { get; init; }

    public CoverageLevel Coverage { get; init; } = CoverageLevel.EmployeeOnly;

    /// <summary>What the employee pays each pay period.</summary>
    public required Money EmployeePremiumPerPeriod { get; init; }

    public required Frequency PremiumFrequency { get; init; }

    /// <summary>What the employer contributes each pay period, for visibility of total cost.</summary>
    public Money EmployerContributionPerPeriod { get; init; } = Money.Zero;

    public DeductionTaxTreatment TaxTreatment { get; init; } = DeductionTaxTreatment.PreTaxIncludingFica;

    /// <summary>Annual deductible, relevant to worst-case exposure.</summary>
    public Money Deductible { get; init; } = Money.Zero;

    /// <summary>Annual out-of-pocket maximum: the worst realistic year under this plan.</summary>
    public Money OutOfPocketMaximum { get; init; } = Money.Zero;

    /// <summary>Payout or coverage amount, for life and AD&amp;D style plans.</summary>
    public Money CoverageAmount { get; init; } = Money.Zero;

    public DateOnly? EffectiveDate { get; init; }

    public DateOnly? RenewalDate { get; init; }

    /// <summary>
    /// False for planned deductions that must not be applied to payroll estimates or historical
    /// payslips until the household confirms the amount and start date.
    /// </summary>
    public bool IsConfirmed { get; init; } = true;

    public IReadOnlyList<string> Beneficiaries { get; init; } = [];

    public string? Notes { get; init; }

    public Money AnnualEmployeePremium => FrequencyConverter.ToAnnual(EmployeePremiumPerPeriod, PremiumFrequency);

    public Money AnnualEmployerContribution =>
        FrequencyConverter.ToAnnual(EmployerContributionPerPeriod, PremiumFrequency);

    public Money AnnualTotalCost => AnnualEmployeePremium + AnnualEmployerContribution;

    /// <summary>
    /// The most this plan can cost the household in a bad year: premiums plus the out-of-pocket maximum.
    /// This is the figure a payroll deduction alone hides.
    /// </summary>
    public Money WorstCaseAnnualExposure =>
        AnnualEmployeePremium + (OutOfPocketMaximum.IsZero ? Deductible : OutOfPocketMaximum);

    public bool AppliesOn(DateOnly date)
    {
        if (!IsConfirmed)
        {
            return false;
        }

        if (EffectiveDate is { } start && date < start)
        {
            return false;
        }

        if (RenewalDate is { } end && date > end)
        {
            return false;
        }

        return true;
    }

    public void Validate()
    {
        if (Id == Guid.Empty)
        {
            throw new ArgumentException("A benefit needs an identifier.");
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new ArgumentException("A benefit needs a name.");
        }

        if (EmployeePremiumPerPeriod.IsNegative || EmployerContributionPerPeriod.IsNegative)
        {
            throw new ArgumentException("Premiums cannot be negative.");
        }

        if (Deductible.IsNegative || OutOfPocketMaximum.IsNegative)
        {
            throw new ArgumentException("Deductibles and out-of-pocket maximums cannot be negative.");
        }

        if (!OutOfPocketMaximum.IsZero && OutOfPocketMaximum < Deductible)
        {
            throw new ArgumentException("An out-of-pocket maximum cannot be below the deductible.");
        }

        if (!PremiumFrequency.IsRecurring())
        {
            throw new ArgumentException("A benefit premium needs a recurring frequency.");
        }

        if (RenewalDate is not null && EffectiveDate is not null && RenewalDate < EffectiveDate)
        {
            throw new ArgumentException("A renewal date cannot precede the effective date.");
        }
    }
}

/// <summary>
/// Expected annual healthcare cost under a plan, for three levels of use.
/// </summary>
public sealed record HealthcareCostModel(
    Money PremiumOnly,
    Money TypicalYear,
    Money BadYear,
    string Explanation);

public static class HealthcareCostEstimator
{
    /// <summary>
    /// Models a plan across a healthy year, a typical year and a bad year, so two plans can be
    /// compared on total exposure rather than on premium alone. A cheaper premium with a high
    /// deductible is often the more expensive choice.
    /// </summary>
    public static HealthcareCostModel Estimate(BenefitPlan plan, Money expectedAnnualClaims)
    {
        ArgumentNullException.ThrowIfNull(plan);
        plan.Validate();

        var premium = plan.AnnualEmployeePremium.Round();

        // In a typical year the household pays claims up to the deductible.
        var typicalOutOfPocket = Money.Min(expectedAnnualClaims, plan.Deductible);
        var typical = (premium + typicalOutOfPocket).Round();

        var badYearOutOfPocket = plan.OutOfPocketMaximum.IsZero ? plan.Deductible : plan.OutOfPocketMaximum;
        var badYear = (premium + badYearOutOfPocket).Round();

        var explanation =
            $"Premiums alone cost {premium.ToDisplayString()} a year. " +
            $"With {expectedAnnualClaims.ToDisplayString()} of expected claims the likely total is " +
            $"{typical.ToDisplayString()}. In a bad year, reaching the " +
            (plan.OutOfPocketMaximum.IsZero ? "deductible" : "out-of-pocket maximum") +
            $" would bring the total to {badYear.ToDisplayString()}.";

        return new HealthcareCostModel(premium, typical, badYear, explanation);
    }

    public sealed record PlanComparison(
        BenefitPlan Plan,
        HealthcareCostModel Costs,
        bool IsCheapestTypical,
        bool IsCheapestWorstCase);

    /// <summary>
    /// Compares plans on typical and worst-case totals. The two winners are often different plans,
    /// which is exactly the trade-off a household needs to see.
    /// </summary>
    public static IReadOnlyList<PlanComparison> Compare(
        IEnumerable<BenefitPlan> plans,
        Money expectedAnnualClaims)
    {
        ArgumentNullException.ThrowIfNull(plans);

        var modelled = plans
            .Select(plan => (Plan: plan, Costs: Estimate(plan, expectedAnnualClaims)))
            .ToList();

        if (modelled.Count == 0)
        {
            return [];
        }

        var bestTypical = modelled.Min(entry => entry.Costs.TypicalYear.Amount);
        var bestWorstCase = modelled.Min(entry => entry.Costs.BadYear.Amount);

        return modelled
            .Select(entry => new PlanComparison(
                entry.Plan,
                entry.Costs,
                entry.Costs.TypicalYear.Amount == bestTypical,
                entry.Costs.BadYear.Amount == bestWorstCase))
            .ToList();
    }
}
