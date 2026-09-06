using SecureBudgetManager.Core.Debt;
using SecureBudgetManager.Core.Expenses;
using SecureBudgetManager.Core.Guidance;
using SecureBudgetManager.Core.Household;
using SecureBudgetManager.Core.Income;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Savings;
using SecureBudgetManager.Core.Storage;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.Tests.Guidance;

public sealed class ReservationAndAllocationTests
{
    private static readonly DateOnly Today = new(2026, 3, 6);

    [Fact]
    public void OneRemainingPaydayReservesTheWholeRemainder()
    {
        var remaining = ReservationPlanner.DividePerPayday(new Money(90m), 1);

        Assert.Equal(new Money(90m), remaining);
    }

    [Fact]
    public void NoRemainingPaydayMarksTheObligationDueOrOverdue()
    {
        var plan = ReservationPlanner.Plan(
            [
                new PlannedObligation
                {
                    Id = Guid.NewGuid(),
                    Name = "Rent",
                    Kind = ObligationKind.Expense,
                    AmountDue = new Money(900m),
                    DueDate = Today,
                    Tier = NeedTier.Survival
                },
                new PlannedObligation
                {
                    Id = Guid.NewGuid(),
                    Name = "Insurance",
                    Kind = ObligationKind.Expense,
                    AmountDue = new Money(80m),
                    DueDate = Today.AddDays(-2),
                    Tier = NeedTier.Safety
                }
            ],
            [Today.AddDays(7)],
            Today,
            Today.AddDays(7));

        var rent = plan.Lines.Single(line => line.Name == "Rent");
        var insurance = plan.Lines.Single(line => line.Name == "Insurance");

        Assert.Equal(ReservationStatus.DueBeforeNextPayday, rent.Status);
        Assert.Equal(new Money(900m), rent.RequiredFromThisPaycheque);
        Assert.Equal(ReservationStatus.Overdue, insurance.Status);
        Assert.Equal(new Money(80m), insurance.Shortfall);
    }

    [Theory]
    [InlineData(Frequency.Weekly)]
    [InlineData(Frequency.Fortnightly)]
    [InlineData(Frequency.TwiceMonthly)]
    [InlineData(Frequency.Monthly)]
    [InlineData(Frequency.Quarterly)]
    [InlineData(Frequency.SixMonthly)]
    [InlineData(Frequency.Annual)]
    public void DatedExpensesProduceReservationsForEachFrequency(Frequency frequency)
    {
        var document = Household(Today);
        var expenseId = Guid.NewGuid();
        document = document with
        {
            Expenses =
            [
                new ExpenseItem
                {
                    Id = expenseId,
                    Name = $"{frequency} bill",
                    Category = ExpenseCategory.Utilities,
                    ExpectedAmount = new Money(90m),
                    Frequency = frequency,
                    AnchorDueDate = Today.AddDays(21),
                    Necessity = ExpenseNecessity.Essential
                }
            ]
        };

        var paydays = ReservationPlanner.PaydaysBetween(document, Today, Today.AddYears(1));
        var obligations = ReservationPlanner.ObligationsFrom(
            document,
            NeedsHierarchy.Default,
            Today,
            Today.AddYears(1));
        var plan = ReservationPlanner.Plan(obligations, paydays, Today, paydays.FirstOrDefault());

        Assert.Contains(plan.Lines, line => line.Name.Contains(frequency.ToString(), StringComparison.Ordinal));
        Assert.All(plan.Lines, line => Assert.True(line.RequiredFromThisPaycheque >= Money.Zero));
    }

    [Fact]
    public void FivePaychequeMonthsAreCountedFromTheCalendar()
    {
        var count = PayPeriodCalendar.CountPayDatesInMonth(
            Frequency.Weekly,
            new DateOnly(2026, 1, 2),
            2026,
            1);

        Assert.True(count >= 5);
    }

    [Fact]
    public void ProportionalSharesAddExactlyAndNameTheOddCent()
    {
        var matthew = Guid.NewGuid();
        var mel = Guid.NewGuid();
        var split = AllocationReconciler.Split(
            new Money(100m),
            [
                (matthew, "Alex", new Money(20m)),
                (mel, "Sam", new Money(18m))
            ]);

        Assert.True(split.ReconcilesExactly);
        Assert.Equal(new Money(100m), split.AllocatedTotal);
        Assert.Equal(52.63m, Math.Round(split.Shares[0].Percent, 2));
        Assert.Equal(47.37m, Math.Round(split.Shares[1].Percent, 2));
        Assert.Equal(1, split.Shares.Count(share => share.CarriesRoundingRemainder));
    }

    [Fact]
    public void ZeroUsableIncomeIsNotAssignedAPositiveShare()
    {
        var split = AllocationReconciler.Split(
            new Money(80m),
            [
                (Guid.NewGuid(), "Alex", new Money(40m)),
                (Guid.NewGuid(), "Sam", Money.Zero)
            ]);

        Assert.True(split.ReconcilesExactly);
        Assert.Single(split.Shares);
        Assert.Contains("no usable net income", split.Warnings[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HierarchyNeverSilentlyReducesSurvival()
    {
        Assert.True(NeedTier.Survival.IsProtectedByDefault());
        Assert.True(NeedTier.Safety.IsProtectedByDefault());
        Assert.True(NeedTier.Stability.IsProtectedByDefault());
        Assert.False(NeedTier.DiscretionaryFreedom.IsProtectedByDefault());
        Assert.Equal(NeedTier.Survival, NeedsHierarchy.Default.TierFor(ExpenseCategory.Housing.Name, ExpenseNecessity.Essential));
        Assert.Equal(
            [
                ReducibleBucket.UnallocatedSurplus,
                ReducibleBucket.OptionalDiscretionary,
                ReducibleBucket.SharedEntertainment,
                ReducibleBucket.FlexibleSavings,
                ReducibleBucket.LowerPriorityGoals
            ],
            AllocationRules.DefaultReductionOrder);
    }

    [Fact]
    public void DependantHasNoDiscretionaryEnvelopeAndIsStillInHouseholdNeeds()
    {
        var document = Household(Today);
        var allocation = PaychequeAllocator.Allocate(document, Today);

        Assert.DoesNotContain(allocation.Earners, earner => earner.MemberName == "Kit");
        Assert.DoesNotContain(document.DiscretionaryEligibleMembers, member => member.IsDependant);
        Assert.Equal(1, document.Composition.Children);
        Assert.Equal(2, document.Composition.Adults);
        Assert.True(allocation.EssentialGroceries >= Money.Zero);
        Assert.Equal(2, allocation.Earners.Count);
    }

    [Fact]
    public void UnequalHoursChangeContributionPercentages()
    {
        var even = PaychequeAllocator.Allocate(Household(Today), Today);
        var uneven = PaychequeAllocator.Allocate(Household(Today, matthewHours: 40m, melHours: 20m), Today);

        var evenAlex = even.Earners.Single(earner => earner.MemberName == "Alex");
        var unevenAlex = uneven.Earners.Single(earner => earner.MemberName == "Alex");

        Assert.True(unevenAlex.ContributionPercent > evenAlex.ContributionPercent);
        Assert.True(even.SharedSplit.ReconcilesExactly);
        Assert.True(uneven.SharedSplit.ReconcilesExactly);
    }

    [Fact]
    public void SpendingFromOneEarnerDoesNotReduceTheOtherAllowance()
    {
        var document = Household(Today);
        var matthew = document.Members.Single(member => member.Name == "Alex");
        var mel = document.Members.Single(member => member.Name == "Sam");
        var before = PaychequeAllocator.Allocate(document, Today);

        var spent = document with
        {
            Transactions =
            [
                new ExpenseTransaction
                {
                    Id = Guid.NewGuid(),
                    Date = Today,
                    Description = "Alex personal",
                    Amount = new Money(5m),
                    Category = ExpenseCategory.Personal,
                    SpentByMemberId = matthew.Id
                }
            ]
        };

        var after = PaychequeAllocator.Allocate(spent, Today);
        var beforeMel = before.For(mel.Id)!;
        var afterMel = after.For(mel.Id)!;
        var afterAlex = after.For(matthew.Id)!;

        Assert.Equal(beforeMel.DiscretionaryAllowance, afterMel.DiscretionaryAllowance);
        Assert.True(afterAlex.PersonalSpendingUsed >= new Money(5m));
    }

    [Fact]
    public void ExplicitTransferMovesOnlyTheNamedBalances()
    {
        var document = Household(Today);
        var matthew = document.Members.Single(member => member.Name == "Alex");
        var mel = document.Members.Single(member => member.Name == "Sam");
        var before = PaychequeAllocator.Allocate(document, Today);

        var transferred = document with
        {
            Transfers =
            [
                new PersonalTransfer
                {
                    Id = Guid.NewGuid(),
                    FromMemberId = matthew.Id,
                    ToMemberId = mel.Id,
                    Amount = new Money(8m),
                    Date = Today
                }
            ]
        };

        var after = PaychequeAllocator.Allocate(transferred, Today);
        Assert.Equal(new Money(8m), after.For(matthew.Id)!.TransfersSent);
        Assert.Equal(new Money(8m), after.For(mel.Id)!.TransfersReceived);
        Assert.Equal(
            (before.For(matthew.Id)!.RemainingPersonalBalance - new Money(8m)).Round(),
            after.For(matthew.Id)!.RemainingPersonalBalance);
        Assert.Equal(
            (before.For(mel.Id)!.RemainingPersonalBalance + new Money(8m)).Round(),
            after.For(mel.Id)!.RemainingPersonalBalance);
    }

    [Fact]
    public void ConservativeIncomeProtectsEssentialsAndOptimisticDoesNotSilentlyFundThem()
    {
        var conservative = PaychequeAllocator.Allocate(
            Household(Today) with { Rules = new AllocationRules { Basis = IncomeBasis.Conservative } },
            Today);
        var optimistic = PaychequeAllocator.Allocate(
            Household(Today) with
            {
                Rules = new AllocationRules
                {
                    Basis = IncomeBasis.Optimistic,
                    OptimisticIncomeMayFundEssentials = false
                }
            },
            Today);

        Assert.Equal(IncomeBasis.Conservative, conservative.Basis);
        Assert.True(optimistic.AdditionalIncomeAboveBaseline >= Money.Zero);
        Assert.True(optimistic.ReliableIncome <= optimistic.IncomeExpectedBeforeNextPayday + optimistic.IncomeReceived);
        Assert.Contains(optimistic.Warnings, warning => warning.Contains("optimistic", StringComparison.OrdinalIgnoreCase)
                                                       || warning.Contains("conservative", StringComparison.OrdinalIgnoreCase)
                                                       || optimistic.AdditionalIncomeAboveBaseline >= Money.Zero);
    }

    [Fact]
    public void SafeToSpendExcludesProtectedMoneyAndAGenuineDeficitStaysVisible()
    {
        var tight = Household(Today) with
        {
            Accounts =
            [
                new BankAccount
                {
                    Id = Guid.NewGuid(),
                    Name = "Checking",
                    CurrentBalance = new Money(20m),
                    IsPrimary = true
                }
            ],
            Expenses =
            [
                new ExpenseItem
                {
                    Id = Guid.NewGuid(),
                    Name = "Rent",
                    Category = ExpenseCategory.Housing,
                    ExpectedAmount = new Money(2000m),
                    Frequency = Frequency.Monthly,
                    AnchorDueDate = Today.AddDays(2),
                    Necessity = ExpenseNecessity.Essential
                }
            ]
        };

        var allocation = PaychequeAllocator.Allocate(tight, Today);

        Assert.True(allocation.MustNotSpend > allocation.SafeToSpend || allocation.HasShortfall || allocation.SafeToSpend.IsNegative);
        Assert.True(allocation.Ledger.ReconcilesExactly);
        Assert.DoesNotContain(allocation.Protections, item => item.Amount.IsNegative);
        if (allocation.HasShortfall)
        {
            Assert.False(string.IsNullOrWhiteSpace(allocation.Shortfall!.Explanation));
        }
    }

    [Fact]
    public void ClassifyUsesTheFiveSafetyRatings()
    {
        var allocation = PaychequeAllocator.Allocate(Household(Today), Today);
        var (safe, _) = PaychequeAllocator.Classify(allocation, Money.Zero);
        Assert.True(safe is SpendingSafety.Safe or SpendingSafety.Caution or SpendingSafety.InsufficientInformation);

        var (unsafeSpend, explanation) = PaychequeAllocator.Classify(allocation, allocation.MustNotSpend + new Money(5000m));
        Assert.True(unsafeSpend is SpendingSafety.Unsafe or SpendingSafety.NotRecommended or SpendingSafety.InsufficientInformation);
        Assert.False(string.IsNullOrWhiteSpace(explanation));
    }

    [Fact]
    public void ReimbursementsAreNotUnrestrictedIncome()
    {
        var document = Household(Today);
        var matthew = document.Members.Single(member => member.Name == "Alex");
        var mileageId = Guid.NewGuid();
        document = document with
        {
            IncomeSources = document.IncomeSources.Concat(
            [
                new MileageReimbursement
                {
                    Id = mileageId,
                    Name = "Mileage",
                    MemberId = matthew.Id,
                    PayFrequency = Frequency.Weekly,
                    AnchorPayDate = Today,
                    MilesPerPeriod = 100m,
                    RatePerMile = new Money(0.67m),
                    IsTaxable = false
                }
            ]).ToList(),
            ReimbursementOffsets =
            [
                new ReimbursementOffset
                {
                    Id = Guid.NewGuid(),
                    IncomeSourceId = mileageId,
                    OffsetPerPeriod = new Money(67m)
                }
            ]
        };

        var incomes = EarnerIncomeCalculator.ForHousehold(document, IncomeBasis.Conservative, Frequency.Weekly, Today);
        var matthewIncome = incomes.Single(income => income.MemberName == "Alex");
        Assert.True(matthewIncome.ReimbursementsOffsettingCosts > Money.Zero);
        Assert.True(matthewIncome.UsableNetIncome < matthewIncome.NetPay + matthewIncome.Reimbursements
                    || matthewIncome.ReimbursementsOffsettingCosts >= new Money(67m));
    }

    [Fact]
    public void SkippingARequiredSavingLeavesAVisibleEffect()
    {
        var document = Household(Today);
        var fundId = Guid.NewGuid();
        document = document with
        {
            Funds =
            [
                new SavingsFund
                {
                    Id = fundId,
                    Name = "Car repairs",
                    Purpose = FundPurpose.CarRepairs,
                    CurrentBalance = Money.Zero,
                    TargetAmount = new Money(600m),
                    TargetDate = Today.AddDays(14),
                    PlannedContribution = new Money(50m),
                    ContributionFrequency = Frequency.Weekly,
                    Priority = 8
                }
            ]
        };

        var withSaving = PaychequeAllocator.Allocate(document, Today);
        var skipped = PaychequeAllocator.Allocate(document with { Funds = [] }, Today);

        Assert.True(withSaving.RequiredSinkingFunds >= skipped.RequiredSinkingFunds);
    }

    private static BudgetDocument Household(
        DateOnly today,
        decimal matthewHours = 37.5m,
        decimal melHours = 37.5m)
    {
        var matthewId = Guid.NewGuid();
        var melId = Guid.NewGuid();
        var pjId = Guid.NewGuid();

        return new BudgetDocument
        {
            HouseholdName = "Test household",
            Members =
            [
                new HouseholdMember { Id = matthewId, Name = "Alex", IsDiscretionaryEligible = true },
                new HouseholdMember { Id = melId, Name = "Sam", IsDiscretionaryEligible = true },
                new HouseholdMember { Id = pjId, Name = "Kit", IsDependant = true }
            ],
            Accounts =
            [
                new BankAccount
                {
                    Id = Guid.NewGuid(),
                    Name = "Checking",
                    CurrentBalance = new Money(800m),
                    IsPrimary = true
                }
            ],
            IncomeSources =
            [
                new HourlyIncome
                {
                    Id = Guid.NewGuid(),
                    Name = "Alex work",
                    MemberId = matthewId,
                    PayFrequency = Frequency.Weekly,
                    AnchorPayDate = today,
                    HourlyRate = new Money(20m),
                    WeeklyHours = VariableHours.Fixed(matthewHours)
                },
                new HourlyIncome
                {
                    Id = Guid.NewGuid(),
                    Name = "Sam work",
                    MemberId = melId,
                    PayFrequency = Frequency.Weekly,
                    AnchorPayDate = today,
                    HourlyRate = new Money(18m),
                    WeeklyHours = VariableHours.Fixed(melHours)
                }
            ],
            Expenses =
            [
                new ExpenseItem
                {
                    Id = Guid.NewGuid(),
                    Name = "Rent",
                    Category = ExpenseCategory.Housing,
                    ExpectedAmount = new Money(900m),
                    Frequency = Frequency.Monthly,
                    AnchorDueDate = today.AddDays(20),
                    Necessity = ExpenseNecessity.Essential
                }
            ],
            Debts =
            [
                new DebtAccount
                {
                    Id = Guid.NewGuid(),
                    Name = "Card",
                    Kind = DebtKind.CreditCard,
                    Balance = new Money(200m),
                    AnnualPercentageRate = 20m,
                    MinimumPayment = new Money(25m)
                }
            ],
            GroceryPlans =
            [
                new GroceryPlan
                {
                    Id = Guid.NewGuid(),
                    Kind = GroceryPlanKind.Current,
                    Name = "Current",
                    Categories =
                    [
                        new GroceryCategoryPlan
                        {
                            Id = Guid.NewGuid(),
                            Name = "Vegetables",
                            IsEssential = true,
                            WeeklyLimit = new Money(30m)
                        }
                    ]
                }
            ]
        };
    }
}
