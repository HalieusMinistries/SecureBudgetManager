using SecureBudgetManager.Core.Models;

namespace SecureBudgetManager.Core.Guidance;

/// <summary>
/// Where a suggested amount came from. The distinction matters: a published survey and a figure
/// someone typed in from memory should not be presented with the same authority.
/// </summary>
public enum CostGuidanceSourceType
{
    /// <summary>A government or agency publication.</summary>
    OfficialGuidance = 0,

    /// <summary>A published market survey, index or retailer price list.</summary>
    PublishedMarketInformation = 1,

    /// <summary>A price the household observed locally, such as a shelf price.</summary>
    LocallyObservedPrice = 2,

    /// <summary>A figure the household estimated.</summary>
    UserEstimate = 3,

    /// <summary>Derived from what this household actually spent.</summary>
    HouseholdActualSpending = 4,

    /// <summary>Allocated from a sourced total using a documented method, not observed directly.</summary>
    DerivedEstimate = 5
}

public static class CostGuidanceSourceTypeExtensions
{
    public static string ToDisplayName(this CostGuidanceSourceType type) => type switch
    {
        CostGuidanceSourceType.OfficialGuidance => "Official guidance",
        CostGuidanceSourceType.PublishedMarketInformation => "Published market information",
        CostGuidanceSourceType.LocallyObservedPrice => "Locally observed price",
        CostGuidanceSourceType.UserEstimate => "User-entered estimate",
        CostGuidanceSourceType.HouseholdActualSpending => "This household's actual spending",
        CostGuidanceSourceType.DerivedEstimate => "Derived estimate",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown guidance source type.")
    };
}

/// <summary>Which kind of money figure a row represents. Guidance never overwrites an actual payment.</summary>
public enum PriceKind
{
    BundledSuggested = 0,
    LocallyObserved = 1,
    HouseholdPlanned = 2,
    HouseholdActual = 3
}

public static class PriceKindExtensions
{
    public static string ToDisplayName(this PriceKind kind) => kind switch
    {
        PriceKind.BundledSuggested => "Bundled suggested price",
        PriceKind.LocallyObserved => "Locally observed price",
        PriceKind.HouseholdPlanned => "Household planned price",
        PriceKind.HouseholdActual => "Household actual paid price",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown price kind.")
    };
}

public enum GuidanceFreshness
{
    Current = 0,
    Ageing = 1,
    Expired = 2
}

public enum GuidanceConfidence
{
    Low = 0,
    Medium = 1,
    High = 2
}

/// <summary>
/// Where a cost applies. Matching is widened outwards — city, then county, then state, then
/// country — so a household in a small town still gets county guidance rather than nothing.
/// </summary>
public sealed record CostLocality
{
    public string Country { get; init; } = "United States";

    public string? State { get; init; }

    public string? County { get; init; }

    public string? City { get; init; }

    public static CostLocality UnitedStates { get; } = new();

    public static CostLocality Utah { get; } = new() { State = "Utah" };

    public static CostLocality SaltLakeCounty { get; } = new()
    {
        State = "Utah",
        County = "Salt Lake County"
    };

    public string Describe()
    {
        var parts = new[] { City, County, State, Country }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToArray();

        return string.Join(", ", parts);
    }

    /// <summary>
    /// How closely this locality matches a household's own. Higher is better; null means the
    /// record is for somewhere else entirely and must not be used.
    /// </summary>
    public int? MatchStrength(CostLocality household)
    {
        ArgumentNullException.ThrowIfNull(household);

        if (!Same(Country, household.Country))
        {
            return null;
        }

        var score = 1;

        if (!string.IsNullOrWhiteSpace(State))
        {
            if (!Same(State, household.State))
            {
                return null;
            }

            score += 1;
        }

        if (!string.IsNullOrWhiteSpace(County))
        {
            if (!Same(County, household.County))
            {
                return null;
            }

            score += 1;
        }

        if (!string.IsNullOrWhiteSpace(City))
        {
            if (!Same(City, household.City))
            {
                return null;
            }

            score += 1;
        }

        return score;
    }

    private static bool Same(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
}

/// <summary>Who the guidance is priced for. A figure for one adult does not fit a family of three.</summary>
public sealed record HouseholdComposition(int Adults, int Children)
{
    public int People => Adults + Children;

    public string Describe() => Children == 0
        ? $"{Adults} adult(s)"
        : $"{Adults} adult(s) and {Children} child(ren)";
}

/// <summary>
/// A locality cost reference. Amounts are never hard-coded into the programme as permanent
/// prices: every figure carries the source that produced it, the date it was observed and a date
/// by which it must be looked at again.
/// </summary>
public sealed record CostGuidanceRecord
{
    public required Guid Id { get; init; }

    public required CostLocality Locality { get; init; }

    public HouseholdComposition Composition { get; init; } = new(2, 0);

    /// <summary>The spending category this guidance is about, such as a grocery category.</summary>
    public required string Category { get; init; }

    /// <summary>The date the guidance starts applying from.</summary>
    public required DateOnly EffectiveDate { get; init; }

    public required string SourceName { get; init; }

    public required CostGuidanceSourceType SourceType { get; init; }

    /// <summary>When the figure was observed or published, which is often earlier than entry.</summary>
    public DateOnly? ObservedOn { get; init; }

    public Money Low { get; init; } = Money.Zero;

    public Money Typical { get; init; } = Money.Zero;

    public Money Comfortable { get; init; } = Money.Zero;

    public GuidanceConfidence Confidence { get; init; } = GuidanceConfidence.Low;

    public DateOnly? LastReviewedOn { get; init; }

    /// <summary>After this date the record stays visible for reference but is marked for review.</summary>
    public DateOnly? ReviewByDate { get; init; }

    public string? Notes { get; init; }

    public string? SourceUrl { get; init; }

    public string? SourceIdentifier { get; init; }

    public string? Assumptions { get; init; }

    public string UnitOrFrequency { get; init; } = "Weekly, household";

    public bool IsDerived { get; init; }

    public Money? SourceTotal { get; init; }

    public string? AllocationMethod { get; init; }

    public string? Calculation { get; init; }

    public string? PackVersion { get; init; }

    public Money? UserSelectedAmount { get; init; }

    public PriceKind PriceKind { get; init; } = PriceKind.BundledSuggested;

    public int FreshnessDays { get; init; } = 90;

    public Money? DifferenceFromGuidance => UserSelectedAmount is { } chosen && HasAmounts
        ? (chosen - Typical).Round()
        : null;

    public GuidanceFreshness Freshness(DateOnly today)
    {
        if (ReviewByDate is { } due && today > due)
        {
            return GuidanceFreshness.Expired;
        }

        var observed = ObservedOn ?? EffectiveDate;
        var age = today.DayNumber - observed.DayNumber;

        if (age > FreshnessDays)
        {
            return GuidanceFreshness.Ageing;
        }

        return GuidanceFreshness.Current;
    }

    public string FreshnessLabel(DateOnly today) => Freshness(today) switch
    {
        GuidanceFreshness.Expired => "Review required",
        GuidanceFreshness.Ageing => "Ageing — review recommended",
        _ => "Current for this pack; not permanently current"
    };

    /// <summary>
    /// False when the record exists as a placeholder with no figures. A placeholder must never be
    /// presented as a recommendation; the household is told the information is missing instead.
    /// </summary>
    public bool HasAmounts => !(Low.IsZero && Typical.IsZero && Comfortable.IsZero);

    public bool RequiresReview(DateOnly today) =>
        !HasAmounts || (ReviewByDate is { } due && today > due);

    public string DescribeSource()
    {
        var observed = ObservedOn is { } date
            ? $" observed {date:yyyy-MM-dd}"
            : string.Empty;

        return $"{SourceName} ({SourceType.ToDisplayName()}){observed}, effective {EffectiveDate:yyyy-MM-dd}";
    }

    public void Validate()
    {
        if (Id == Guid.Empty)
        {
            throw new ArgumentException("A cost guidance record needs an identifier.");
        }

        if (string.IsNullOrWhiteSpace(Category))
        {
            throw new ArgumentException("A cost guidance record needs a category.");
        }

        if (string.IsNullOrWhiteSpace(SourceName))
        {
            throw new ArgumentException($"The guidance for \"{Category}\" needs a source.");
        }

        if (Low.IsNegative || Typical.IsNegative || Comfortable.IsNegative)
        {
            throw new ArgumentException($"The guidance for \"{Category}\" cannot use negative amounts.");
        }

        if (HasAmounts && (Low > Typical || Typical > Comfortable))
        {
            throw new ArgumentException(
                $"The guidance for \"{Category}\" must read low, then typical, then comfortable.");
        }
    }
}

/// <summary>The outcome of looking for guidance, including the honest answer that there is none.</summary>
public sealed record CostGuidanceLookup
{
    public CostGuidanceRecord? Record { get; init; }

    public required string Category { get; init; }

    public required string Explanation { get; init; }

    public bool RequiresReview { get; init; }

    public bool HasGuidance => Record is { HasAmounts: true };

    public const string NoLocalInformation = "Insufficient local pricing information";
}

public static class CostGuidanceLibrary
{
    /// <summary>
    /// Finds the most specific, most recent, still-usable guidance for a category.
    ///
    /// Records that are out of review date are still returned so the household can see what they
    /// once relied on, but they are flagged, and a placeholder with no figures is reported as
    /// missing information rather than dressed up as a recommendation.
    /// </summary>
    public static CostGuidanceLookup Find(
        IEnumerable<CostGuidanceRecord> records,
        CostLocality household,
        string category,
        HouseholdComposition composition,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(household);
        ArgumentNullException.ThrowIfNull(composition);

        var candidates = records
            .Where(record => string.Equals(record.Category, category, StringComparison.OrdinalIgnoreCase))
            .Where(record => record.EffectiveDate <= today)
            .Select(record => (Record: record, Strength: record.Locality.MatchStrength(household)))
            .Where(pair => pair.Strength is not null)
            .OrderByDescending(pair => pair.Record.HasAmounts)
            .ThenByDescending(pair => CompositionScore(pair.Record.Composition, composition))
            .ThenByDescending(pair => pair.Strength!.Value)
            .ThenByDescending(pair => pair.Record.EffectiveDate)
            .ToList();

        if (candidates.Count == 0)
        {
            return new CostGuidanceLookup
            {
                Category = category,
                Explanation =
                    $"{CostGuidanceLookup.NoLocalInformation} for {category} in {household.Describe()}. " +
                    "Enter a local figure on the Local Guidance page, or leave this category unplanned.",
                RequiresReview = true
            };
        }

        var best = candidates[0].Record;

        if (!best.HasAmounts)
        {
            return new CostGuidanceLookup
            {
                Record = best,
                Category = category,
                Explanation =
                    $"{CostGuidanceLookup.NoLocalInformation} for {category} in {household.Describe()}. " +
                    $"A placeholder exists from {best.SourceName} but no amounts have been entered.",
                RequiresReview = true
            };
        }

        var requiresReview = best.RequiresReview(today);

        var explanation = requiresReview
            ? $"{best.Typical.ToDisplayString()} typical for {category}, from {best.DescribeSource()}. " +
              $"This guidance passed its review date of {best.ReviewByDate:yyyy-MM-dd} and requires review. " +
              "It is shown for reference and should not be treated as a current figure."
            : $"{best.Typical.ToDisplayString()} typical for {category} " +
              $"({best.Low.ToDisplayString()} low, {best.Comfortable.ToDisplayString()} comfortable), " +
              $"from {best.DescribeSource()}. Confidence: {best.Confidence}.";

        return new CostGuidanceLookup
        {
            Record = best,
            Category = category,
            Explanation = explanation,
            RequiresReview = requiresReview
        };
    }

    /// <summary>Every record that has passed its review date, for the warning banner.</summary>
    public static IReadOnlyList<CostGuidanceRecord> NeedingReview(
        IEnumerable<CostGuidanceRecord> records,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(records);

        return records
            .Where(record => record.RequiresReview(today))
            .OrderBy(record => record.ReviewByDate ?? record.EffectiveDate)
            .ToList();
    }

    /// <summary>
    /// Creates empty, clearly labelled placeholders for a locality so a household unfamiliar with
    /// local costs has somewhere to record what it learns. No prices are invented: every record
    /// starts with no amounts and is reported as missing information until the household fills it in.
    /// </summary>
    public static IReadOnlyList<CostGuidanceRecord> CreatePlaceholders(
        CostLocality locality,
        HouseholdComposition composition,
        IEnumerable<string> categories,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(locality);
        ArgumentNullException.ThrowIfNull(categories);

        return categories
            .Select(category => new CostGuidanceRecord
            {
                Id = Guid.NewGuid(),
                Locality = locality,
                Composition = composition,
                Category = category,
                EffectiveDate = today,
                SourceName = "Not yet researched",
                SourceType = CostGuidanceSourceType.UserEstimate,
                Confidence = GuidanceConfidence.Low,
                ReviewByDate = today,
                Notes = "Placeholder. Enter a local figure and record where it came from."
            })
            .ToList();
    }

    private static int CompositionScore(HouseholdComposition record, HouseholdComposition household)
    {
        var distance = Math.Abs(record.Adults - household.Adults)
                       + Math.Abs(record.Children - household.Children);

        return -distance;
    }
}
