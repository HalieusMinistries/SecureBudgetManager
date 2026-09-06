using SecureBudgetManager.Core.Income;
using SecureBudgetManager.Core.Models;

namespace SecureBudgetManager.Core.Guidance;

/// <summary>
/// Which income figure the guidance is built on. <see cref="Actual"/> uses recorded payslips and
/// falls back to the conservative estimate for any period that has not been paid yet.
/// </summary>
public enum IncomeBasis
{
    Conservative = 0,
    Normal = 1,
    Optimistic = 2,
    Actual = 3
}

public static class IncomeBasisExtensions
{
    public static IncomeEstimate ToEstimate(this IncomeBasis basis) => basis switch
    {
        IncomeBasis.Conservative => IncomeEstimate.Conservative,
        IncomeBasis.Normal => IncomeEstimate.Normal,
        IncomeBasis.Optimistic => IncomeEstimate.Optimistic,

        // Actual pay is read from payslips; anything not yet paid is estimated cautiously.
        IncomeBasis.Actual => IncomeEstimate.Conservative,
        _ => throw new ArgumentOutOfRangeException(nameof(basis), basis, "Unknown income basis.")
    };

    public static string ToDisplayName(this IncomeBasis basis) => basis switch
    {
        IncomeBasis.Conservative => "Conservative",
        IncomeBasis.Normal => "Normal",
        IncomeBasis.Optimistic => "Optimistic",
        IncomeBasis.Actual => "Actual pay, conservative where not yet paid",
        _ => throw new ArgumentOutOfRangeException(nameof(basis), basis, "Unknown income basis.")
    };
}

/// <summary>How each eligible earner's personal discretionary allowance is worked out.</summary>
public enum DiscretionaryMethod
{
    /// <summary>
    /// The default. The same percentage is applied to each person's own remaining money, so the
    /// allowance comes from that person's income rather than from a pooled household balance.
    /// </summary>
    SamePercentageOfOwnRemaining = 0,

    /// <summary>A household pot split in proportion to usable net income.</summary>
    ProportionalToUsableNetIncome = 1,

    /// <summary>The same dollar amount for each eligible earner.</summary>
    FixedAmountEach = 2,

    /// <summary>A separately chosen percentage for each eligible earner.</summary>
    CustomPercentages = 3,

    /// <summary>Everything each person has left after their agreed obligations.</summary>
    AllRemainingPersonalMoney = 4,

    /// <summary>No discretionary allocation for this period.</summary>
    None = 5
}

/// <summary>What happens to money left over once an allocation is complete.</summary>
public enum SurplusTarget
{
    Unallocated = 0,
    EmergencyFund = 1,
    DebtRepayment = 2,
    UpcomingAnnualExpenses = 3,
    SinkingFunds = 4,
    HouseholdSavings = 5,
    PlannedGoals = 6,
    SharedEntertainment = 7,
    PersonalDiscretionary = 8
}

/// <summary>An allocation that may be reduced when income falls short, in the order listed.</summary>
public enum ReducibleBucket
{
    UnallocatedSurplus = 0,
    OptionalDiscretionary = 1,
    SharedEntertainment = 2,
    FlexibleSavings = 3,
    LowerPriorityGoals = 4
}

public static class ReducibleBucketExtensions
{
    public static string ToDisplayName(this ReducibleBucket bucket) => bucket switch
    {
        ReducibleBucket.UnallocatedSurplus => "Unallocated surplus",
        ReducibleBucket.OptionalDiscretionary => "Optional discretionary spending",
        ReducibleBucket.SharedEntertainment => "Shared entertainment",
        ReducibleBucket.FlexibleSavings => "Flexible savings",
        ReducibleBucket.LowerPriorityGoals => "Lower-priority goals",
        _ => throw new ArgumentOutOfRangeException(nameof(bucket), bucket, "Unknown allocation bucket.")
    };
}

/// <summary>
/// Positive funding order. This is not the reverse of the shortfall reduction order:
/// family and growth allocations are funded before discretionary and flexible leftovers,
/// and unallocated surplus receives only what remains.
/// </summary>
public enum FundingBucket
{
    Survival = 0,
    Safety = 1,
    Stability = 2,
    RequiredSinkingAndProtectedSavings = 3,
    FamilyAndBelonging = 4,
    Growth = 5,
    PersonalDiscretionary = 6,
    FlexibleAllocations = 7,
    UnallocatedSurplus = 8
}

public static class FundingBucketExtensions
{
    public static string ToDisplayName(this FundingBucket bucket) => bucket switch
    {
        FundingBucket.Survival => "Survival obligations",
        FundingBucket.Safety => "Safety obligations",
        FundingBucket.Stability => "Stability obligations",
        FundingBucket.RequiredSinkingAndProtectedSavings => "Required sinking funds and protected savings",
        FundingBucket.FamilyAndBelonging => "Configured family-and-belonging allocations",
        FundingBucket.Growth => "Configured growth allocations",
        FundingBucket.PersonalDiscretionary => "Configured personal discretionary allowances",
        FundingBucket.FlexibleAllocations => "User-defined flexible allocations",
        FundingBucket.UnallocatedSurplus => "Unallocated surplus (final remainder only)",
        _ => throw new ArgumentOutOfRangeException(nameof(bucket), bucket, "Unknown funding bucket.")
    };
}

/// <summary>Where additional income above the conservative baseline is directed.</summary>
public sealed record SurplusRule
{
    public required SurplusTarget Target { get; init; }

    /// <summary>Share of the additional income, 0 to 100.</summary>
    public required decimal Percent { get; init; }

    public void Validate()
    {
        if (Percent < 0m || Percent > 100m)
        {
            throw new ArgumentException("A surplus rule share must be between 0 and 100 percent.");
        }
    }
}

/// <summary>
/// The household's agreed rules for turning a paycheque into allocations. Persisting these rather
/// than only the resulting numbers is what makes a past recommendation reproducible.
/// </summary>
public sealed record AllocationRules
{
    /// <summary>The income figure essentials are protected with. Conservative by default.</summary>
    public IncomeBasis Basis { get; init; } = IncomeBasis.Conservative;

    /// <summary>
    /// Set only by an explicit decision. Until then, optimistic income is shown but never used to
    /// fund an essential obligation.
    /// </summary>
    public bool OptimisticIncomeMayFundEssentials { get; init; }

    /// <summary>Cash deliberately left untouched, on top of every named obligation.</summary>
    public Money SafetyBuffer { get; init; } = Money.Zero;

    public DiscretionaryMethod DiscretionaryMethod { get; init; } =
        DiscretionaryMethod.SamePercentageOfOwnRemaining;

    /// <summary>Used by <see cref="DiscretionaryMethod.SamePercentageOfOwnRemaining"/>, 0 to 100.</summary>
    public decimal DiscretionaryPercent { get; init; } = 10m;

    /// <summary>
    /// False when the household has chosen the percentage method but has not yet chosen the
    /// percentage. Existing households default to true so a stored 10 percent remains in force.
    /// </summary>
    public bool DiscretionaryPercentConfigured { get; init; } = true;

    /// <summary>Used by <see cref="DiscretionaryMethod.FixedAmountEach"/>.</summary>
    public Money FixedDiscretionaryAmount { get; init; } = Money.Zero;

    /// <summary>Used by <see cref="DiscretionaryMethod.CustomPercentages"/>, keyed by member.</summary>
    public IReadOnlyDictionary<Guid, decimal> CustomDiscretionaryPercents { get; init; } =
        new Dictionary<Guid, decimal>();

    /// <summary>Shared household entertainment set aside each payday.</summary>
    public Money SharedEntertainmentPerPayday { get; init; } = Money.Zero;

    /// <summary>Savings that may be reduced without harming a protected obligation.</summary>
    public Money FlexibleSavingsPerPayday { get; init; } = Money.Zero;

    /// <summary>
    /// An agreed departure from the proportional split, keyed by member and expressed as a
    /// percentage of shared obligations. Empty means the proportional result is used.
    /// </summary>
    public IReadOnlyDictionary<Guid, decimal> SharedContributionOverrides { get; init; } =
        new Dictionary<Guid, decimal>();

    public IReadOnlyList<SurplusRule> SurplusRules { get; init; } = [];

    /// <summary>The order allocations are reduced in when income is short.</summary>
    public IReadOnlyList<ReducibleBucket> ReductionOrder { get; init; } = DefaultReductionOrder;

    /// <summary>The order leftover money is assigned after protected essentials.</summary>
    public IReadOnlyList<FundingBucket> FundingOrder { get; init; } = DefaultFundingOrder;

    /// <summary>
    /// Who receives the odd cent when a shared obligation does not divide evenly. Null means the
    /// household keeps it, which is the default so that no individual is silently charged more.
    /// </summary>
    public Guid? RoundingRemainderMemberId { get; init; }

    public string? Notes { get; init; }

    public static IReadOnlyList<ReducibleBucket> DefaultReductionOrder { get; } =
    [
        ReducibleBucket.UnallocatedSurplus,
        ReducibleBucket.OptionalDiscretionary,
        ReducibleBucket.SharedEntertainment,
        ReducibleBucket.FlexibleSavings,
        ReducibleBucket.LowerPriorityGoals
    ];

    public static IReadOnlyList<FundingBucket> DefaultFundingOrder { get; } =
    [
        FundingBucket.Survival,
        FundingBucket.Safety,
        FundingBucket.Stability,
        FundingBucket.RequiredSinkingAndProtectedSavings,
        FundingBucket.FamilyAndBelonging,
        FundingBucket.Growth,
        FundingBucket.PersonalDiscretionary,
        FundingBucket.FlexibleAllocations,
        FundingBucket.UnallocatedSurplus
    ];

    public bool UsesCustomFundingOrder =>
        FundingOrder.Count != DefaultFundingOrder.Count
        || !FundingOrder.SequenceEqual(DefaultFundingOrder);

    public bool UsesCustomReductionOrder =>
        ReductionOrder.Count != DefaultReductionOrder.Count
        || !ReductionOrder.SequenceEqual(DefaultReductionOrder);

    public decimal PercentFor(Guid memberId) => DiscretionaryMethod switch
    {
        DiscretionaryMethod.CustomPercentages =>
            CustomDiscretionaryPercents.TryGetValue(memberId, out var percent) ? percent : 0m,
        DiscretionaryMethod.SamePercentageOfOwnRemaining =>
            DiscretionaryPercentConfigured ? DiscretionaryPercent : 0m,
        _ => 0m
    };

    public void Validate()
    {
        if (SafetyBuffer.IsNegative)
        {
            throw new ArgumentException("The household safety buffer cannot be negative.");
        }

        if (DiscretionaryPercent < 0m || DiscretionaryPercent > 100m)
        {
            throw new ArgumentException("The discretionary percentage must be between 0 and 100.");
        }

        if (FixedDiscretionaryAmount.IsNegative)
        {
            throw new ArgumentException("A fixed discretionary amount cannot be negative.");
        }

        if (SharedEntertainmentPerPayday.IsNegative || FlexibleSavingsPerPayday.IsNegative)
        {
            throw new ArgumentException("Entertainment and flexible savings cannot be negative.");
        }

        foreach (var percent in CustomDiscretionaryPercents.Values)
        {
            if (percent < 0m || percent > 100m)
            {
                throw new ArgumentException("A personal discretionary percentage must be between 0 and 100.");
            }
        }

        foreach (var percent in SharedContributionOverrides.Values)
        {
            if (percent < 0m || percent > 100m)
            {
                throw new ArgumentException("A shared contribution override must be between 0 and 100 percent.");
            }
        }

        foreach (var rule in SurplusRules)
        {
            rule.Validate();
        }

        var surplusTotal = SurplusRules.Sum(rule => rule.Percent);

        if (surplusTotal > 100m)
        {
            throw new ArgumentException(
                $"Additional-income rules total {surplusTotal:0.##} percent, which is more than the income available.");
        }

        if (ReductionOrder.Distinct().Count() != ReductionOrder.Count)
        {
            throw new ArgumentException("The reduction order lists the same allocation more than once.");
        }

        if (FundingOrder.Distinct().Count() != FundingOrder.Count)
        {
            throw new ArgumentException("The funding order lists the same allocation more than once.");
        }
    }
}
