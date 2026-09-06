using SecureBudgetManager.Core.Benefits;
using SecureBudgetManager.Core.Budgeting;
using SecureBudgetManager.Core.CashFlow;
using SecureBudgetManager.Core.Expenses;
using SecureBudgetManager.Core.Guidance;
using SecureBudgetManager.Core.Household;
using SecureBudgetManager.Core.Import;
using SecureBudgetManager.Core.Income;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Storage;
using SecureBudgetManager.Core.Tax;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.Tests.Guidance;

public sealed class OperationalReadinessTests
{
    private static readonly DateOnly Today = new(2026, 9, 5);

    [Fact]
    public void SafeToSpendUsesCashOnHandNotExpectedMonthlyIncome()
    {
        var document = Household(Today) with
        {
            Accounts =
            [
                new BankAccount
                {
                    Id = Guid.NewGuid(),
                    Name = "Checking",
                    CurrentBalance = Money.Zero,
                    IsPrimary = true,
                    UpdatedOn = Today
                }
            ]
        };

        var position = OperationalPositionCalculator.Build(document, Today);

        Assert.Equal(Money.Zero, position.AvailableNow);
        Assert.Equal(Money.Zero, position.SafeToSpend);
        Assert.Contains("Available now: $0.00", position.SafetyNotice, StringComparison.Ordinal);
        Assert.Contains("future, not available today", position.ForecastSurplusText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OneTimeIncomeIsNotAveragedIntoRegularMonthlyWages()
    {
        var memberId = Guid.NewGuid();
        var document = new BudgetDocument
        {
            Members = [new HouseholdMember { Id = memberId, Name = "Alex" }],
            IncomeSources =
            [
                new HourlyIncome
                {
                    Id = Guid.NewGuid(),
                    Name = "Warehouse",
                    MemberId = memberId,
                    PayFrequency = Frequency.Weekly,
                    AnchorPayDate = new DateOnly(2026, 9, 11),
                    HourlyRate = new Money(20m),
                    WeeklyHours = VariableHours.Fixed(20m)
                },
                new VariableIncome
                {
                    Id = Guid.NewGuid(),
                    Name = "One-time plasma",
                    MemberId = memberId,
                    PayFrequency = Frequency.OneOff,
                    AnchorPayDate = new DateOnly(2026, 9, 20),
                    Role = IncomeRole.OneTime,
                    AmountPerPeriod = VariableHours.Fixed(80m)
                }
            ]
        };

        var takeHome = TakeHomeCalculator.From(document, IncomeEstimate.Normal, Today);
        var overview = BudgetOverviewCalculator.Build(document, DisplayPeriod.AverageMonthly, Today);

        Assert.Equal(new Money(80m), takeHome.AnnualOneTime);
        Assert.Equal(new Money(20_800m), takeHome.AnnualRegularGross);
        Assert.Equal(new Money(1733.33m), overview.GrossIncome.Amount);
        Assert.Contains(takeHome.Attention, item => item.Message.Contains("one-time", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("not available today", overview.NetCashFlow.Label, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MileageReimbursementDoesNotInflateRegularWages()
    {
        var memberId = Guid.NewGuid();
        var document = new BudgetDocument
        {
            Members = [new HouseholdMember { Id = memberId, Name = "Alex" }],
            IncomeSources =
            [
                new MileageReimbursement
                {
                    Id = Guid.NewGuid(),
                    Name = "Miles",
                    MemberId = memberId,
                    PayFrequency = Frequency.Weekly,
                    AnchorPayDate = Today,
                    IsTaxable = false,
                    Role = IncomeRole.Reimbursement,
                    MilesPerPeriod = 100m,
                    RatePerMile = new Money(0.50m)
                }
            ]
        };

        var overview = BudgetOverviewCalculator.Build(document, DisplayPeriod.Weekly, Today);
        var takeHome = TakeHomeCalculator.From(document, IncomeEstimate.Normal, Today);

        Assert.True(overview.GrossIncome.IsUnavailable);
        Assert.Equal(new Money(2_600m), takeHome.AnnualReimbursements);
        Assert.Equal(Money.Zero, takeHome.AnnualRegularGross);
        Assert.Equal(new Money(50m), overview.TakeHomePay.Amount);
    }

    [Fact]
    public void FirstIncompletePayWeekIsNotProjectedAsAFullWeek()
    {
        var source = new HourlyIncome
        {
            Id = Guid.NewGuid(),
            Name = "New job",
            MemberId = Guid.NewGuid(),
            PayFrequency = Frequency.Weekly,
            AnchorPayDate = new DateOnly(2026, 9, 11),
            StartsOn = new DateOnly(2026, 9, 8),
            HourlyRate = new Money(22m),
            WeeklyHours = VariableHours.Fixed(35m)
        };

        Assert.False(IncomeOccurrence.HasConfirmedPayableAmount(source, new DateOnly(2026, 9, 11), []));

        var projection = CashFlowProjector.Project(new CashFlowInputs
        {
            From = new DateOnly(2026, 9, 5),
            To = new DateOnly(2026, 9, 20),
            StartingBalance = Money.Zero,
            IncomeSources = [source],
            NetPayPerPeriod = new Dictionary<Guid, Money> { [source.Id] = new(770m) }
        });

        Assert.DoesNotContain(projection.Days.SelectMany(day => day.Events), item =>
            item.Date == new DateOnly(2026, 9, 11) && item.Direction == CashFlowDirection.Deposit);
    }

    [Fact]
    public void UnconfirmedBenefitsAreNamedAsExcludedFromTakeHome()
    {
        var memberId = Guid.NewGuid();
        var document = new BudgetDocument
        {
            Members = [new HouseholdMember { Id = memberId, Name = "Alex" }],
            IncomeSources =
            [
                new HourlyIncome
                {
                    Id = Guid.NewGuid(),
                    Name = "Warehouse",
                    MemberId = memberId,
                    PayFrequency = Frequency.Weekly,
                    AnchorPayDate = new DateOnly(2026, 9, 11),
                    HourlyRate = new Money(20m),
                    WeeklyHours = VariableHours.Fixed(35m)
                }
            ],
            PayrollProfiles =
            [
                new PayrollProfile
                {
                    MemberId = memberId,
                    TaxYear = 2025,
                    Retirement = new RetirementPlan { EmployeeContributionPercent = 6m }
                }
            ],
            Benefits =
            [
                new BenefitPlan
                {
                    Id = Guid.NewGuid(),
                    Name = "Medical",
                    Kind = BenefitKind.Medical,
                    MemberId = memberId,
                    EmployeePremiumPerPeriod = new Money(125.00m),
                    PremiumFrequency = Frequency.Weekly,
                    IsConfirmed = false
                }
            ]
        };

        var takeHome = TakeHomeCalculator.From(document, IncomeEstimate.Normal, Today);

        Assert.Contains(takeHome.Attention, item =>
            item.Message.Contains("awaiting effective-date confirmation", StringComparison.OrdinalIgnoreCase)
            && item.Message.Contains("excluded", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PaidBillAndUnknownDateAppearOnTheObligationRegister()
    {
        var expenseId = Guid.NewGuid();
        var document = new BudgetDocument
        {
            Members = [new HouseholdMember { Id = Guid.NewGuid(), Name = "Alex" }],
            Expenses =
            [
                new ExpenseItem
                {
                    Id = expenseId,
                    Name = "Home internet",
                    Category = ExpenseCategory.Utilities,
                    ExpectedAmount = new Money(58m),
                    Frequency = Frequency.Monthly,
                    AnchorDueDate = new DateOnly(2026, 9, 5),
                    Necessity = ExpenseNecessity.Essential,
                    DueDateUnknown = true
                }
            ],
            Transactions =
            [
                new ExpenseTransaction
                {
                    Id = Guid.NewGuid(),
                    ExpenseItemId = expenseId,
                    Date = Today,
                    Description = "September internet",
                    Amount = new Money(58m),
                    Category = ExpenseCategory.Utilities,
                    IsConfirmed = true
                }
            ]
        };

        var register = ObligationRegister.Build(document, Today);
        var line = Assert.Single(register.Lines);

        Assert.Equal(new Money(58m), line.AmountPaid);
        Assert.Equal(Money.Zero, line.StillRequired);
        Assert.Contains("Paid", line.Statuses);
        Assert.Contains("Date unknown", line.Statuses);
        Assert.Equal(register.TotalRequired, (register.TotalPaid + register.TotalReserved + register.TotalStillRequired).Round());
    }

    [Fact]
    public void UnconfirmedExpenseScheduleEmitsOnlyTheStoredDate()
    {
        var expense = new ExpenseItem
        {
            Id = Guid.NewGuid(),
            Name = "Car insurance",
            Category = ExpenseCategory.Insurance,
            ExpectedAmount = new Money(53m),
            Frequency = Frequency.Fortnightly,
            AnchorDueDate = new DateOnly(2026, 9, 12),
            ScheduleConfirmed = false
        };

        var dates = expense.DueDates(new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 31)).ToList();

        Assert.Equal([new DateOnly(2026, 9, 12)], dates);
    }

    [Fact]
    public void ASecondIdenticalMergeDoesNotChangeExistingRecords()
    {
        var incoming = new BudgetDocument
        {
            HouseholdName = "Sample household",
            Members =
            [
                new HouseholdMember { Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), Name = "Alex", IsDiscretionaryEligible = true }
            ],
            IncomeSources =
            [
                new HourlyIncome
                {
                    Id = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
                    Name = "Warehouse",
                    MemberId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                    PayFrequency = Frequency.Weekly,
                    AnchorPayDate = new DateOnly(2026, 9, 11),
                    HourlyRate = new Money(20m),
                    WeeklyHours = VariableHours.Standard
                }
            ]
        };
        var first = HouseholdImportMerger.Merge(new BudgetDocument(), incoming);
        var second = HouseholdImportMerger.Merge(first.Document, incoming);

        Assert.Empty(second.Preview.Created);
        Assert.Empty(second.Preview.Updated);
        Assert.Equal(first.Document.Accounts, second.Document.Accounts);
        Assert.Equal(first.Document.IncomeSources, second.Document.IncomeSources);
        Assert.Equal(first.Document.Expenses, second.Document.Expenses);
    }

    private static BudgetDocument Household(DateOnly today) => new()
    {
        Members = [new HouseholdMember { Id = Guid.NewGuid(), Name = "Alex", IsDiscretionaryEligible = true }],
        Accounts =
        [
            new BankAccount
            {
                Id = Guid.NewGuid(),
                Name = "Checking",
                CurrentBalance = new Money(800m),
                IsPrimary = true,
                UpdatedOn = today
            }
        ],
        IncomeSources =
        [
            new HourlyIncome
            {
                Id = Guid.NewGuid(),
                Name = "Warehouse",
                MemberId = Guid.NewGuid(),
                PayFrequency = Frequency.Weekly,
                AnchorPayDate = today.AddDays(6),
                HourlyRate = new Money(20m),
                WeeklyHours = VariableHours.Fixed(35m)
            }
        ]
    };
}

file static class OperationalPositionTestExtensions
{
    public static string ForecastSurplusText(this OperationalPosition position) =>
        $"Forecast monthly surplus {position.ForecastMonthlySurplus.ToDisplayString()} — future, not available today.";
}
