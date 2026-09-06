using SecureBudgetManager.Core.Expenses;
using SecureBudgetManager.Core.Guidance;
using SecureBudgetManager.Core.Household;
using SecureBudgetManager.Core.Income;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Storage;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.Tests.Guidance;

public sealed class AllocationOrderAndEssentialTests
{
    [Fact]
    public void PositiveFundingFillsConfiguredBucketsBeforeSurplus()
    {
        var slice = AllocationOrder.AssignByFundingOrder(
            leftover: new Money(200m),
            requestedEntertainment: new Money(40m),
            requestedGoals: new Money(40m),
            requestedDiscretionary: new Money(40m),
            requestedFlexible: new Money(40m));

        Assert.Equal(new Money(40m), slice.Entertainment);
        Assert.Equal(new Money(40m), slice.Goals);
        Assert.Equal(new Money(40m), slice.Discretionary);
        Assert.Equal(new Money(40m), slice.FlexibleSavings);
        Assert.Equal(new Money(40m), slice.Surplus);
        Assert.Equal(new Money(200m), slice.Total);
    }

    [Fact]
    public void PositiveFundingWalksFamilyBeforeGrowthBeforeDiscretionary()
    {
        var slice = AllocationOrder.AssignByFundingOrder(
            leftover: new Money(100m),
            requestedEntertainment: new Money(40m),
            requestedGoals: new Money(40m),
            requestedDiscretionary: new Money(40m),
            requestedFlexible: new Money(40m));

        Assert.Equal(new Money(40m), slice.Entertainment);
        Assert.Equal(new Money(40m), slice.Goals);
        Assert.Equal(new Money(20m), slice.Discretionary);
        Assert.Equal(Money.Zero, slice.FlexibleSavings);
        Assert.Equal(Money.Zero, slice.Surplus);
    }

    [Fact]
    public void ShortfallReductionConsumesSurplusFirstThenDiscretionary()
    {
        var funded = new AllocationSlice(
            new Money(40m),
            new Money(40m),
            new Money(40m),
            new Money(40m),
            new Money(30m));

        var reduced = AllocationOrder.ReduceByReductionOrder(funded, new Money(50m));

        Assert.Equal(Money.Zero, reduced.Surplus);
        Assert.Equal(new Money(20m), reduced.Discretionary);
        Assert.Equal(new Money(40m), reduced.Entertainment);
        Assert.Equal(new Money(40m), reduced.FlexibleSavings);
        Assert.Equal(new Money(40m), reduced.Goals);
    }

    [Fact]
    public void FillConfiguredLeavesSurplusOnlyAsTheRemainder()
    {
        var slice = AllocationOrder.FillConfiguredOrReduce(
            leftover: new Money(130m),
            requestedEntertainment: new Money(20m),
            requestedGoals: new Money(20m),
            requestedDiscretionary: new Money(20m),
            requestedFlexible: new Money(20m));

        Assert.Equal(new Money(50m), slice.Surplus);
        Assert.Equal(new Money(130m), slice.Total);
    }

    [Fact]
    public void CustomFundingOrderChangesTheDollarAssignment()
    {
        var custom = new FundingBucket[]
        {
            FundingBucket.FlexibleAllocations,
            FundingBucket.PersonalDiscretionary,
            FundingBucket.FamilyAndBelonging,
            FundingBucket.Growth
        };

        var slice = AllocationOrder.AssignByFundingOrder(
            leftover: new Money(50m),
            requestedEntertainment: new Money(40m),
            requestedGoals: new Money(40m),
            requestedDiscretionary: new Money(40m),
            requestedFlexible: new Money(40m),
            custom);

        Assert.Equal(new Money(40m), slice.FlexibleSavings);
        Assert.Equal(new Money(10m), slice.Discretionary);
        Assert.Equal(Money.Zero, slice.Entertainment);
    }

    [Fact]
    public void RequiredFundedAndUnfundedEssentialsStaySeparate()
    {
        var document = TightHousehold(new Money(700m), new Money(800m));
        var allocation = PaychequeAllocator.Allocate(document, new DateOnly(2026, 3, 6));

        Assert.Equal(allocation.Essentials.Required, (allocation.Essentials.Funded + allocation.Essentials.Unfunded).Round());
        Assert.True(allocation.Essentials.Required > allocation.Essentials.Funded);
        Assert.True(allocation.Essentials.Unfunded > Money.Zero);
        Assert.True(allocation.Essentials.Available >= new Money(700m));
        Assert.True(allocation.Safety is SpendingSafety.Unsafe or SpendingSafety.NotRecommended or SpendingSafety.InsufficientInformation);
        Assert.True(allocation.Ledger.ReconcilesExactly);
        Assert.DoesNotContain(allocation.Reductions, item => item.Bucket == ReducibleBucket.UnallocatedSurplus && item.Allocated < Money.Zero);
    }

    [Fact]
    public void ConservativeIncomeDoesNotConcealTheRequiredEssentialAmount()
    {
        var document = TightHousehold(new Money(700m), new Money(800m));
        var allocation = PaychequeAllocator.Allocate(document, new DateOnly(2026, 3, 6));

        Assert.True(allocation.Essentials.Required > allocation.Essentials.Funded);
        Assert.Contains("required", allocation.Essentials.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("funded", allocation.Essentials.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("shortfall", allocation.Essentials.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    private static BudgetDocument TightHousehold(Money available, Money rent)
    {
        var adult = Guid.NewGuid();

        return new BudgetDocument
        {
            Members = [new HouseholdMember { Id = adult, Name = "Adult", IsDiscretionaryEligible = true }],
            Accounts =
            [
                new BankAccount
                {
                    Id = Guid.NewGuid(),
                    Name = "Checking",
                    CurrentBalance = available,
                    IsPrimary = true
                }
            ],
            IncomeSources =
            [
                new HourlyIncome
                {
                    Id = Guid.NewGuid(),
                    Name = "Work",
                    MemberId = adult,
                    PayFrequency = Frequency.Weekly,
                    AnchorPayDate = new DateOnly(2026, 3, 13),
                    HourlyRate = new Money(1m),
                    WeeklyHours = VariableHours.Fixed(1m)
                }
            ],
            Expenses =
            [
                new ExpenseItem
                {
                    Id = Guid.NewGuid(),
                    Name = "Rent",
                    Category = ExpenseCategory.Housing,
                    ExpectedAmount = rent,
                    Frequency = Frequency.Monthly,
                    AnchorDueDate = new DateOnly(2026, 3, 8),
                    Necessity = ExpenseNecessity.Essential
                }
            ],
            Rules = new AllocationRules
            {
                FlexibleSavingsPerPayday = new Money(20m),
                SharedEntertainmentPerPayday = new Money(20m)
            }
        };
    }
}
