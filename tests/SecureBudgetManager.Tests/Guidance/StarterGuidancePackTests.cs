using SecureBudgetManager.Core.Guidance;
using SecureBudgetManager.Core.Models;

namespace SecureBudgetManager.Tests.Guidance;

public sealed class StarterGuidancePackTests
{
    private static readonly DateOnly Today = new(2026, 9, 3);

    [Fact]
    public void PackRecordsIdentifySourceDatesAndGeography()
    {
        Assert.Equal("2026.09.1", StarterGuidancePack.Version);
        Assert.Equal(new DateOnly(2026, 7, 1), StarterGuidancePack.EffectiveDate);
        Assert.Equal(new DateOnly(2026, 10, 31), StarterGuidancePack.ReviewByDate);

        Assert.Contains(StarterGuidancePack.Records, record =>
            record.Category == "Total groceries"
            && record.SourceType == CostGuidanceSourceType.OfficialGuidance
            && record.SourceUrl!.Contains("fns.usda.gov", StringComparison.OrdinalIgnoreCase)
            && record.ObservedOn == StarterGuidancePack.PublishedOn
            && record.Locality.Country == "United States"
            && !record.IsDerived);

        Assert.Contains(StarterGuidancePack.Records, record =>
            record.IsDerived
            && record.SourceType == CostGuidanceSourceType.DerivedEstimate
            && record.SourceTotal is not null
            && !string.IsNullOrWhiteSpace(record.AllocationMethod)
            && !string.IsNullOrWhiteSpace(record.Calculation));
    }

    [Fact]
    public void UtahAndTaylorsvilleOfficialGroceryTotalsAreMissingNotInvented()
    {
        var utah = StarterGuidancePack.Records.Single(record =>
            record.Locality.City is null
            && record.Locality.County is null
            && record.Locality.State == "Utah"
            && record.Category.Contains("local official", StringComparison.OrdinalIgnoreCase)
            && record.Composition.Children == 1);

        Assert.False(utah.HasAmounts);
        Assert.Contains(CostGuidanceLookup.NoLocalInformation, utah.Notes, StringComparison.Ordinal);

        var lookup = CostGuidanceLibrary.Find(
            StarterGuidancePack.Records,
            new CostLocality { State = "Utah", County = "Salt Lake County", City = "Taylorsville" },
            "Fuel",
            new HouseholdComposition(2, 1),
            Today);

        Assert.False(lookup.HasGuidance);
        Assert.Contains(CostGuidanceLookup.NoLocalInformation, lookup.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpiredGuidanceStaysVisibleAndIsLabelledReviewRequired()
    {
        var expired = StarterGuidancePack.Records.First(record => record.HasAmounts) with
        {
            ReviewByDate = Today.AddDays(-1)
        };

        Assert.Equal(GuidanceFreshness.Expired, expired.Freshness(Today));
        Assert.Equal("Review required", expired.FreshnessLabel(Today));
        Assert.True(expired.RequiresReview(Today));
    }

    [Fact]
    public void ImportDoesNotOverwriteAHouseholdOverride()
    {
        var existing = StarterGuidancePack.Records[0] with
        {
            Typical = new Money(12m),
            Low = new Money(10m),
            Comfortable = new Money(14m),
            UserSelectedAmount = new Money(11m),
            SourceName = "Household override"
        };

        var added = StarterGuidancePack.ImportMissing([existing]);

        Assert.DoesNotContain(added, record => record.Id == existing.Id);
        Assert.Equal("Household override", existing.SourceName);
        Assert.Equal(new Money(11m), existing.UserSelectedAmount);
    }
}
