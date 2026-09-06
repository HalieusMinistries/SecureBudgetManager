using SecureBudgetManager.Core.Expenses;

namespace SecureBudgetManager.Core.Guidance;

/// <summary>
/// A modern household adaptation of Maslow's hierarchy, used to decide what is protected first
/// when money is short. The order is deliberate: a household that keeps its housing and medicine
/// but loses its hobbies has had a bad month, whereas the reverse has had a crisis.
///
/// The tiers guide recommendations; they do not judge. Spending in the lower tiers is a normal
/// part of a sustainable budget, not a failure.
/// </summary>
public enum NeedTier
{
    /// <summary>Housing, essential food, water, essential medicine, immediate healthcare.</summary>
    Survival = 1,

    /// <summary>Essential utilities, required insurance, transport to work, minimum debt payments.</summary>
    Safety = 2,

    /// <summary>Upcoming bills, sinking funds, emergency reserves, predictable irregular obligations.</summary>
    Stability = 3,

    /// <summary>Shared family activities, worship, hospitality, modest entertainment.</summary>
    FamilyAndBelonging = 4,

    /// <summary>Education, retirement, longer-term savings, planned purchases, holidays.</summary>
    Growth = 5,

    /// <summary>Hobbies, dining out, alcohol, tobacco, optional subscriptions, personal spending.</summary>
    DiscretionaryFreedom = 6
}

public static class NeedTierExtensions
{
    public static string ToDisplayName(this NeedTier tier) => tier switch
    {
        NeedTier.Survival => "Survival",
        NeedTier.Safety => "Safety",
        NeedTier.Stability => "Stability",
        NeedTier.FamilyAndBelonging => "Family and belonging",
        NeedTier.Growth => "Growth",
        NeedTier.DiscretionaryFreedom => "Discretionary freedom",
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown need tier.")
    };

    public static string Describe(this NeedTier tier) => tier switch
    {
        NeedTier.Survival =>
            "Housing, essential food, water, essential medicine and immediate healthcare.",
        NeedTier.Safety =>
            "Essential utilities, legally required insurance, transport to work, minimum debt payments " +
            "and essential medical coverage.",
        NeedTier.Stability =>
            "Upcoming bills, sinking funds, emergency reserves and predictable irregular obligations.",
        NeedTier.FamilyAndBelonging =>
            "Shared family activities, worship, hospitality, community and modest entertainment.",
        NeedTier.Growth =>
            "Education, retirement, longer-term savings, planned purchases and household goals.",
        NeedTier.DiscretionaryFreedom =>
            "Hobbies, dining out, optional subscriptions and personal spending.",
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown need tier.")
    };

    /// <summary>
    /// Tiers one to three fund the household's security. They are never reduced automatically,
    /// only by an explicit decision the household makes and can see the consequences of.
    /// </summary>
    public static bool IsProtectedByDefault(this NeedTier tier) => tier <= NeedTier.Stability;

    public static IReadOnlyList<NeedTier> All { get; } =
    [
        NeedTier.Survival,
        NeedTier.Safety,
        NeedTier.Stability,
        NeedTier.FamilyAndBelonging,
        NeedTier.Growth,
        NeedTier.DiscretionaryFreedom
    ];
}

/// <summary>
/// One configurable line of the hierarchy: which tier a named spending category belongs to.
/// Households differ, so every default here can be changed.
/// </summary>
public sealed record NeedCategoryRule
{
    public required string CategoryName { get; init; }

    public required NeedTier Tier { get; init; }

    /// <summary>
    /// True when money in this category must not be reduced automatically, even if the tier
    /// would normally allow it. Used for things like a court-ordered payment in a low tier.
    /// </summary>
    public bool IsAlwaysProtected { get; init; }

    public string? Notes { get; init; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(CategoryName))
        {
            throw new ArgumentException("A hierarchy rule needs a category name.");
        }

        if (!Enum.IsDefined(Tier))
        {
            throw new ArgumentException($"\"{CategoryName}\" refers to an unknown need tier.");
        }
    }
}

/// <summary>
/// The household's configured mapping from spending category to need tier.
///
/// Lookup is by category name, case-insensitively, with a fallback that also considers whether
/// the expense was marked essential. An unrecognised essential cost is treated as Stability
/// rather than Survival: it is protected from casual reduction but does not outrank rent.
/// </summary>
public sealed record NeedsHierarchy
{
    public IReadOnlyList<NeedCategoryRule> Rules { get; init; } = [];

    /// <summary>The tier used when a category has no rule and the expense is marked essential.</summary>
    public NeedTier FallbackForEssential { get; init; } = NeedTier.Stability;

    /// <summary>The tier used when a category has no rule and the expense is marked optional.</summary>
    public NeedTier FallbackForOptional { get; init; } = NeedTier.DiscretionaryFreedom;

    public static NeedsHierarchy Default { get; } = new() { Rules = DefaultRules() };

    public bool IsDefault => Rules.Count == 0
                             || Rules.SequenceEqual(DefaultRules());

    public NeedTier TierFor(ExpenseItem expense)
    {
        ArgumentNullException.ThrowIfNull(expense);
        return TierFor(expense.Category.Name, expense.Necessity);
    }

    public NeedTier TierFor(string categoryName, ExpenseNecessity necessity)
    {
        var rule = FindRule(categoryName);

        if (rule is not null)
        {
            return rule.Tier;
        }

        return necessity == ExpenseNecessity.Essential ? FallbackForEssential : FallbackForOptional;
    }

    public bool IsProtected(string categoryName, ExpenseNecessity necessity)
    {
        var rule = FindRule(categoryName);

        if (rule is { IsAlwaysProtected: true })
        {
            return true;
        }

        return TierFor(categoryName, necessity).IsProtectedByDefault();
    }

    public NeedCategoryRule? FindRule(string categoryName) =>
        string.IsNullOrWhiteSpace(categoryName)
            ? null
            : Rules.FirstOrDefault(rule =>
                string.Equals(rule.CategoryName, categoryName.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Replaces or adds one rule, leaving the rest of the configuration untouched.</summary>
    public NeedsHierarchy With(NeedCategoryRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        rule.Validate();

        var kept = Rules
            .Where(existing => !string.Equals(
                existing.CategoryName,
                rule.CategoryName.Trim(),
                StringComparison.OrdinalIgnoreCase))
            .ToList();

        kept.Add(rule with { CategoryName = rule.CategoryName.Trim() });

        return this with
        {
            Rules = kept.OrderBy(item => item.Tier).ThenBy(item => item.CategoryName).ToList()
        };
    }

    public void Validate()
    {
        foreach (var rule in Rules)
        {
            rule.Validate();
        }

        var duplicate = Rules
            .GroupBy(rule => rule.CategoryName.Trim(), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"The category \"{duplicate.Key}\" appears more than once in the needs hierarchy.");
        }

        if (!Enum.IsDefined(FallbackForEssential) || !Enum.IsDefined(FallbackForOptional))
        {
            throw new ArgumentException("The needs hierarchy refers to an unknown fallback tier.");
        }
    }

    private static List<NeedCategoryRule> DefaultRules() =>
    [
        Rule(ExpenseCategory.Housing.Name, NeedTier.Survival),
        Rule(GuidanceCategories.EssentialFood, NeedTier.Survival),
        Rule(GuidanceCategories.Water, NeedTier.Survival),
        Rule(GuidanceCategories.EssentialMedicine, NeedTier.Survival),
        Rule(ExpenseCategory.Healthcare.Name, NeedTier.Survival),

        Rule(ExpenseCategory.Utilities.Name, NeedTier.Safety),
        Rule(ExpenseCategory.Insurance.Name, NeedTier.Safety),
        Rule(GuidanceCategories.EssentialTransport, NeedTier.Safety),
        Rule(ExpenseCategory.Transport.Name, NeedTier.Safety),
        Rule(ExpenseCategory.DebtPayments.Name, NeedTier.Safety),
        Rule(GuidanceCategories.HouseholdSecurity, NeedTier.Safety),

        Rule(GuidanceCategories.SinkingFunds, NeedTier.Stability),
        Rule(GuidanceCategories.EmergencyReserve, NeedTier.Stability),
        Rule(GuidanceCategories.WorkCommunication, NeedTier.Stability),
        Rule(GuidanceCategories.VehicleMaintenance, NeedTier.Stability),
        Rule(ExpenseCategory.Childcare.Name, NeedTier.Stability),

        Rule(GuidanceCategories.SharedFamilyEntertainment, NeedTier.FamilyAndBelonging),
        Rule(GuidanceCategories.ChurchAndCommunity, NeedTier.FamilyAndBelonging),
        Rule(GuidanceCategories.Hospitality, NeedTier.FamilyAndBelonging),

        Rule(ExpenseCategory.Savings.Name, NeedTier.Growth),
        Rule(GuidanceCategories.Education, NeedTier.Growth),
        Rule(GuidanceCategories.Retirement, NeedTier.Growth),
        Rule(GuidanceCategories.PlannedPurchases, NeedTier.Growth),
        Rule(GuidanceCategories.HolidaysAndTravel, NeedTier.Growth),

        Rule(GuidanceCategories.Hobbies, NeedTier.DiscretionaryFreedom),
        Rule(GuidanceCategories.DiningOut, NeedTier.DiscretionaryFreedom),
        Rule(ExpenseCategory.Subscriptions.Name, NeedTier.DiscretionaryFreedom),
        Rule(GuidanceCategories.Alcohol, NeedTier.DiscretionaryFreedom),
        Rule(GuidanceCategories.Tobacco, NeedTier.DiscretionaryFreedom),
        Rule(GuidanceCategories.ClothingBeyondNecessity, NeedTier.DiscretionaryFreedom),
        Rule(GuidanceCategories.NonEssentialShopping, NeedTier.DiscretionaryFreedom),
        Rule(GuidanceCategories.GiftsOutsidePlannedFunds, NeedTier.DiscretionaryFreedom),
        Rule(ExpenseCategory.Personal.Name, NeedTier.DiscretionaryFreedom)
    ];

    private static NeedCategoryRule Rule(string category, NeedTier tier) =>
        new() { CategoryName = category, Tier = tier };
}
