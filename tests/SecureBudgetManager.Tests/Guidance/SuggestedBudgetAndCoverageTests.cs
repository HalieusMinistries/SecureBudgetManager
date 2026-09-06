using SecureBudgetManager.Core.Debt;
using SecureBudgetManager.Core.Expenses;
using SecureBudgetManager.Core.Guidance;
using SecureBudgetManager.Core.Household;
using SecureBudgetManager.Core.Income;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Storage;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.Tests.Guidance;

public sealed class SuggestedBudgetAndCoverageTests
{
    [Fact]
    public void ConservativeIncomeProtectsEssentialsAndKeepsActualBills()
    {
        var today = new DateOnly(2026, 9, 6);
        var alex = Guid.NewGuid();
        var sam = Guid.NewGuid();
        var rentId = Guid.NewGuid();
        var document = Household(alex, sam, rentId);

        var conservative = SuggestedBudgetPlanner.Propose(document, today, IncomeEstimate.Conservative);
        var normal = SuggestedBudgetPlanner.Propose(document, today, IncomeEstimate.Normal);

        var rent = Assert.Single(conservative.Lines, line => line.Category == "Rent");
        Assert.Equal(SuggestionAmountKind.ActualFixed, rent.AmountKind);
        Assert.Equal(new Money(1450m), rent.Amount);
        Assert.Equal(SuggestionOwnerKind.Household, rent.OwnerKind);
        Assert.Contains("recorded", rent.Why, StringComparison.OrdinalIgnoreCase);
        Assert.True(rent.IsExistingActual);

        Assert.True(normal.People.Sum(person => person.EstimatedIncome.Amount)
                    >= conservative.People.Sum(person => person.EstimatedIncome.Amount));
        Assert.Contains(conservative.Lines, line => line.OwnerName == "Alex");
        Assert.Contains(conservative.Lines, line => line.OwnerName == "Sam");
        Assert.DoesNotContain(conservative.Lines, line => line.OwnerName.Contains("Matthew", StringComparison.Ordinal));
    }

    [Fact]
    public void AcceptAdjustAndExcludeDoNotDuplicateActualBills()
    {
        var today = new DateOnly(2026, 9, 6);
        var alex = Guid.NewGuid();
        var sam = Guid.NewGuid();
        var rentId = Guid.NewGuid();
        var document = Household(alex, sam, rentId);
        var proposal = SuggestedBudgetPlanner.Propose(document, today, IncomeEstimate.Normal);

        var rent = Assert.Single(proposal.Lines, line => line.ExistingExpenseId == rentId);
        var grocery = proposal.Lines.First(line => line.Category == "Grocery budget");
        var dining = proposal.Lines.First(line => line.Category == "Dining out");

        var applied = SuggestedBudgetPlanner.Apply(
            document,
            [
                rent.WithDecision(SuggestionDecision.Accept),
                grocery.WithDecision(SuggestionDecision.Adjust, new Money(95m)),
                dining.WithDecision(SuggestionDecision.Exclude)
            ],
            today);

        Assert.Single(applied.Expenses, expense => expense.Name == "Rent");
        Assert.Equal(new Money(1450m), applied.Expenses.Single(expense => expense.Name == "Rent").ExpectedAmount);
        Assert.Single(applied.Expenses, expense => expense.Name == "Grocery budget");
        Assert.Equal(new Money(95m), applied.Expenses.Single(expense => expense.Name == "Grocery budget").ExpectedAmount);
        Assert.DoesNotContain(applied.Expenses, expense => expense.Name == "Dining out");
    }

    [Fact]
    public void ParkingAndWorkTollsCreateNoHouseholdOutflow()
    {
        var today = new DateOnly(2026, 9, 6);
        var parking = new ExpenseItem
        {
            Id = Guid.NewGuid(),
            Name = "Parking",
            Category = ExpenseCategory.Transport,
            ExpectedAmount = new Money(75m),
            Frequency = Frequency.Monthly,
            AnchorDueDate = today,
            Necessity = ExpenseNecessity.Essential
        };
        var tolls = new ExpenseItem
        {
            Id = Guid.NewGuid(),
            Name = "Tolls",
            Category = ExpenseCategory.Transport,
            ExpectedAmount = new Money(40m),
            Frequency = Frequency.Weekly,
            AnchorDueDate = today,
            Necessity = ExpenseNecessity.Essential
        };
        var personal = new ExpenseItem
        {
            Id = Guid.NewGuid(),
            Name = "Airport parking",
            Category = ExpenseCategory.Transport,
            ExpectedAmount = new Money(18m),
            Frequency = Frequency.OneOff,
            AnchorDueDate = today,
            Coverage = HouseholdCostCoverage.HouseholdPays,
            CoverageConfirmed = true
        };

        Assert.Equal(HouseholdCostCoverage.IncludedInAnotherPayment, ExpenseCoverage.Effective(parking));
        Assert.Equal("Included in rent", ExpenseCoverage.Explanation(parking));
        Assert.True(ExpenseCoverage.HouseholdResponsibility(parking).IsZero);
        Assert.False(ExpenseCoverage.AppearsAsOutstanding(parking));
        Assert.Empty(parking.DueDates(today, today.AddMonths(2)));

        Assert.Equal(HouseholdCostCoverage.EmployerCovered, ExpenseCoverage.Effective(tolls));
        Assert.Equal("Employer-covered", ExpenseCoverage.Explanation(tolls));
        Assert.True(ExpenseCoverage.HouseholdResponsibility(tolls).IsZero);

        Assert.True(ExpenseCoverage.CreatesHouseholdOutflow(personal));
        Assert.Equal(new Money(18m), ExpenseCoverage.HouseholdResponsibility(personal));
    }

    [Fact]
    public void CoveredCostsAreExcludedFromTheObligationRegister()
    {
        var today = new DateOnly(2026, 9, 6);
        var document = new BudgetDocument
        {
            HouseholdName = "Ours",
            Members = [new HouseholdMember { Id = Guid.NewGuid(), Name = "Alex" }],
            Expenses =
            [
                new ExpenseItem
                {
                    Id = Guid.NewGuid(),
                    Name = "Parking",
                    Category = ExpenseCategory.Transport,
                    ExpectedAmount = new Money(75m),
                    Frequency = Frequency.Monthly,
                    AnchorDueDate = today,
                    Necessity = ExpenseNecessity.Essential
                },
                new ExpenseItem
                {
                    Id = Guid.NewGuid(),
                    Name = "Rent",
                    Category = ExpenseCategory.Housing,
                    ExpectedAmount = new Money(1450m),
                    Frequency = Frequency.Monthly,
                    AnchorDueDate = today,
                    Necessity = ExpenseNecessity.Essential
                }
            ]
        };

        var register = ObligationRegister.Build(document, today);
        Assert.DoesNotContain(register.Lines, line => line.Name == "Parking");
        Assert.Contains(register.Lines, line => line.Name == "Rent");
    }

    [Fact]
    public void SetupProgressUsesDependencyOrderAndDoesNotBlockLaterPages()
    {
        var empty = new BudgetDocument();
        var steps = SetupProgress.Steps(empty);
        Assert.Equal(
            [
                SetupStepKind.Household,
                SetupStepKind.Income,
                SetupStepKind.Payroll,
                SetupStepKind.Benefits,
                SetupStepKind.Expenses,
                SetupStepKind.GroceryPlan,
                SetupStepKind.Debt,
                SetupStepKind.Savings,
                SetupStepKind.AllocationRules,
                SetupStepKind.BillsAndReservations
            ],
            steps.Select(step => step.Kind));
        Assert.Equal(SetupStepKind.Household, SetupProgress.NextIncompleteStep(empty));
        Assert.False(SetupProgress.IsMinimumOperational(empty));
    }

    [Fact]
    public void IncompleteInformationReducesSuggestionConfidence()
    {
        var today = new DateOnly(2026, 9, 6);
        var document = new BudgetDocument
        {
            HouseholdName = "Ours",
            Members = [new HouseholdMember { Id = Guid.NewGuid(), Name = "Alex", IsDiscretionaryEligible = true }]
        };

        var proposal = SuggestedBudgetPlanner.Propose(document, today, IncomeEstimate.Conservative);
        Assert.Contains("No account balance", proposal.ConfidenceNote, StringComparison.Ordinal);
        Assert.Contains(proposal.Lines, line => line.Confidence == GuidanceConfidence.Low);
    }

    [Fact]
    public void DebtMinimumsStayAtRecordedAmounts()
    {
        var today = new DateOnly(2026, 9, 6);
        var document = new BudgetDocument
        {
            HouseholdName = "Ours",
            Members = [new HouseholdMember { Id = Guid.NewGuid(), Name = "Alex" }],
            Debts =
            [
                new DebtAccount
                {
                    Id = Guid.NewGuid(),
                    Name = "USCIS",
                    Kind = DebtKind.PersonalLoan,
                    Balance = new Money(2000m),
                    AnnualPercentageRate = 0m,
                    MinimumPayment = new Money(116m)
                }
            ]
        };

        var line = Assert.Single(
            SuggestedBudgetPlanner.Propose(document, today, IncomeEstimate.Conservative).Lines,
            item => item.Category.Contains("USCIS", StringComparison.Ordinal));
        Assert.Equal(new Money(116m), line.Amount);
        Assert.Equal(SuggestionAmountKind.ActualFixed, line.AmountKind);
    }

    private static BudgetDocument Household(Guid alex, Guid sam, Guid rentId) => new()
    {
        HouseholdName = "Ours",
        Members =
        [
            new HouseholdMember { Id = alex, Name = "Alex", IsDiscretionaryEligible = true },
            new HouseholdMember { Id = sam, Name = "Sam", IsDiscretionaryEligible = true }
        ],
        Accounts =
        [
            new BankAccount
            {
                Id = Guid.NewGuid(),
                Name = "Joint",
                CurrentBalance = Money.Zero,
                IsPrimary = true,
                UpdatedOn = new DateOnly(2026, 9, 6)
            }
        ],
        IncomeSources =
        [
            new HourlyIncome
            {
                Id = Guid.NewGuid(),
                MemberId = alex,
                Name = "Warehouse",
                HourlyRate = new Money(22m),
                WeeklyHours = new VariableHours(35m, 40m, 40m),
                PayFrequency = Frequency.Weekly,
                AnchorPayDate = new DateOnly(2026, 9, 4),
                IsActive = true
            },
            new HourlyIncome
            {
                Id = Guid.NewGuid(),
                MemberId = sam,
                Name = "Classroom",
                HourlyRate = new Money(16m),
                WeeklyHours = new VariableHours(35m, 40m, 45m),
                PayFrequency = Frequency.Weekly,
                AnchorPayDate = new DateOnly(2026, 9, 5),
                IsActive = true
            }
        ],
        Expenses =
        [
            new ExpenseItem
            {
                Id = rentId,
                Name = "Rent",
                Category = ExpenseCategory.Housing,
                ExpectedAmount = new Money(1450m),
                Frequency = Frequency.Monthly,
                AnchorDueDate = new DateOnly(2026, 9, 1),
                Necessity = ExpenseNecessity.Essential
            }
        ]
    };
}
