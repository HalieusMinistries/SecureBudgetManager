using SecureBudgetManager.Core.Models;

namespace SecureBudgetManager.Core.Guidance;

/// <summary>
/// Which version of the grocery plan this is. Keeping all three lets the household see, before it
/// happens, what losing food assistance would cost.
/// </summary>
public enum GroceryPlanKind
{
    /// <summary>What the household expects to spend, allowing for any assistance it receives.</summary>
    Current = 0,

    /// <summary>The same shopping without any assistance. What the cash requirement becomes if it stops.</summary>
    FallbackWithoutAssistance = 1,

    /// <summary>The least the household could spend and still eat. Used only under real pressure.</summary>
    EmergencyMinimum = 2
}

public static class GroceryPlanKindExtensions
{
    public static string ToDisplayName(this GroceryPlanKind kind) => kind switch
    {
        GroceryPlanKind.Current => "Current plan",
        GroceryPlanKind.FallbackWithoutAssistance => "Fallback plan without assistance",
        GroceryPlanKind.EmergencyMinimum => "Emergency minimum plan",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown grocery plan kind.")
    };
}

/// <summary>What happens to money left in a category at the end of the week.</summary>
public enum RolloverRule
{
    /// <summary>Unspent money returns to the household surplus.</summary>
    ReturnToHousehold = 0,

    /// <summary>Unspent money stays in the category for next week.</summary>
    CarryForward = 1
}

/// <summary>
/// One line of the grocery plan. Groceries are split into categories because a single figure
/// hides the thing a household most needs to see: which part of the shop is actually the problem.
/// </summary>
public sealed record GroceryCategoryPlan
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    /// <summary>Essential categories are protected as Survival food; optional ones are not.</summary>
    public bool IsEssential { get; init; } = true;

    /// <summary>The amount guidance suggests, before the household chooses its own limit.</summary>
    public Money SuggestedWeekly { get; init; } = Money.Zero;

    public Money LowRange { get; init; } = Money.Zero;

    public Money TypicalRange { get; init; } = Money.Zero;

    public Money ComfortableRange { get; init; } = Money.Zero;

    /// <summary>The limit the household chose. This, not the suggestion, is what is allocated.</summary>
    public Money WeeklyLimit { get; init; } = Money.Zero;

    public Money CarriedForward { get; init; } = Money.Zero;

    public RolloverRule Rollover { get; init; } = RolloverRule.ReturnToHousehold;

    /// <summary>True when this category is normally supplied by food assistance rather than cash.</summary>
    public bool SuppliedByAssistance { get; init; }

    /// <summary>Where the suggested amount came from, carried through from cost guidance.</summary>
    public string GuidanceSource { get; init; } = "Not yet researched";

    public DateOnly? GuidanceEffectiveDate { get; init; }

    public GuidanceConfidence Confidence { get; init; } = GuidanceConfidence.Low;

    public bool ReviewRequired { get; init; } = true;

    public string? Notes { get; init; }

    /// <summary>Money available this week: the chosen limit plus anything carried in.</summary>
    public Money AvailableThisWeek => (WeeklyLimit + CarriedForward).Round();

    public Money RemainingAfter(Money spent) => (AvailableThisWeek - spent).Round();

    public void Validate()
    {
        if (Id == Guid.Empty)
        {
            throw new ArgumentException("A grocery category needs an identifier.");
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new ArgumentException("A grocery category needs a name.");
        }

        if (WeeklyLimit.IsNegative || CarriedForward.IsNegative || SuggestedWeekly.IsNegative)
        {
            throw new ArgumentException($"The grocery category \"{Name}\" cannot use negative amounts.");
        }
    }
}

/// <summary>
/// Food assistance the household expects to receive.
///
/// Assistance reduces the cash a household needs for the categories it actually covers. It is
/// never counted as income, and it is never assumed to be permanent: the fallback plan always
/// exists alongside it.
/// </summary>
public sealed record FoodAssistance
{
    public bool IsExpected { get; init; }

    /// <summary>Temporarily turns the expectation off without deleting what is known about it.</summary>
    public bool IsSuspended { get; init; }

    public string? SourceName { get; init; }

    /// <summary>The value the household expects to receive in goods, per week.</summary>
    public Money EstimatedWeeklyValue { get; init; } = Money.Zero;

    /// <summary>Names of the grocery categories assistance normally supplies.</summary>
    public IReadOnlyList<string> CategoriesSupplied { get; init; } = [];

    public DateOnly? EffectiveDate { get; init; }

    /// <summary>When the household should check whether assistance is continuing.</summary>
    public DateOnly? ReviewDate { get; init; }

    public string? Notes { get; init; }

    public bool AppliesOn(DateOnly date) =>
        IsExpected
        && !IsSuspended
        && (EffectiveDate is null || EffectiveDate <= date);

    public bool RequiresReview(DateOnly today) =>
        IsExpected && !IsSuspended && ReviewDate is { } due && today > due;

    public bool Supplies(string categoryName) =>
        CategoriesSupplied.Any(name =>
            string.Equals(name, categoryName, StringComparison.OrdinalIgnoreCase));

    public void Validate()
    {
        if (EstimatedWeeklyValue.IsNegative)
        {
            throw new ArgumentException("Food assistance cannot have a negative value.");
        }
    }
}

/// <summary>One complete grocery plan, with its own set of category limits.</summary>
public sealed record GroceryPlan
{
    public required Guid Id { get; init; }

    public required GroceryPlanKind Kind { get; init; }

    public required string Name { get; init; }

    public IReadOnlyList<GroceryCategoryPlan> Categories { get; init; } = [];

    public FoodAssistance Assistance { get; init; } = new();

    public string? Notes { get; init; }

    /// <summary>
    /// True when a cash grocery total was entered but still needs confirmation that it excludes
    /// tobacco, alcohol and other non-food spending.
    /// </summary>
    public bool CashAmountNeedsConfirmation { get; init; }

    public void Validate()
    {
        if (Id == Guid.Empty)
        {
            throw new ArgumentException("A grocery plan needs an identifier.");
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new ArgumentException("A grocery plan needs a name.");
        }

        Assistance.Validate();

        foreach (var category in Categories)
        {
            category.Validate();
        }

        var duplicate = Categories
            .GroupBy(category => category.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"The grocery category \"{duplicate.Key}\" appears more than once in \"{Name}\".");
        }
    }
}

/// <summary>What a grocery plan costs in cash for one week, and what assistance is covering.</summary>
public sealed record GroceryRequirement
{
    public required GroceryPlanKind Kind { get; init; }

    /// <summary>Cash needed for essential categories not covered by assistance.</summary>
    public required Money EssentialCash { get; init; }

    /// <summary>Cash for optional grocery categories, which are reducible.</summary>
    public required Money OptionalCash { get; init; }

    /// <summary>Value expected in goods rather than cash. Never treated as income.</summary>
    public required Money CoveredByAssistance { get; init; }

    public required IReadOnlyList<string> CategoriesRequiringCash { get; init; }

    public required IReadOnlyList<string> CategoriesSuppliedByAssistance { get; init; }

    public required IReadOnlyList<string> Warnings { get; init; }

    public Money TotalCash => (EssentialCash + OptionalCash).Round();
}

public static class GroceryPlanner
{
    /// <summary>
    /// The default grocery categories. Dining out and takeaway are deliberately absent: they are
    /// discretionary spending, and folding them into groceries is what makes a food budget look
    /// unaffordable when it is not.
    /// </summary>
    public static IReadOnlyList<(string Name, bool IsEssential)> DefaultCategories { get; } =
    [
        ("Meat, poultry and fish", true),
        ("Tinned and shelf-stable food", true),
        ("Fruit", true),
        ("Vegetables", true),
        ("Dairy and eggs", true),
        ("Bread, rice, pasta and other grains", true),
        ("Breakfast food", true),
        ("Frozen food", true),
        ("Pantry staples", true),
        ("Sauces, spices and cooking ingredients", true),
        ("Snacks and soft drinks", false),
        ("Household cleaning products", true),
        ("Toiletries and personal-care products", true),
        ("Pet food and pet supplies", false),
        ("Emergency pantry", false)
    ];

    /// <summary>
    /// Builds a plan from the default categories, filling each suggested amount from local cost
    /// guidance where it exists and leaving it unplanned where it does not.
    /// </summary>
    public static GroceryPlan CreateDefault(
        GroceryPlanKind kind,
        IEnumerable<CostGuidanceRecord> guidance,
        CostLocality locality,
        HouseholdComposition composition,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(guidance);

        var records = guidance.ToList();

        var categories = DefaultCategories
            .Select(entry =>
            {
                var lookup = CostGuidanceLibrary.Find(records, locality, entry.Name, composition, today);
                var record = lookup.HasGuidance ? lookup.Record : null;

                return new GroceryCategoryPlan
                {
                    Id = Guid.NewGuid(),
                    Name = entry.Name,
                    IsEssential = entry.IsEssential,
                    SuggestedWeekly = record?.Typical ?? Money.Zero,
                    LowRange = record?.Low ?? Money.Zero,
                    TypicalRange = record?.Typical ?? Money.Zero,
                    ComfortableRange = record?.Comfortable ?? Money.Zero,
                    WeeklyLimit = record?.Typical ?? Money.Zero,
                    GuidanceSource = record?.DescribeSource() ?? CostGuidanceLookup.NoLocalInformation,
                    GuidanceEffectiveDate = record?.EffectiveDate,
                    Confidence = record?.Confidence ?? GuidanceConfidence.Low,
                    ReviewRequired = lookup.RequiresReview
                };
            })
            .ToList();

        return new GroceryPlan
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            Name = kind.ToDisplayName(),
            Categories = categories
        };
    }

    /// <summary>
    /// Works out the cash a plan needs for one week. Assistance removes the cash requirement only
    /// for the categories it actually supplies, and only while it is in force.
    /// </summary>
    public static GroceryRequirement Require(GroceryPlan plan, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var assistanceApplies = plan.Assistance.AppliesOn(today);

        var essential = Money.Zero;
        var optional = Money.Zero;
        var covered = Money.Zero;
        var cashCategories = new List<string>();
        var assistedCategories = new List<string>();
        var warnings = new List<string>();

        foreach (var category in plan.Categories)
        {
            var suppliedHere = assistanceApplies
                               && (category.SuppliedByAssistance
                                   || plan.Assistance.Supplies(category.Name));

            if (suppliedHere)
            {
                covered += category.AvailableThisWeek;
                assistedCategories.Add(category.Name);
                continue;
            }

            if (category.IsEssential)
            {
                essential += category.AvailableThisWeek;
            }
            else
            {
                optional += category.AvailableThisWeek;
            }

            cashCategories.Add(category.Name);

            if (category.ReviewRequired && category.WeeklyLimit.IsZero)
            {
                warnings.Add(
                    $"No amount is planned for {category.Name} and there is no current local guidance for it.");
            }
            else if (category.ReviewRequired)
            {
                warnings.Add($"The guidance behind {category.Name} requires review.");
            }
        }

        if (plan.Assistance.RequiresReview(today))
        {
            warnings.Add(
                $"Food assistance was due for review on {plan.Assistance.ReviewDate:yyyy-MM-dd}. " +
                "Confirm it is continuing, or the fallback plan applies.");
        }

        if (plan.Assistance is { IsExpected: true, IsSuspended: true })
        {
            warnings.Add(
                "Food assistance is currently suspended, so the full cash requirement applies.");
        }

        if (plan.CashAmountNeedsConfirmation)
        {
            warnings.Add(
                "Confirm that the cash grocery amount excludes tobacco, alcohol and other non-food spending.");
        }

        return new GroceryRequirement
        {
            Kind = plan.Kind,
            EssentialCash = essential.Round(),
            OptionalCash = optional.Round(),
            CoveredByAssistance = covered.Round(),
            CategoriesRequiringCash = cashCategories,
            CategoriesSuppliedByAssistance = assistedCategories,
            Warnings = warnings
        };
    }

    /// <summary>
    /// The extra cash the household would need each week if assistance stopped, stated in plain
    /// terms so the risk is visible before it arrives rather than after.
    /// </summary>
    public static (Money Additional, string Explanation) IfAssistanceEnds(
        GroceryPlan current,
        GroceryPlan? fallback,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(current);

        var now = Require(current, today);

        if (fallback is null)
        {
            var withoutAssistance = Require(
                current with { Assistance = current.Assistance with { IsSuspended = true } },
                today);

            var gap = (withoutAssistance.TotalCash - now.TotalCash).Round();

            return (gap, gap.IsZero
                ? "No food assistance is being relied on, so nothing changes if it ends."
                : $"If food assistance ends, groceries would need a further {gap.ToDisplayString()} " +
                  "a week in cash. There is no separate fallback plan, so this assumes the same shopping.");
        }

        var fallbackRequirement = Require(fallback, today);
        var additional = (fallbackRequirement.TotalCash - now.TotalCash).Round();

        return (additional, additional.IsZero
            ? "The fallback plan costs the same in cash as the current plan."
            : $"If food assistance ends, the fallback plan needs {fallbackRequirement.TotalCash.ToDisplayString()} " +
              $"a week instead of {now.TotalCash.ToDisplayString()}, a further {additional.ToDisplayString()} " +
              "that would have to come out of safe-to-spend.");
    }
}
