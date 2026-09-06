using System.Security.Cryptography;
using System.Text;
using SecureBudgetManager.Core.Models;

namespace SecureBudgetManager.Core.Guidance;

/// <summary>
/// The bundled, versioned starter pack. It is compiled into the application so the programme
/// works offline. It is not household data and must never be uploaded. Importing it into the
/// encrypted store is an explicit user action and never overwrites a household override.
/// </summary>
public sealed record GuidancePackInfo(
    string Version,
    DateOnly EffectiveDate,
    DateOnly PublishedOn,
    DateOnly ReviewedOn,
    DateOnly ReviewByDate,
    string Notes);

public static class StarterGuidancePack
{
    public const string Version = "2026.09.1";

    public static readonly DateOnly EffectiveDate = new(2026, 7, 1);

    public static readonly DateOnly PublishedOn = new(2026, 8, 26);

    public static readonly DateOnly ReviewedOn = new(2026, 9, 3);

    public static readonly DateOnly ReviewByDate = new(2026, 10, 31);

    public static GuidancePackInfo Info { get; } = new(
        Version,
        EffectiveDate,
        PublishedOn,
        ReviewedOn,
        ReviewByDate,
        "Bundled starter guidance. Not permanently current. Review by the review date. " +
        "USDA Food Plans are national averages; USDA does not publish a Utah- or " +
        "Taylorsville-specific food-plan cost.");

    /// <summary>
    /// Official USDA Thrifty Food Plan, 2021 cost shares (reference-family chart in
    /// Thrifty Food Plan, 2021, USDA FNS/CNPP). Used only to allocate a sourced total.
    /// </summary>
    public static IReadOnlyDictionary<string, decimal> UsdaTfp2021CostShares { get; } =
        new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["Vegetables"] = 0.24m,
            ["Fruit"] = 0.14m,
            ["Bread, rice, pasta and other grains"] = 0.16m,
            ["Dairy and eggs"] = 0.14m,
            ["Meat, poultry and fish"] = 0.25m,
            ["Pantry staples"] = 0.07m
        };

    public static IReadOnlyList<CostGuidanceRecord> Records { get; } = Build();

    public static IReadOnlyList<CostGuidanceRecord> ImportMissing(
        IReadOnlyList<CostGuidanceRecord> existing)
    {
        ArgumentNullException.ThrowIfNull(existing);

        var keys = existing
            .Select(record => Key(record.Locality, record.Category, record.Composition))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return Records
            .Where(record => !keys.Contains(Key(record.Locality, record.Category, record.Composition)))
            .ToList();
    }

    private static string Key(CostLocality locality, string category, HouseholdComposition composition) =>
        $"{locality.Describe()}|{category}|{composition.Adults}|{composition.Children}";

    private static IReadOnlyList<CostGuidanceRecord> Build()
    {
        var records = new List<CostGuidanceRecord>();
        var us = CostLocality.UnitedStates;
        var utah = CostLocality.Utah;
        var slc = CostLocality.SaltLakeCounty;
        var taylorsville = new CostLocality
        {
            State = "Utah",
            County = "Salt Lake County",
            City = "Taylorsville"
        };

        // Two adults 20–50 plus one child 9–11, USDA July 2026, +5% three-person adjustment.
        AddUsdaHousehold(
            records,
            us,
            new HouseholdComposition(2, 1),
            thriftyWeekly: 197.60m,
            lowWeekly: 209.70m,
            moderateWeekly: 263.10m,
            liberalWeekly: 321.30m,
            "Male 20–50 $73.70 + female 20–50 $58.60 + child 9–11 $55.90 = $188.20; " +
            "USDA three-person adjustment +5% = $197.61, rounded to $197.60 (Thrifty). " +
            "Low-Cost $73.30+$63.70+$62.70=$199.70 × 1.05 = $209.69 → $209.70. " +
            "Moderate-Cost $92.00+$77.80+$80.80=$250.60 × 1.05 = $263.13 → $263.10. " +
            "Liberal $112.50+$99.30+$94.20=$306.00 × 1.05 = $321.30.");

        // Two adults 20–50, USDA July 2026, +10% two-person adjustment.
        AddUsdaHousehold(
            records,
            us,
            new HouseholdComposition(2, 0),
            thriftyWeekly: 145.50m,
            lowWeekly: 150.70m,
            moderateWeekly: 186.80m,
            liberalWeekly: 233.00m,
            "Male 20–50 + female 20–50, USDA two-person adjustment +10%. " +
            "Thrifty ($73.70+$58.60)×1.10=$145.53 → $145.50. " +
            "Low-Cost ($73.30+$63.70)×1.10=$150.70. " +
            "Moderate-Cost ($92.00+$77.80)×1.10=$186.78 → $186.80. " +
            "Liberal ($112.50+$99.30)×1.10=$232.98 → $233.00.");

        foreach (var locality in new[] { utah, slc, taylorsville })
        {
            records.Add(NationalAverageNote(locality, new HouseholdComposition(2, 1)));
            records.Add(NationalAverageNote(locality, new HouseholdComposition(2, 0)));
        }

        return records;
    }

    private static void AddUsdaHousehold(
        List<CostGuidanceRecord> records,
        CostLocality locality,
        HouseholdComposition composition,
        decimal thriftyWeekly,
        decimal lowWeekly,
        decimal moderateWeekly,
        decimal liberalWeekly,
        string calculation)
    {
        var assumptions =
            "All meals and snacks prepared at home. Costs are for individuals in four-person " +
            "households before the USDA household-size adjustment. Adults are priced as ages 20–50; " +
            "the child, where present, as ages 9–11. USDA rounds to the nearest 10 cents. " +
            "These are national averages, not Utah shelf prices.";

        records.Add(Direct(
            locality,
            composition,
            "Total groceries",
            lowWeekly,
            moderateWeekly,
            liberalWeekly,
            calculation,
            assumptions,
            "Official USDA Food Plans: Thrifty (low), Moderate-Cost (typical), Liberal (comfortable). " +
            "Low-Cost is also published and is shown in the calculation."));

        foreach (var (category, share) in UsdaTfp2021CostShares)
        {
            var low = MoneyRound(thriftyWeekly * share);
            var typical = MoneyRound(moderateWeekly * share);
            var comfortable = MoneyRound(liberalWeekly * share);
            var sourceTotal = new Money(moderateWeekly);

            records.Add(new CostGuidanceRecord
            {
                Id = PackId($"{locality.Describe()}|{composition.Describe()}|{category}"),
                Locality = locality,
                Composition = composition,
                Category = category,
                EffectiveDate = EffectiveDate,
                SourceName = "USDA Food Plans (July 2026 totals) × Thrifty Food Plan, 2021 cost shares",
                SourceType = CostGuidanceSourceType.DerivedEstimate,
                ObservedOn = PublishedOn,
                Low = low,
                Typical = typical,
                Comfortable = comfortable,
                Confidence = GuidanceConfidence.Medium,
                LastReviewedOn = ReviewedOn,
                ReviewByDate = ReviewByDate,
                SourceUrl = "https://www.fns.usda.gov/research/cnpp/usda-food-plans/cost-food-monthly-reports",
                SourceIdentifier = "USDA TFP 2021 cost-share chart; July 2026 Monthly Cost of Food Reports",
                Assumptions = assumptions + " Subcategory dollars are not published by USDA for this category list.",
                UnitOrFrequency = "Weekly, household",
                IsDerived = true,
                SourceTotal = sourceTotal,
                AllocationMethod =
                    "Multiply the USDA household weekly total by the official TFP 2021 cost share " +
                    $"for the matching major food group ({share:P0}).",
                Calculation =
                    $"{sourceTotal.ToDisplayString()} × {share:P0} = {typical.ToDisplayString()} typical. " +
                    $"Low uses Thrifty {new Money(thriftyWeekly).ToDisplayString()} × {share:P0}; " +
                    $"comfortable uses Liberal {new Money(liberalWeekly).ToDisplayString()} × {share:P0}.",
                PackVersion = Version,
                PriceKind = PriceKind.BundledSuggested,
                FreshnessDays = 90,
                Notes =
                    "Derived estimate. USDA publishes official totals, not this subcategory split. " +
                    "Editable. Not a Utah-specific price."
            });
        }
    }

    private static CostGuidanceRecord Direct(
        CostLocality locality,
        HouseholdComposition composition,
        string category,
        decimal low,
        decimal typical,
        decimal comfortable,
        string calculation,
        string assumptions,
        string notes) =>
        new()
        {
            Id = PackId($"{locality.Describe()}|{composition.Describe()}|{category}"),
            Locality = locality,
            Composition = composition,
            Category = category,
            EffectiveDate = EffectiveDate,
            SourceName = "USDA Food Plans: Monthly Cost of Food Reports, July 2026",
            SourceType = CostGuidanceSourceType.OfficialGuidance,
            ObservedOn = PublishedOn,
            Low = new Money(low),
            Typical = new Money(typical),
            Comfortable = new Money(comfortable),
            Confidence = GuidanceConfidence.High,
            LastReviewedOn = ReviewedOn,
            ReviewByDate = ReviewByDate,
            SourceUrl = "https://www.fns.usda.gov/research/cnpp/usda-food-plans/cost-food-monthly-reports",
            SourceIdentifier = "USDA FNS/CNPP Monthly Cost of Food Reports, July 2026, page updated 26 August 2026",
            Assumptions = assumptions,
            UnitOrFrequency = "Weekly, household",
            IsDerived = false,
            AllocationMethod = "Direct official totals after the USDA household-size adjustment.",
            Calculation = calculation,
            PackVersion = Version,
            PriceKind = PriceKind.BundledSuggested,
            FreshnessDays = 90,
            Notes = notes
        };

    private static CostGuidanceRecord NationalAverageNote(
        CostLocality locality,
        HouseholdComposition composition) =>
        new()
        {
            Id = PackId($"{locality.Describe()}|{composition.Describe()}|local-grocery-unavailable"),
            Locality = locality,
            Composition = composition,
            Category = "Total groceries (local official)",
            EffectiveDate = EffectiveDate,
            SourceName = "No official USDA or Utah state food-plan cost is published for this locality",
            SourceType = CostGuidanceSourceType.OfficialGuidance,
            ObservedOn = PublishedOn,
            Confidence = GuidanceConfidence.Low,
            LastReviewedOn = ReviewedOn,
            ReviewByDate = ReviewByDate,
            SourceUrl = "https://www.fns.usda.gov/research/cnpp/usda-food-plans/cost-food-monthly-reports",
            SourceIdentifier = "USDA Food Plans are national averages only",
            Assumptions = "A Utah, Salt Lake County or Taylorsville official grocery-plan total was not found.",
            UnitOrFrequency = "Weekly, household",
            PackVersion = Version,
            PriceKind = PriceKind.BundledSuggested,
            FreshnessDays = 90,
            Notes = CostGuidanceLookup.NoLocalInformation +
                    ". Use the United States USDA totals in this pack; they are national averages."
        };

    private static Money MoneyRound(decimal amount) =>
        new(Math.Round(amount, 2, MidpointRounding.ToEven));

    private static Guid PackId(string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("sbm-starter-guidance:" + key));
        return new Guid(hash.AsSpan(0, 16));
    }
}
