using SecureBudgetManager.Core.Models;

namespace SecureBudgetManager.Core.Guidance;

/// <summary>
/// The working behind a recommendation. Every instruction the programme gives can be expanded
/// into one of these, so a household is never asked to trust a number it cannot check.
/// </summary>
public sealed record GuidanceExplanation
{
    public IReadOnlyList<string> InputsUsed { get; init; } = [];

    public IReadOnlyList<string> DatesConsidered { get; init; } = [];

    public required string IncomeBasis { get; init; }

    public IReadOnlyList<string> ObligationsProtected { get; init; } = [];

    public IReadOnlyList<string> Calculations { get; init; } = [];

    public IReadOnlyList<string> Assumptions { get; init; } = [];

    public IReadOnlyList<string> MissingInformation { get; init; } = [];

    public IReadOnlyList<string> GuidanceSources { get; init; } = [];

    public required string FinancialEffect { get; init; }

    public bool HasMissingInformation => MissingInformation.Count > 0;
}

/// <summary>How urgent an instruction is, which is what decides the order they are shown in.</summary>
public enum RecommendationUrgency
{
    /// <summary>Something must be paid or set aside now.</summary>
    ActNow = 0,

    /// <summary>Something should be set aside from this paycheque.</summary>
    SetAside = 1,

    /// <summary>Money that may be spent.</summary>
    MaySpend = 2,

    /// <summary>A risk the household should know about.</summary>
    Warning = 3,

    /// <summary>Information is missing and the answer cannot be given honestly without it.</summary>
    MissingInformation = 4
}

/// <summary>One plain-English instruction, with its working attached.</summary>
public sealed record Recommendation
{
    public required string Instruction { get; init; }

    public required RecommendationUrgency Urgency { get; init; }

    public Money? Amount { get; init; }

    public NeedTier? Tier { get; init; }

    public required GuidanceExplanation Explanation { get; init; }
}

/// <summary>
/// Money that is present but not available, and the obligation that is holding it. Naming the
/// obligation is the difference between "do not spend this" and guidance a household can act on.
/// </summary>
public sealed record ProtectedAmount
{
    public required string Label { get; init; }

    public required Money Amount { get; init; }

    public required NeedTier Tier { get; init; }

    /// <summary>The bill, reserve, debt payment or essential need this money belongs to.</summary>
    public required string ProtectedBy { get; init; }

    public DateOnly? Date { get; init; }

    public required string Reason { get; init; }
}

/// <summary>An allocation that had to be cut because the money was not there.</summary>
public sealed record ReductionApplied
{
    public required ReducibleBucket Bucket { get; init; }

    public required Money Requested { get; init; }

    public required Money Allocated { get; init; }

    public Money Reduced => (Requested - Allocated).Round();

    public required string Explanation { get; init; }
}

/// <summary>An obligation the household cannot currently cover, stated without softening.</summary>
public sealed record ShortfallItem
{
    public required string Name { get; init; }

    public required Money AmountMissing { get; init; }

    public required DateOnly Date { get; init; }

    public required NeedTier Tier { get; init; }
}

public sealed record ShortfallReport
{
    public required Money TotalShortfall { get; init; }

    public required DateOnly OccursOn { get; init; }

    public required IReadOnlyList<ShortfallItem> Affected { get; init; }

    public DateOnly? EarliestNegativeBalance { get; init; }

    public required IReadOnlyList<ReductionApplied> AlreadyReduced { get; init; }

    public required Money UnresolvedDeficit { get; init; }

    public required string Explanation { get; init; }
}
