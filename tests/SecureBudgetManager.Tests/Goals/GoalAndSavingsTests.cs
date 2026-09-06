using SecureBudgetManager.Core.Goals;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Savings;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.Tests.Goals;

public sealed class GoalAndSavingsTests
{
    private static readonly DateOnly Today = new(2026, 3, 1);

    [Fact]
    public void TheRequiredWeeklyContributionReachesTheTargetOnTime()
    {
        var goal = new Goal
        {
            Id = Guid.NewGuid(),
            Name = "Car deposit",
            TargetAmount = new Money(2600m),
            TargetDate = Today.AddDays(364),
            CurrentAmount = Money.Zero
        };

        var required = goal.RequiredContribution(Today, Frequency.Weekly);

        // 2,600 over roughly 52 weeks.
        Assert.True(required >= new Money(49m));
        Assert.True(required <= new Money(51m));
    }

    [Fact]
    public void AGoalWithNoContributionIsReportedAsUnreachable()
    {
        var goal = Goal("Holiday", new Money(3000m), Today.AddYears(1), Money.Zero);

        var status = Assert.Single(GoalPlanner.Review([goal], Today, Frequency.Monthly));

        Assert.False(status.OnTrack);
        Assert.Null(status.ProjectedCompletion);
        Assert.Contains("will not be reached", status.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AGoalContributingEnoughIsOnTrack()
    {
        var goal = Goal("Holiday", new Money(1200m), Today.AddYears(1), new Money(120m));

        var status = Assert.Single(GoalPlanner.Review([goal], Today, Frequency.Monthly));

        Assert.True(status.OnTrack);
        Assert.NotNull(status.ProjectedCompletion);
    }

    [Fact]
    public void AnUnderfundedGoalNamesTheContributionItActuallyNeeds()
    {
        var goal = Goal("Holiday", new Money(3000m), Today.AddYears(1), new Money(50m));

        var status = Assert.Single(GoalPlanner.Review([goal], Today, Frequency.Monthly));

        Assert.False(status.OnTrack);
        Assert.Contains(status.RequiredContribution.ToDisplayString(), status.Explanation);
        Assert.Contains("after your", status.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ACompleteGoalNeedsNothingFurther()
    {
        var goal = Goal("Done", new Money(500m), Today.AddYears(1), new Money(10m)) with
        {
            CurrentAmount = new Money(500m)
        };

        Assert.True(goal.IsComplete);
        Assert.Equal(Money.Zero, goal.Remaining);
        Assert.Equal(100m, goal.ProgressPercent);
        Assert.Equal(Money.Zero, goal.RequiredContribution(Today, Frequency.Weekly));
    }

    [Fact]
    public void AnOverfundedGoalDoesNotReportMoreThanOneHundredPercent()
    {
        var goal = Goal("Overfunded", new Money(500m), Today.AddYears(1), Money.Zero) with
        {
            CurrentAmount = new Money(700m)
        };

        Assert.Equal(100m, goal.ProgressPercent);
    }

    [Fact]
    public void APastDeadlineMeansTheWholeRemainderIsNeededNow()
    {
        var goal = Goal("Overdue", new Money(800m), Today.AddDays(-10), Money.Zero) with
        {
            CurrentAmount = new Money(300m)
        };

        Assert.Equal(new Money(500m), goal.RequiredContribution(Today, Frequency.Monthly));
    }

    [Fact]
    public void GoalsThatTogetherExceedAvailableMoneyRaiseAConflict()
    {
        var goals = new List<Goal>
        {
            Goal("Car deposit", new Money(3000m), Today.AddYears(1), new Money(100m)),
            Goal("Holiday", new Money(2400m), Today.AddYears(1), new Money(100m)),
            Goal("New laptop", new Money(1200m), Today.AddMonths(6), new Money(100m))
        };

        var conflict = GoalPlanner.FindConflict(goals, new Money(300m), Today, Frequency.Monthly);

        Assert.NotNull(conflict);
        Assert.True(conflict.Shortfall > Money.Zero);
        Assert.Equal(3, conflict.GoalNames.Count);
        Assert.NotEmpty(conflict.SuggestedTradeOffs);
        Assert.Contains("cannot all be met", conflict.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NoConflictIsRaisedWhenThereIsEnoughMoney()
    {
        var goals = new List<Goal>
        {
            Goal("Car deposit", new Money(1200m), Today.AddYears(1), new Money(100m))
        };

        Assert.Null(GoalPlanner.FindConflict(goals, new Money(500m), Today, Frequency.Monthly));
    }

    [Fact]
    public void TheSuggestedTradeOffsIncludeExtendingTheLargestGoal()
    {
        var goals = new List<Goal>
        {
            Goal("Car deposit", new Money(6000m), Today.AddMonths(6), new Money(100m)),
            Goal("Holiday", new Money(600m), Today.AddYears(1), new Money(50m))
        };

        var conflict = GoalPlanner.FindConflict(goals, new Money(400m), Today, Frequency.Monthly);

        Assert.NotNull(conflict);
        Assert.Contains(
            conflict.SuggestedTradeOffs,
            suggestion => suggestion.Contains("Moving", StringComparison.Ordinal));
    }

    [Fact]
    public void RaisingAContributionBringsTheCompletionDateForward()
    {
        var goal = Goal("Car deposit", new Money(3000m), Today.AddYears(2), new Money(50m));

        var effect = GoalPlanner.ModelContributionChange(goal, new Money(150m), Today);

        Assert.True(effect.DaysDifference > 0);
        Assert.Contains("sooner", effect.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LoweringAContributionPushesTheCompletionDateBack()
    {
        var goal = Goal("Car deposit", new Money(3000m), Today.AddYears(2), new Money(150m));

        var effect = GoalPlanner.ModelContributionChange(goal, new Money(50m), Today);

        Assert.True(effect.DaysDifference < 0);
        Assert.Contains("delays", effect.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StoppingContributionsIsReportedPlainly()
    {
        var goal = Goal("Car deposit", new Money(3000m), Today.AddYears(2), new Money(150m));

        var effect = GoalPlanner.ModelContributionChange(goal, Money.Zero, Today);

        Assert.Null(effect.NewCompletion);
        Assert.Contains("never be reached", effect.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GoalsAreReviewedInPriorityOrder()
    {
        var goals = new List<Goal>
        {
            Goal("Low", new Money(100m), Today.AddYears(1), new Money(10m)) with
            {
                Priority = GoalPriority.Low
            },
            Goal("Essential", new Money(100m), Today.AddYears(1), new Money(10m)) with
            {
                Priority = GoalPriority.Essential
            }
        };

        var statuses = GoalPlanner.Review(goals, Today, Frequency.Monthly);

        Assert.Equal("Essential", statuses[0].Goal.Name);
    }

    [Fact]
    public void AnIndividualGoalWithoutAnOwnerIsRefused()
    {
        var goal = Goal("Personal", new Money(100m), Today.AddYears(1), new Money(10m)) with
        {
            Scope = GoalScope.Individual
        };

        Assert.Throws<ArgumentException>(goal.Validate);
    }

    [Fact]
    public void ASinkingFundReportsWhatItStillNeedsEachPeriod()
    {
        var fund = new SavingsFund
        {
            Id = Guid.NewGuid(),
            Name = "Car registration",
            Purpose = FundPurpose.CarRegistration,
            TargetAmount = new Money(120m),
            TargetDate = Today.AddDays(364),
            CurrentBalance = new Money(20m)
        };

        var required = fund.RequiredContribution(Today, Frequency.Monthly);

        // 100 still needed over 364 days, which is a shade under twelve monthly periods. The
        // figure is deliberately based on elapsed days rather than a round twelve, so it never
        // understates what is needed.
        Assert.Equal(new Money(8.36m), required);
        Assert.Equal(new Money(100m), fund.RemainingToTarget);
        Assert.True(fund.IsBehindSchedule(Today));
    }

    [Fact]
    public void AFullyFundedFundIsNotBehindSchedule()
    {
        var fund = new SavingsFund
        {
            Id = Guid.NewGuid(),
            Name = "Deductible",
            Purpose = FundPurpose.InsuranceDeductible,
            TargetAmount = new Money(1000m),
            CurrentBalance = new Money(1000m),
            TargetDate = Today.AddYears(1)
        };

        Assert.True(fund.IsFullyFunded);
        Assert.Equal(Money.Zero, fund.RemainingToTarget);
        Assert.False(fund.IsBehindSchedule(Today));
    }

    [Fact]
    public void SpareMoneyIsAllocatedToTheMostUrgentFundsFirst()
    {
        var funds = new List<SavingsFund>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Urgent registration",
                Purpose = FundPurpose.CarRegistration,
                TargetAmount = new Money(240m),
                TargetDate = Today.AddMonths(2),
                Priority = 8
            },
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Distant holiday",
                Purpose = FundPurpose.Holiday,
                TargetAmount = new Money(2400m),
                TargetDate = Today.AddYears(2),
                Priority = 2
            }
        };

        var plan = SavingsAllocator.Allocate(funds, new Money(150m), Frequency.Monthly, Today);

        Assert.Equal("Urgent registration", plan.Suggestions[0].FundName);
        Assert.True(plan.TotalAllocated <= new Money(150m));
    }

    [Fact]
    public void AShortfallIsStatedPlainlyRatherThanSilentlyUnderfunding()
    {
        var funds = new List<SavingsFund>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Deductible",
                Purpose = FundPurpose.InsuranceDeductible,
                TargetAmount = new Money(3000m),
                TargetDate = Today.AddMonths(3),
                Priority = 9
            }
        };

        var plan = SavingsAllocator.Allocate(funds, new Money(100m), Frequency.Monthly, Today);

        Assert.True(plan.Shortfall > Money.Zero);
        Assert.Contains(
            plan.Warnings,
            warning => warning.Contains("You are short", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MoneyLeftOverIsReportedAsUnallocated()
    {
        var funds = new List<SavingsFund>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Small fund",
                Purpose = FundPurpose.Gifts,
                TargetAmount = new Money(120m),
                TargetDate = Today.AddYears(1),
                Priority = 3
            }
        };

        var plan = SavingsAllocator.Allocate(funds, new Money(500m), Frequency.Monthly, Today);

        Assert.True(plan.Unallocated > Money.Zero);
        Assert.Equal(new Money(500m), (plan.TotalAllocated + plan.Unallocated).Round());
    }

    [Fact]
    public void NegativeAvailableMoneyIsRefused()
    {
        Assert.Throws<ArgumentException>(() => SavingsAllocator.Allocate(
            [],
            new Money(-100m),
            Frequency.Monthly,
            Today));
    }

    [Fact]
    public void TheStandardFundSetAlwaysIncludesAnEmergencyFund()
    {
        var funds = SavingsAllocator.SuggestStandardFunds(
            monthlyEssentialSpending: new Money(2000m),
            emergencyFundTargetMonths: 3m,
            annualCarRegistration: new Money(96m),
            insuranceDeductible: new Money(1000m),
            annualCarMaintenance: new Money(600m));

        var emergency = funds.Single(fund => fund.Purpose == FundPurpose.EmergencyFund);

        Assert.Equal(new Money(6000m), emergency.TargetAmount);
        Assert.True(emergency.IsRevolving);
        Assert.Equal(4, funds.Count);
        Assert.All(funds, fund => fund.Validate());
    }

    [Fact]
    public void UnknownIrregularCostsSimplyProduceNoFund()
    {
        var funds = SavingsAllocator.SuggestStandardFunds(new Money(2000m), 3m);

        Assert.Single(funds);
    }

    private static Goal Goal(string name, Money target, DateOnly date, Money contribution) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        TargetAmount = target,
        TargetDate = date,
        PlannedContribution = contribution,
        ContributionFrequency = Frequency.Monthly
    };
}
