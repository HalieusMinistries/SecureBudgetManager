using SecureBudgetManager.Core.CashFlow;
using SecureBudgetManager.Core.Expenses;
using SecureBudgetManager.Core.Income;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.Tests.CashFlow;

/// <summary>
/// A monthly total can look affordable while the bank balance still goes negative before payday.
/// These tests are about that gap.
/// </summary>
public sealed class CashFlowProjectorTests
{
    private static readonly Guid MemberId = Guid.NewGuid();

    [Fact]
    public void AnEmptyProjectionKeepsTheStartingBalance()
    {
        var projection = Project(new Money(500m), [], []);

        Assert.Equal(new Money(500m), projection.ClosingBalance);
        Assert.Equal(new Money(500m), projection.LowestBalance);
        Assert.False(projection.GoesNegative);
    }

    [Fact]
    public void DepositsAndWithdrawalsLandOnTheirOwnDays()
    {
        var projection = Project(
            new Money(1000m),
            [WeeklyWage(new Money(700m), new DateOnly(2026, 3, 6))],
            [MonthlyBill("Rent", new Money(1200m), new DateOnly(2026, 3, 1))]);

        var rentDay = projection.Days.Single(day => day.Date == new DateOnly(2026, 3, 1));
        var wageDay = projection.Days.Single(day => day.Date == new DateOnly(2026, 3, 6));

        Assert.Equal(new Money(1200m), rentDay.Withdrawals);
        Assert.Equal(new Money(700m), wageDay.Deposits);
        Assert.Equal(new Money(-200m), rentDay.ClosingBalance);
    }

    [Fact]
    public void TheLowestPointIsFoundEvenWhenTheMonthEndsPositive()
    {
        // Rent on the 1st sinks the balance; four weekly wages restore it by month end.
        // The month looks fine in total but the household was overdrawn for five days.
        var projection = Project(
            new Money(1000m),
            [WeeklyWage(new Money(700m), new DateOnly(2026, 3, 6))],
            [MonthlyBill("Rent", new Money(1200m), new DateOnly(2026, 3, 1))]);

        Assert.True(projection.ClosingBalance > Money.Zero);
        Assert.True(projection.GoesNegative);
        Assert.Equal(new DateOnly(2026, 3, 1), projection.FirstNegativeDate);
        Assert.Equal(new Money(-200m), projection.LowestBalance);
    }

    [Fact]
    public void DaysBelowZeroAreListedIndividually()
    {
        var projection = Project(
            new Money(1000m),
            [WeeklyWage(new Money(700m), new DateOnly(2026, 3, 6))],
            [MonthlyBill("Rent", new Money(1200m), new DateOnly(2026, 3, 1))]);

        // Negative from the 1st until the wage arrives on the 6th.
        Assert.Equal(5, projection.DaysBelowZero.Count);
        Assert.All(projection.DaysBelowZero, day => Assert.True(day.Date < new DateOnly(2026, 3, 6)));
    }

    [Fact]
    public void DaysBelowTheMinimumReserveAreReportedSeparatelyFromDaysBelowZero()
    {
        var projection = Project(
            new Money(1000m),
            [WeeklyWage(new Money(700m), new DateOnly(2026, 3, 6))],
            [MonthlyBill("Rent", new Money(900m), new DateOnly(2026, 3, 1))]);

        Assert.False(projection.GoesNegative);
        Assert.NotEmpty(projection.DaysBelowReserve(new Money(400m)));
    }

    [Fact]
    public void AFiveWeekMonthProducesFiveWagePayments()
    {
        var projection = CashFlowProjector.Project(new CashFlowInputs
        {
            From = new DateOnly(2026, 1, 1),
            To = new DateOnly(2026, 1, 31),
            StartingBalance = Money.Zero,
            IncomeSources = [WeeklyWage(new Money(700m), new DateOnly(2026, 1, 2))],
            NetPayPerPeriod = new Dictionary<Guid, Money>(),
            Assumption = IncomeEstimate.Conservative
        });

        Assert.Equal(new Money(3500m), projection.TotalDeposits.Round());
    }

    [Fact]
    public void FortnightlyBillsAgainstWeeklyIncomeStillBalanceOverAYear()
    {
        var projection = CashFlowProjector.Project(new CashFlowInputs
        {
            From = new DateOnly(2026, 1, 5),
            To = new DateOnly(2026, 1, 5).AddDays(363),
            StartingBalance = new Money(2000m),
            IncomeSources = [WeeklyWage(new Money(600m), new DateOnly(2026, 1, 9))],
            Expenses =
            [
                new ExpenseItem
                {
                    Id = Guid.NewGuid(),
                    Name = "Fortnightly childcare",
                    Category = ExpenseCategory.Childcare,
                    ExpectedAmount = new Money(1100m),
                    Frequency = Frequency.Fortnightly,
                    AnchorDueDate = new DateOnly(2026, 1, 6)
                }
            ]
        });

        // 52 weekly deposits against 26 fortnightly bills.
        Assert.Equal(new Money(31_200m), projection.TotalDeposits.Round());
        Assert.Equal(new Money(28_600m), projection.TotalWithdrawals.Round());
        Assert.Equal(new Money(2600m), projection.NetChange);
    }

    [Fact]
    public void TheConservativeAssumptionProjectsLessIncomeThanTheOptimisticOne()
    {
        var hourly = new HourlyIncome
        {
            Id = Guid.NewGuid(),
            Name = "Warehouse",
            MemberId = MemberId,
            PayFrequency = Frequency.Weekly,
            AnchorPayDate = new DateOnly(2026, 3, 6),
            HourlyRate = new Money(18m),
            WeeklyHours = VariableHours.Standard
        };

        var conservative = Project(new Money(500m), [hourly], [], IncomeEstimate.Conservative);
        var optimistic = Project(new Money(500m), [hourly], [], IncomeEstimate.Optimistic);

        // 35 hours against 40: five hours a week is 260 hours a year.
        Assert.True(conservative.ClosingBalance < optimistic.ClosingBalance);
    }

    [Fact]
    public void APausedExpenseGeneratesNoEvents()
    {
        var paused = MonthlyBill("Streaming", new Money(15.99m), new DateOnly(2026, 3, 12)) with
        {
            IsPaused = true
        };

        var projection = Project(new Money(500m), [], [paused]);

        Assert.Equal(Money.Zero, projection.TotalWithdrawals);
    }

    [Fact]
    public void AnExpenseStopsOnItsEndDate()
    {
        var ending = MonthlyBill("Gym", new Money(40m), new DateOnly(2026, 3, 10)) with
        {
            EndsOn = new DateOnly(2026, 5, 1)
        };

        var projection = CashFlowProjector.Project(new CashFlowInputs
        {
            From = new DateOnly(2026, 3, 1),
            To = new DateOnly(2026, 8, 31),
            StartingBalance = new Money(2000m),
            Expenses = [ending]
        });

        // March and April only.
        Assert.Equal(new Money(80m), projection.TotalWithdrawals.Round());
    }

    [Fact]
    public void APriceIncreaseCompoundsOnlyOnWholeYears()
    {
        var rising = new ExpenseItem
        {
            Id = Guid.NewGuid(),
            Name = "Rent",
            Category = ExpenseCategory.Housing,
            ExpectedAmount = new Money(1000m),
            Frequency = Frequency.Monthly,
            AnchorDueDate = new DateOnly(2026, 1, 1),
            AnnualIncreasePercent = 5m
        };

        Assert.Equal(new Money(1000m), rising.AmountOn(new DateOnly(2026, 6, 1)));
        Assert.Equal(new Money(1050m), rising.AmountOn(new DateOnly(2027, 2, 1)));
        Assert.Equal(new Money(1102.5m), rising.AmountOn(new DateOnly(2028, 2, 1)));
    }

    [Fact]
    public void ANetPayOverrideIsPreferredOverTheGrossEstimate()
    {
        var wage = WeeklyWage(new Money(1000m), new DateOnly(2026, 3, 6));

        var projection = CashFlowProjector.Project(new CashFlowInputs
        {
            From = new DateOnly(2026, 3, 1),
            To = new DateOnly(2026, 3, 31),
            StartingBalance = Money.Zero,
            IncomeSources = [wage],
            NetPayPerPeriod = new Dictionary<Guid, Money> { [wage.Id] = new Money(780m) }
        });

        // Four Fridays in March 2026 at the net figure, not the gross one.
        Assert.Equal(new Money(3120m), projection.TotalDeposits.Round());
    }

    [Fact]
    public void AnInactiveIncomeSourceIsIgnored()
    {
        var wage = WeeklyWage(new Money(700m), new DateOnly(2026, 3, 6)) with { IsActive = false };

        var projection = Project(new Money(100m), [wage], []);

        Assert.Equal(Money.Zero, projection.TotalDeposits);
    }

    [Fact]
    public void ConfirmedEventsAppearAlongsideProjectedOnes()
    {
        var projection = CashFlowProjector.Project(new CashFlowInputs
        {
            From = new DateOnly(2026, 3, 1),
            To = new DateOnly(2026, 3, 31),
            StartingBalance = new Money(1000m),
            ConfirmedEvents =
            [
                new CashFlowEvent
                {
                    Date = new DateOnly(2026, 3, 4),
                    Description = "Car repair",
                    Amount = new Money(430m),
                    Direction = CashFlowDirection.Withdrawal,
                    IsConfirmed = true
                }
            ]
        });

        Assert.Equal(new Money(570m), projection.ClosingBalance);
        Assert.True(projection.Days.Single(day => day.Date == new DateOnly(2026, 3, 4)).Events[0].IsConfirmed);
    }

    [Fact]
    public void EachDaysOpeningBalanceIsThePreviousDaysClosing()
    {
        var projection = Project(
            new Money(1000m),
            [WeeklyWage(new Money(700m), new DateOnly(2026, 3, 6))],
            [MonthlyBill("Rent", new Money(1200m), new DateOnly(2026, 3, 1))]);

        for (var i = 1; i < projection.Days.Count; i++)
        {
            Assert.Equal(projection.Days[i - 1].ClosingBalance, projection.Days[i].OpeningBalance);
        }
    }

    [Fact]
    public void AnInvertedDateRangeIsRefused()
    {
        Assert.Throws<ArgumentException>(() => CashFlowProjector.Project(new CashFlowInputs
        {
            From = new DateOnly(2026, 6, 1),
            To = new DateOnly(2026, 3, 1),
            StartingBalance = Money.Zero
        }));
    }

    [Fact]
    public void AnAbsurdlyLongProjectionIsRefused()
    {
        Assert.Throws<ArgumentException>(() => CashFlowProjector.Project(new CashFlowInputs
        {
            From = new DateOnly(2026, 1, 1),
            To = new DateOnly(2060, 1, 1),
            StartingBalance = Money.Zero
        }));
    }

    private static CashFlowProjection Project(
        Money startingBalance,
        IReadOnlyList<IncomeSource> income,
        IReadOnlyList<ExpenseItem> expenses,
        IncomeEstimate assumption = IncomeEstimate.Conservative) =>
        CashFlowProjector.Project(new CashFlowInputs
        {
            From = new DateOnly(2026, 3, 1),
            To = new DateOnly(2026, 3, 31),
            StartingBalance = startingBalance,
            IncomeSources = income,
            Expenses = expenses,
            Assumption = assumption
        });

    private static SalaryIncome WeeklyWage(Money weeklyAmount, DateOnly anchor) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Wages",
        MemberId = MemberId,
        PayFrequency = Frequency.Weekly,
        AnchorPayDate = anchor,
        AnnualSalary = weeklyAmount * 52m
    };

    private static ExpenseItem MonthlyBill(string name, Money amount, DateOnly anchor) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Category = ExpenseCategory.Housing,
        ExpectedAmount = amount,
        Frequency = Frequency.Monthly,
        AnchorDueDate = anchor
    };
}
