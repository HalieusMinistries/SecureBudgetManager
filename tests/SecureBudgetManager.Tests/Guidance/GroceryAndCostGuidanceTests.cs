using SecureBudgetManager.Core.Guidance;
using SecureBudgetManager.Core.Models;

namespace SecureBudgetManager.Tests.Guidance;

public sealed class GroceryAndCostGuidanceTests
{
    private static readonly DateOnly Today = new(2026, 3, 6);

    [Fact]
    public void DefaultCategoriesKeepDiningOutSeparate()
    {
        Assert.Contains(GroceryPlanner.DefaultCategories, item => item.Name == "Meat, poultry and fish" && item.IsEssential);
        Assert.Contains(GroceryPlanner.DefaultCategories, item => item.Name == "Snacks and soft drinks" && !item.IsEssential);
        Assert.DoesNotContain(GroceryPlanner.DefaultCategories, item => item.Name.Contains("dining", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(GroceryPlanner.DefaultCategories, item => item.Name.Contains("takeaway", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FoodAssistanceReducesOnlySuppliedCategories()
    {
        var plan = new GroceryPlan
        {
            Id = Guid.NewGuid(),
            Kind = GroceryPlanKind.Current,
            Name = "Current",
            Assistance = new FoodAssistance
            {
                IsExpected = true,
                EstimatedWeeklyValue = new Money(40m),
                CategoriesSupplied = ["Vegetables"],
                EffectiveDate = Today.AddDays(-1)
            },
            Categories =
            [
                new GroceryCategoryPlan { Id = Guid.NewGuid(), Name = "Vegetables", IsEssential = true, WeeklyLimit = new Money(30m) },
                new GroceryCategoryPlan { Id = Guid.NewGuid(), Name = "Meat, poultry and fish", IsEssential = true, WeeklyLimit = new Money(45m) }
            ]
        };

        var current = GroceryPlanner.Require(plan, Today);
        Assert.Equal(new Money(45m), current.EssentialCash);
        Assert.Equal(new Money(30m), current.CoveredByAssistance);
        Assert.Contains("Vegetables", current.CategoriesSuppliedByAssistance);
        Assert.DoesNotContain("Meat, poultry and fish", current.CategoriesSuppliedByAssistance);
    }

    [Fact]
    public void EndingAssistanceRaisesTheCashRequirement()
    {
        var current = new GroceryPlan
        {
            Id = Guid.NewGuid(),
            Kind = GroceryPlanKind.Current,
            Name = "Current",
            Assistance = new FoodAssistance
            {
                IsExpected = true,
                CategoriesSupplied = ["Vegetables"],
                EffectiveDate = Today.AddDays(-7)
            },
            Categories =
            [
                new GroceryCategoryPlan { Id = Guid.NewGuid(), Name = "Vegetables", IsEssential = true, WeeklyLimit = new Money(30m) }
            ]
        };

        var fallback = new GroceryPlan
        {
            Id = Guid.NewGuid(),
            Kind = GroceryPlanKind.FallbackWithoutAssistance,
            Name = "Fallback",
            Categories =
            [
                new GroceryCategoryPlan { Id = Guid.NewGuid(), Name = "Vegetables", IsEssential = true, WeeklyLimit = new Money(30m) }
            ]
        };

        var (additional, explanation) = GroceryPlanner.IfAssistanceEnds(current, fallback, Today);
        Assert.Equal(new Money(30m), additional);
        Assert.Contains("fallback", explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OutdatedGuidanceIsFlaggedAndMissingGuidanceIsNamed()
    {
        var records = new[]
        {
            new CostGuidanceRecord
            {
                Id = Guid.NewGuid(),
                Locality = CostLocality.SaltLakeCounty,
                Category = "Fruit",
                EffectiveDate = new DateOnly(2024, 1, 1),
                SourceName = "Old notebook",
                SourceType = CostGuidanceSourceType.UserEstimate,
                Low = new Money(10m),
                Typical = new Money(12m),
                Comfortable = new Money(15m),
                ReviewByDate = new DateOnly(2025, 1, 1)
            }
        };

        var outdated = CostGuidanceLibrary.Find(
            records,
            CostLocality.SaltLakeCounty,
            "Fruit",
            new HouseholdComposition(2, 1),
            Today);

        Assert.True(outdated.HasGuidance);
        Assert.True(outdated.RequiresReview);
        Assert.Contains("requires review", outdated.Explanation, StringComparison.OrdinalIgnoreCase);

        var missing = CostGuidanceLibrary.Find(
            [],
            CostLocality.SaltLakeCounty,
            "Dairy and eggs",
            new HouseholdComposition(2, 1),
            Today);

        Assert.False(missing.HasGuidance);
        Assert.Contains(CostGuidanceLookup.NoLocalInformation, missing.Explanation);
    }

    [Fact]
    public void EmergencyPlanIsDistinctFromCurrent()
    {
        Assert.NotEqual(GroceryPlanKind.Current, GroceryPlanKind.EmergencyMinimum);
        Assert.NotEqual(GroceryPlanKind.Current, GroceryPlanKind.FallbackWithoutAssistance);
    }
}
