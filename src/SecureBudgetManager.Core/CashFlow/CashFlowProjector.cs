using SecureBudgetManager.Core.Expenses;
using SecureBudgetManager.Core.Guidance;
using SecureBudgetManager.Core.Income;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.Core.CashFlow;

public enum CashFlowDirection
{
    Deposit = 0,
    Withdrawal = 1
}

/// <summary>One expected movement of money on a specific date.</summary>
public sealed record CashFlowEvent
{
    public required DateOnly Date { get; init; }

    public required string Description { get; init; }

    public required Money Amount { get; init; }

    public required CashFlowDirection Direction { get; init; }

    public Guid? SourceId { get; init; }

    public ExpenseNecessity? Necessity { get; init; }

    /// <summary>False for projections, true for movements already confirmed by the household.</summary>
    public bool IsConfirmed { get; init; }

    public Money SignedAmount => Direction == CashFlowDirection.Deposit ? Amount : Amount.Negate();
}

/// <summary>The projected balance at the end of one day.</summary>
public sealed record DailyBalance
{
    public required DateOnly Date { get; init; }

    public required Money OpeningBalance { get; init; }

    public required Money ClosingBalance { get; init; }

    public required IReadOnlyList<CashFlowEvent> Events { get; init; }

    public Money Deposits => Money.Sum(
        Events.Where(e => e.Direction == CashFlowDirection.Deposit).Select(e => e.Amount));

    public Money Withdrawals => Money.Sum(
        Events.Where(e => e.Direction == CashFlowDirection.Withdrawal).Select(e => e.Amount));

    public bool IsBelowZero => ClosingBalance.IsNegative;
}

public sealed record CashFlowProjection
{
    public required DateOnly From { get; init; }

    public required DateOnly To { get; init; }

    public required Money StartingBalance { get; init; }

    public required IReadOnlyList<DailyBalance> Days { get; init; }

    public required IncomeEstimate Assumption { get; init; }

    public Money ClosingBalance => Days.Count == 0 ? StartingBalance : Days[^1].ClosingBalance;

    public Money TotalDeposits => Money.Sum(Days.Select(day => day.Deposits));

    public Money TotalWithdrawals => Money.Sum(Days.Select(day => day.Withdrawals));

    public Money NetChange => (ClosingBalance - StartingBalance).Round();

    public DailyBalance? LowestDay => Days.Count == 0
        ? null
        : Days.MinBy(day => day.ClosingBalance.Amount);

    public Money LowestBalance => LowestDay?.ClosingBalance ?? StartingBalance;

    public IReadOnlyList<DailyBalance> DaysBelowZero =>
        Days.Where(day => day.IsBelowZero).ToList();

    public bool GoesNegative => DaysBelowZero.Count > 0;

    public DateOnly? FirstNegativeDate => DaysBelowZero.Count == 0 ? null : DaysBelowZero[0].Date;

    /// <summary>Days on which the balance drops below the household's minimum reserve.</summary>
    public IReadOnlyList<DailyBalance> DaysBelowReserve(Money reserve) =>
        Days.Where(day => day.ClosingBalance < reserve).ToList();
}

public sealed record CashFlowInputs
{
    public required DateOnly From { get; init; }

    public required DateOnly To { get; init; }

    public required Money StartingBalance { get; init; }

    public IReadOnlyList<IncomeSource> IncomeSources { get; init; } = [];

    /// <summary>Net pay per period for each income source, keyed by source id.</summary>
    public IReadOnlyDictionary<Guid, Money> NetPayPerPeriod { get; init; } = new Dictionary<Guid, Money>();

    public IReadOnlyList<ExpenseItem> Expenses { get; init; } = [];

    public IReadOnlyList<Payslip> Payslips { get; init; } = [];

    /// <summary>Movements the household has already confirmed, such as a cleared transaction.</summary>
    public IReadOnlyList<CashFlowEvent> ConfirmedEvents { get; init; } = [];

    public IncomeEstimate Assumption { get; init; } = IncomeEstimate.Conservative;

    public void Validate()
    {
        if (To < From)
        {
            throw new ArgumentException("The projection cannot end before it starts.");
        }

        if (To.DayNumber - From.DayNumber > 366 * 6)
        {
            throw new ArgumentException("Projections are limited to about six years.");
        }
    }
}

/// <summary>
/// Builds a day-by-day balance projection.
///
/// This exists because a monthly total can look comfortable while the account still goes negative
/// before payday. Only a daily projection makes that visible.
/// </summary>
public static class CashFlowProjector
{
    public static CashFlowProjection Project(CashFlowInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        inputs.Validate();

        var events = new List<CashFlowEvent>();
        events.AddRange(BuildIncomeEvents(inputs));
        events.AddRange(BuildExpenseEvents(inputs));
        events.AddRange(inputs.ConfirmedEvents.Where(e => e.Date >= inputs.From && e.Date <= inputs.To));

        var byDate = events
            .GroupBy(e => e.Date)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<CashFlowEvent>)group.ToList());

        var days = new List<DailyBalance>();
        var balance = inputs.StartingBalance;

        for (var date = inputs.From; date <= inputs.To; date = date.AddDays(1))
        {
            var dayEvents = byDate.TryGetValue(date, out var found) ? found : [];
            var opening = balance;

            foreach (var dayEvent in dayEvents)
            {
                balance += dayEvent.SignedAmount;
            }

            balance = balance.Round();

            days.Add(new DailyBalance
            {
                Date = date,
                OpeningBalance = opening,
                ClosingBalance = balance,
                Events = dayEvents
            });
        }

        return new CashFlowProjection
        {
            From = inputs.From,
            To = inputs.To,
            StartingBalance = inputs.StartingBalance,
            Days = days,
            Assumption = inputs.Assumption
        };
    }

    private static IEnumerable<CashFlowEvent> BuildIncomeEvents(CashFlowInputs inputs)
    {
        foreach (var source in inputs.IncomeSources.Where(source => source.IsActive))
        {
            if (!source.PayFrequency.IsRecurring())
            {
                continue;
            }

            // Prefer a supplied net figure; fall back to gross so an incomplete setup still projects.
            var amount = inputs.NetPayPerPeriod.TryGetValue(source.Id, out var net)
                ? net
                : source.GrossPerPeriod(inputs.Assumption);

            if (amount <= Money.Zero)
            {
                continue;
            }

            foreach (var payDate in source.PayDates(inputs.From, inputs.To))
            {
                var slip = inputs.Payslips.FirstOrDefault(item =>
                    item.IncomeSourceId == source.Id && item.PayDate == payDate);
                if (slip is not null)
                {
                    if (slip.NetPay <= Money.Zero)
                    {
                        continue;
                    }

                    yield return new CashFlowEvent
                    {
                        Date = payDate,
                        Description = source.Name,
                        Amount = slip.NetPay.Round(),
                        Direction = CashFlowDirection.Deposit,
                        SourceId = source.Id,
                        IsConfirmed = true
                    };
                    continue;
                }

                if (!IncomeOccurrence.HasConfirmedPayableAmount(source, payDate, inputs.Payslips))
                {
                    continue;
                }

                yield return new CashFlowEvent
                {
                    Date = payDate,
                    Description = source.Name,
                    Amount = amount.Round(),
                    Direction = CashFlowDirection.Deposit,
                    SourceId = source.Id
                };
            }
        }
    }

    private static IEnumerable<CashFlowEvent> BuildExpenseEvents(CashFlowInputs inputs)
    {
        foreach (var expense in inputs.Expenses)
        {
            foreach (var dueDate in expense.DueDates(inputs.From, inputs.To))
            {
                var amount = expense.AmountOn(dueDate);
                if (amount <= Money.Zero)
                {
                    continue;
                }

                yield return new CashFlowEvent
                {
                    Date = dueDate,
                    Description = expense.Name,
                    Amount = amount.Round(),
                    Direction = CashFlowDirection.Withdrawal,
                    SourceId = expense.Id,
                    Necessity = expense.Necessity
                };
            }
        }
    }
}
