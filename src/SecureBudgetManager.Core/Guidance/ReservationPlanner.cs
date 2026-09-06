using SecureBudgetManager.Core.Debt;
using SecureBudgetManager.Core.Expenses;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Savings;
using SecureBudgetManager.Core.Storage;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.Core.Guidance;

public enum ReservationStatus
{
    /// <summary>Enough is already set aside. Nothing more is needed from this paycheque.</summary>
    FullyFunded = 0,

    /// <summary>More paydays remain, so the cost is being spread across them.</summary>
    OnTrack = 1,

    /// <summary>This is the last paycheque before it is due. The whole remainder must be reserved.</summary>
    FinalPaydayBeforeDue = 2,

    /// <summary>It falls due before the next payday, so it must be paid from money in hand.</summary>
    DueBeforeNextPayday = 3,

    /// <summary>The due date has passed.</summary>
    Overdue = 4,

    /// <summary>No paydays remain before the due date and the money is not there.</summary>
    Underfunded = 5
}

public static class ReservationStatusExtensions
{
    public static string ToDisplayName(this ReservationStatus status) => status switch
    {
        ReservationStatus.FullyFunded => "Fully funded",
        ReservationStatus.OnTrack => "On track",
        ReservationStatus.FinalPaydayBeforeDue => "Last payday before it is due",
        ReservationStatus.DueBeforeNextPayday => "Due before the next payday",
        ReservationStatus.Overdue => "Overdue",
        ReservationStatus.Underfunded => "Underfunded",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown reservation status.")
    };
}

/// <summary>
/// A dated obligation the household has to meet, whatever record it came from. Bills, minimum
/// debt payments and dated savings targets are all reserved the same way, because from the
/// household's point of view they are all money that has to be there on a particular day.
/// </summary>
public sealed record PlannedObligation
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required ObligationKind Kind { get; init; }

    public required Money AmountDue { get; init; }

    public required DateOnly DueDate { get; init; }

    public required NeedTier Tier { get; init; }

    public Money AlreadyReserved { get; init; } = Money.Zero;

    public bool IsEssential { get; init; } = true;

    public string Category { get; init; } = string.Empty;

    public Frequency Frequency { get; init; } = Frequency.OneOff;

    public Money Remaining => Money.Max(Money.Zero, (AmountDue - AlreadyReserved).Round());
}

/// <summary>One obligation, and what this paycheque has to do about it.</summary>
public sealed record ReservationLine
{
    public required PlannedObligation Obligation { get; init; }

    public required int PaydaysRemaining { get; init; }

    public required Money RequiredFromThisPaycheque { get; init; }

    public required Money Shortfall { get; init; }

    public required ReservationStatus Status { get; init; }

    public required string Explanation { get; init; }

    public string Name => Obligation.Name;

    public Money AmountDue => Obligation.AmountDue;

    public Money AlreadyReserved => Obligation.AlreadyReserved;

    public DateOnly DueDate => Obligation.DueDate;

    public NeedTier Tier => Obligation.Tier;

    public bool IsShort => !Shortfall.IsZero;
}

public sealed record ReservationPlan
{
    public required IReadOnlyList<ReservationLine> Lines { get; init; }

    public required DateOnly Today { get; init; }

    public DateOnly? NextPayday { get; init; }

    /// <summary>Bills that must be paid out of money in hand before the next payday.</summary>
    public IReadOnlyList<ReservationLine> DueBeforeNextPayday => Lines
        .Where(line => line.Status is ReservationStatus.DueBeforeNextPayday or ReservationStatus.Overdue)
        .ToList();

    /// <summary>Later bills that need a contribution from this paycheque.</summary>
    public IReadOnlyList<ReservationLine> RequiringContribution => Lines
        .Where(line => line.Status is ReservationStatus.OnTrack or ReservationStatus.FinalPaydayBeforeDue)
        .Where(line => !line.RequiredFromThisPaycheque.IsZero)
        .ToList();

    public IReadOnlyList<ReservationLine> Short => Lines.Where(line => line.IsShort).ToList();

    public Money TotalDueNow => Money.Sum(DueBeforeNextPayday
        .Select(line => line.RequiredFromThisPaycheque)).Round();

    public Money TotalToReserve => Money.Sum(RequiringContribution
        .Select(line => line.RequiredFromThisPaycheque)).Round();

    public Money TotalShortfall => Money.Sum(Lines.Select(line => line.Shortfall)).Round();
}

public static class ReservationPlanner
{
    /// <summary>
    /// Works out what each future obligation needs from the paycheque in hand.
    ///
    /// The contribution is the amount still needed divided by the number of paydays left before
    /// the due date, counting today's payday. That is the difference between reserving a weekly
    /// average and reserving what the bill actually needs: with one payday left before a $90 bill
    /// and nothing set aside, the answer is $90, not a quarter of it.
    /// </summary>
    public static ReservationPlan Plan(
        IEnumerable<PlannedObligation> obligations,
        IReadOnlyList<DateOnly> paydays,
        DateOnly today,
        DateOnly? nextPayday)
    {
        ArgumentNullException.ThrowIfNull(obligations);
        ArgumentNullException.ThrowIfNull(paydays);

        var lines = obligations
            .Select(obligation => Build(obligation, paydays, today, nextPayday))
            .OrderBy(line => line.DueDate)
            .ThenBy(line => line.Tier)
            .ToList();

        return new ReservationPlan
        {
            Lines = lines,
            Today = today,
            NextPayday = nextPayday
        };
    }

    private static ReservationLine Build(
        PlannedObligation obligation,
        IReadOnlyList<DateOnly> paydays,
        DateOnly today,
        DateOnly? nextPayday)
    {
        var remaining = obligation.Remaining;

        if (remaining.IsZero)
        {
            return new ReservationLine
            {
                Obligation = obligation,
                PaydaysRemaining = CountPaydays(paydays, today, obligation.DueDate),
                RequiredFromThisPaycheque = Money.Zero,
                Shortfall = Money.Zero,
                Status = ReservationStatus.FullyFunded,
                Explanation =
                    $"{obligation.Name} of {obligation.AmountDue.ToDisplayString()} is due " +
                    $"{obligation.DueDate:yyyy-MM-dd} and {obligation.AlreadyReserved.ToDisplayString()} " +
                    "is already set aside. Nothing more is needed from this paycheque."
            };
        }

        if (obligation.DueDate < today)
        {
            return new ReservationLine
            {
                Obligation = obligation,
                PaydaysRemaining = 0,
                RequiredFromThisPaycheque = remaining,
                Shortfall = remaining,
                Status = ReservationStatus.Overdue,
                Explanation =
                    $"{obligation.Name} was due on {obligation.DueDate:yyyy-MM-dd} and " +
                    $"{remaining.ToDisplayString()} of it is still unpaid. Pay this now."
            };
        }

        var paydaysRemaining = CountPaydays(paydays, today, obligation.DueDate);
        var dueBeforeNextPayday = obligation.DueDate == today
            || (nextPayday is { } next && obligation.DueDate < next);

        if (dueBeforeNextPayday)
        {
            return new ReservationLine
            {
                Obligation = obligation,
                PaydaysRemaining = paydaysRemaining,
                RequiredFromThisPaycheque = remaining,
                Shortfall = Money.Zero,
                Status = ReservationStatus.DueBeforeNextPayday,
                Explanation =
                    $"{obligation.Name} of {remaining.ToDisplayString()} is due on " +
                    $"{obligation.DueDate:yyyy-MM-dd}, before the next payday. Pay this now, " +
                    "or hold the full amount back from this paycheque."
            };
        }

        if (paydaysRemaining == 0)
        {
            // The bill is still upcoming, but no payday is recorded before it.
            return new ReservationLine
            {
                Obligation = obligation,
                PaydaysRemaining = 0,
                RequiredFromThisPaycheque = remaining,
                Shortfall = remaining,
                Status = ReservationStatus.Underfunded,
                Explanation =
                    $"{obligation.Name} of {obligation.AmountDue.ToDisplayString()} falls due on " +
                    $"{obligation.DueDate:yyyy-MM-dd} and no payday arrives before then. " +
                    $"{remaining.ToDisplayString()} has to come from money already in the account."
            };
        }

        if (paydaysRemaining == 1)
        {
            return new ReservationLine
            {
                Obligation = obligation,
                PaydaysRemaining = 1,
                RequiredFromThisPaycheque = remaining,
                Shortfall = Money.Zero,
                Status = ReservationStatus.FinalPaydayBeforeDue,
                Explanation =
                    $"Put aside {remaining.ToDisplayString()} from this paycheque for " +
                    $"{obligation.Name}, due {obligation.DueDate:yyyy-MM-dd}. This is the last " +
                    "payday before then, so the whole remaining amount is needed now rather than " +
                    "an ordinary weekly share."
            };
        }

        var required = DividePerPayday(remaining, paydaysRemaining);

        return new ReservationLine
        {
            Obligation = obligation,
            PaydaysRemaining = paydaysRemaining,
            RequiredFromThisPaycheque = required,
            Shortfall = Money.Zero,
            Status = ReservationStatus.OnTrack,
            Explanation =
                $"Put aside {required.ToDisplayString()} from this paycheque for {obligation.Name}. " +
                $"{remaining.ToDisplayString()} is still needed by {obligation.DueDate:yyyy-MM-dd} " +
                $"and {paydaysRemaining} paydays fall before then, including this one" +
                (obligation.AlreadyReserved.IsZero
                    ? "."
                    : $", with {obligation.AlreadyReserved.ToDisplayString()} already set aside.")
        };
    }

    /// <summary>
    /// Paydays from today up to and including the due date. Today counts when money arrives today,
    /// because the paycheque in hand is one of the chances to reserve for the bill.
    /// </summary>
    public static int CountPaydays(IReadOnlyList<DateOnly> paydays, DateOnly today, DateOnly dueDate)
    {
        ArgumentNullException.ThrowIfNull(paydays);

        return paydays.Count(payday => payday >= today && payday <= dueDate);
    }

    /// <summary>
    /// Splits what is still needed across the remaining paydays, rounding each contribution up to
    /// the cent. Rounding down would leave the bill a few cents short on the day it matters.
    /// </summary>
    public static Money DividePerPayday(Money remaining, int paydaysRemaining)
    {
        if (paydaysRemaining <= 0)
        {
            return remaining.Round();
        }

        var exact = remaining.Amount / paydaysRemaining;
        var roundedUp = Math.Ceiling(exact * 100m) / 100m;

        return Money.Min(remaining.Round(), new Money(roundedUp));
    }

    /// <summary>
    /// Every payday for the household between two dates, across all active income sources.
    /// Two earners paid on different days both create chances to reserve.
    /// </summary>
    public static IReadOnlyList<DateOnly> PaydaysBetween(BudgetDocument document, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (to < from)
        {
            return [];
        }

        return document.IncomeSources
            .Where(source => source.IsActive)
            .Where(source => source.PayFrequency.IsRecurring())
            .SelectMany(source => source.PayDates(from, to))
            .Distinct()
            .OrderBy(date => date)
            .ToList();
    }

    /// <summary>
    /// Turns the household's records into dated obligations: recurring bills within the horizon,
    /// minimum debt payments, and savings funds that have a deadline to meet.
    ///
    /// Costs that vary rather than falling due — petrol, groceries — are deliberately left out.
    /// They are handled as weekly spending allowances instead, because reserving for a due date
    /// that does not exist would double-count them against the same money.
    /// </summary>
    public static IReadOnlyList<PlannedObligation> ObligationsFrom(
        BudgetDocument document,
        NeedsHierarchy hierarchy,
        DateOnly from,
        DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(hierarchy);

        var reserves = document.Reserves.ToDictionary(reserve => reserve.ObligationId);
        var obligations = new List<PlannedObligation>();

        foreach (var expense in document.Expenses.Where(IsDatedObligation))
        {
            foreach (var dueDate in expense.DueDates(from, to))
            {
                obligations.Add(new PlannedObligation
                {
                    Id = expense.Id,
                    Name = expense.Name,
                    Kind = ObligationKind.Expense,
                    AmountDue = expense.AmountOn(dueDate).Round(),
                    DueDate = dueDate,
                    Tier = hierarchy.TierFor(expense),
                    AlreadyReserved = ReservedFor(reserves, expense.Id),
                    IsEssential = expense.Necessity == ExpenseNecessity.Essential,
                    Category = expense.Category.Name,
                    Frequency = expense.Frequency
                });

                // Only the next occurrence of a recurring bill competes for this paycheque.
                break;
            }
        }

        foreach (var debt in document.Debts.Where(debt => !debt.MinimumPayment.IsZero))
        {
            var dueDate = NextDebtDueDate(debt, from);

            if (dueDate > to)
            {
                continue;
            }

            obligations.Add(new PlannedObligation
            {
                Id = debt.Id,
                Name = $"{debt.Name} minimum payment",
                Kind = ObligationKind.Debt,
                AmountDue = debt.MinimumPayment.Round(),
                DueDate = dueDate,
                Tier = NeedTier.Safety,
                AlreadyReserved = ReservedFor(reserves, debt.Id),
                IsEssential = true,
                Category = ExpenseCategory.DebtPayments.Name,
                Frequency = Frequency.Monthly
            });
        }

        foreach (var fund in document.Funds.Where(fund => fund.TargetDate is not null))
        {
            var target = fund.TargetAmount;

            if (target is not { } amount || fund.TargetDate is not { } targetDate || targetDate > to)
            {
                continue;
            }

            var stillNeeded = Money.Max(Money.Zero, (amount - fund.CurrentBalance).Round());

            if (stillNeeded.IsZero)
            {
                continue;
            }

            obligations.Add(new PlannedObligation
            {
                Id = fund.Id,
                Name = fund.Name,
                Kind = ObligationKind.SavingsFund,
                AmountDue = stillNeeded,
                DueDate = targetDate,
                Tier = fund.Purpose == FundPurpose.EmergencyFund ? NeedTier.Stability : NeedTier.Growth,
                AlreadyReserved = ReservedFor(reserves, fund.Id),
                IsEssential = fund.Purpose == FundPurpose.EmergencyFund,
                Category = GuidanceCategories.SinkingFunds,
                Frequency = fund.ContributionFrequency
            });
        }

        return obligations;
    }

    /// <summary>
    /// True when an expense behaves like a bill: a fixed amount on a known date. Variable costs
    /// and groceries are budgeted as allowances, not reserved against a due date.
    /// </summary>
    public static bool IsDatedObligation(ExpenseItem expense)
    {
        ArgumentNullException.ThrowIfNull(expense);

        if (expense.Variability == ExpenseVariability.Variable)
        {
            return false;
        }

        return !string.Equals(
            expense.Category.Name,
            ExpenseCategory.Groceries.Name,
            StringComparison.OrdinalIgnoreCase);
    }

    private static Money ReservedFor(IReadOnlyDictionary<Guid, ObligationReserve> reserves, Guid id) =>
        reserves.TryGetValue(id, out var reserve) ? reserve.Reserved : Money.Zero;

    private static DateOnly NextDebtDueDate(DebtAccount debt, DateOnly from)
    {
        if (debt.DueDayOfMonth is not { } day)
        {
            // Without a stated due day, treat the payment as falling at the end of the month.
            return new DateOnly(from.Year, from.Month, DateTime.DaysInMonth(from.Year, from.Month));
        }

        var thisMonth = new DateOnly(
            from.Year,
            from.Month,
            Math.Min(day, DateTime.DaysInMonth(from.Year, from.Month)));

        if (thisMonth >= from)
        {
            return thisMonth;
        }

        var next = new DateOnly(from.Year, from.Month, 1).AddMonths(1);

        return new DateOnly(
            next.Year,
            next.Month,
            Math.Min(day, DateTime.DaysInMonth(next.Year, next.Month)));
    }
}
