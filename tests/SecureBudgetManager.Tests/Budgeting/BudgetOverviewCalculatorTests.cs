using SecureBudgetManager.Core.Budgeting;
using SecureBudgetManager.Core.Expenses;
using SecureBudgetManager.Core.Household;
using SecureBudgetManager.Core.Income;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Storage;
using SecureBudgetManager.Core.Tax;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.Tests.Budgeting;

public sealed class BudgetOverviewCalculatorTests
{
    private static readonly DateOnly Today = new(2026, 3, 1);

    [Fact]
    public void LockedWorkspaceShowsNoFigures()
    {
        var overview = BudgetOverviewCalculator.Build(null, DisplayPeriod.Weekly, Today);

        Assert.Equal("Open the household database to see household figures.", overview.GrossIncome.Text);
        Assert.True(overview.GrossIncome.IsUnavailable);
        Assert.Null(overview.GrossIncome.Amount);
    }

    [Fact]
    public void EmptyHouseholdShowsNoRecordsYetNotZero()
    {
        var overview = BudgetOverviewCalculator.Build(new BudgetDocument(), DisplayPeriod.AverageMonthly, Today);

        Assert.Equal("No records yet", overview.GrossIncome.Text);
        Assert.Equal("No records yet", overview.RecurringExpenses.Text);
        Assert.Equal("No records yet", overview.TakeHomePay.Text);
        Assert.DoesNotContain("$0.00", overview.GrossIncome.Text);
        Assert.Contains(overview.Attention, item => item.Message.Contains("member", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AverageMonthlyIsAnnualDividedByTwelveNotFourWeeks()
    {
        var annual = BudgetOverviewCalculator.ToPeriod(new Money(5200m), DisplayPeriod.Annual);
        var monthly = BudgetOverviewCalculator.ToPeriod(new Money(5200m), DisplayPeriod.AverageMonthly);
        var weekly = BudgetOverviewCalculator.ToPeriod(new Money(5200m), DisplayPeriod.Weekly);

        Assert.Equal(new Money(5200m), annual);
        Assert.Equal(new Money(433.33m), monthly);
        Assert.Equal(new Money(100m), weekly);
        Assert.NotEqual(weekly * 4m, monthly);
    }

    [Fact]
    public void WeeklyIncomeShowsConvertedEquivalents()
    {
        var memberId = Guid.NewGuid();
        var document = new BudgetDocument
        {
            Members = [new HouseholdMember { Id = memberId, Name = "Alex" }],
            IncomeSources =
            [
                new SalaryIncome
                {
                    Id = Guid.NewGuid(),
                    Name = "Job",
                    MemberId = memberId,
                    PayFrequency = Frequency.Weekly,
                    AnchorPayDate = Today,
                    AnnualSalary = new Money(52_000m)
                }
            ]
        };

        var weekly = BudgetOverviewCalculator.Build(document, DisplayPeriod.Weekly, Today);
        var monthly = BudgetOverviewCalculator.Build(document, DisplayPeriod.AverageMonthly, Today);
        var annual = BudgetOverviewCalculator.Build(document, DisplayPeriod.Annual, Today);

        Assert.Equal(new Money(1000m), weekly.GrossIncome.Amount);
        Assert.Equal(new Money(4333.33m), monthly.GrossIncome.Amount);
        Assert.Equal(new Money(52_000m), annual.GrossIncome.Amount);
        Assert.Equal("Insufficient information", weekly.EstimatedTaxes.Text);
    }

    [Fact]
    public void TaxesRemainInsufficientUntilPayrollDetailsExist()
    {
        var memberId = Guid.NewGuid();
        var document = HouseholdWithSalary(memberId, includePayroll: false);

        var overview = BudgetOverviewCalculator.Build(document, DisplayPeriod.Annual, Today);

        Assert.False(overview.EstimatedTaxes.Amount.HasValue);
        Assert.Equal("Insufficient information", overview.EstimatedTaxes.Text);
        Assert.Contains(overview.Attention, item => item.Message.Contains("payroll", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PayrollDetailsProduceEstimatedTaxAndDoNotAddEmployerMatchToTakeHome()
    {
        var memberId = Guid.NewGuid();
        var document = HouseholdWithSalary(memberId, includePayroll: true);

        var overview = BudgetOverviewCalculator.Build(document, DisplayPeriod.Annual, Today);
        var takeHome = TakeHomeCalculator.From(document, IncomeEstimate.Normal, Today);

        Assert.True(overview.EstimatedTaxes.IsEstimated);
        Assert.True(overview.EstimatedTaxes.Amount is { } tax && tax.Amount > 0m);
        Assert.True(takeHome.AnnualEmployerMatch.Amount > 0m);
        Assert.True(takeHome.AnnualTakeHome < takeHome.AnnualGross);
        Assert.True(overview.TakeHomePay.Amount < overview.GrossIncome.Amount);
    }

    [Fact]
    public void MileageReimbursementIsGrossIncomeButNotTaxable()
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
                    MilesPerPeriod = 100m,
                    RatePerMile = new Money(0.50m)
                }
            ]
        };

        var overview = BudgetOverviewCalculator.Build(document, DisplayPeriod.Weekly, Today);

        Assert.True(overview.GrossIncome.IsUnavailable);
        Assert.Equal("None — reimbursements recorded separately", overview.GrossIncome.Text);
        Assert.Equal("No records yet", overview.EstimatedTaxes.Text);
        Assert.Equal(new Money(50m), overview.TakeHomePay.Amount);
    }

    [Fact]
    public void RecurringExpenseUsesFrequencyConverter()
    {
        var memberId = Guid.NewGuid();
        var document = new BudgetDocument
        {
            Members = [new HouseholdMember { Id = memberId, Name = "Alex" }],
            Expenses =
            [
                new ExpenseItem
                {
                    Id = Guid.NewGuid(),
                    Name = "Rent",
                    Category = ExpenseCategory.Housing,
                    ExpectedAmount = new Money(1200m),
                    Frequency = Frequency.Monthly,
                    AnchorDueDate = Today,
                    Necessity = ExpenseNecessity.Essential
                }
            ]
        };

        var annual = BudgetOverviewCalculator.Build(document, DisplayPeriod.Annual, Today);
        var weekly = BudgetOverviewCalculator.Build(document, DisplayPeriod.Weekly, Today);

        Assert.Equal(new Money(14_400m), annual.RecurringExpenses.Amount);
        Assert.Equal(FrequencyConverter.FromAnnual(new Money(14_400m), Frequency.Weekly).Round(), weekly.RecurringExpenses.Amount);
    }

    [Fact]
    public void NegativeCashFlowIsFlagged()
    {
        var memberId = Guid.NewGuid();
        var document = new BudgetDocument
        {
            Members = [new HouseholdMember { Id = memberId, Name = "Alex" }],
            IncomeSources =
            [
                new SalaryIncome
                {
                    Id = Guid.NewGuid(),
                    Name = "Job",
                    MemberId = memberId,
                    PayFrequency = Frequency.Monthly,
                    AnchorPayDate = Today,
                    AnnualSalary = new Money(12_000m)
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
                    AnchorDueDate = Today
                }
            ]
        };

        var overview = BudgetOverviewCalculator.Build(document, DisplayPeriod.AverageMonthly, Today);

        Assert.True(overview.NetCashFlow.Amount is { } net && net.IsNegative);
        Assert.Contains(overview.Attention, item => item.Message.Contains("exceeds", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SafeToSpendRequiresARecordedBalance()
    {
        var memberId = Guid.NewGuid();
        var document = HouseholdWithSalary(memberId, includePayroll: false);

        var overview = BudgetOverviewCalculator.Build(document, DisplayPeriod.Weekly, Today);

        Assert.Equal("Insufficient information", overview.SafeToSpend.Text);
        Assert.Equal("Insufficient information", overview.LowestProjectedBalance.Text);
    }

    [Fact]
    public void SafeToSpendUsesTheCashFlowEngineWhenABalanceExists()
    {
        var memberId = Guid.NewGuid();
        var document = HouseholdWithSalary(memberId, includePayroll: false) with
        {
            Accounts =
            [
                new BankAccount
                {
                    Id = Guid.NewGuid(),
                    Name = "Checking",
                    CurrentBalance = new Money(800m),
                    IsPrimary = true,
                    UpdatedOn = Today
                }
            ]
        };

        var overview = BudgetOverviewCalculator.Build(document, DisplayPeriod.Weekly, Today);

        Assert.False(overview.SafeToSpend.IsUnavailable);
        Assert.False(overview.LowestProjectedBalance.IsUnavailable);
        Assert.True(overview.SafeToSpend.IsEstimated);
    }

    [Fact]
    public void VariableHoursAreMarkedEstimated()
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
                    Name = "Shifts",
                    MemberId = memberId,
                    PayFrequency = Frequency.Weekly,
                    AnchorPayDate = Today,
                    HourlyRate = new Money(20m),
                    WeeklyHours = VariableHours.Standard
                }
            ]
        };

        var overview = BudgetOverviewCalculator.Build(document, DisplayPeriod.Weekly, Today);

        Assert.True(overview.GrossIncome.IsEstimated);
    }

    private static BudgetDocument HouseholdWithSalary(Guid memberId, bool includePayroll) => new()
    {
        Members = [new HouseholdMember { Id = memberId, Name = "Alex" }],
        IncomeSources =
        [
            new SalaryIncome
            {
                Id = Guid.NewGuid(),
                Name = "Job",
                MemberId = memberId,
                PayFrequency = Frequency.Weekly,
                AnchorPayDate = Today,
                AnnualSalary = new Money(52_000m)
            }
        ],
        PayrollProfiles = includePayroll
            ?
            [
                new PayrollProfile
                {
                    MemberId = memberId,
                    TaxYear = 2025,
                    Retirement = new RetirementPlan
                    {
                        EmployeeContributionPercent = 5m,
                        EmployerMatchPercent = 4m,
                        EmployerMatchLimitPercent = 5m
                    }
                }
            ]
            : []
    };
}
