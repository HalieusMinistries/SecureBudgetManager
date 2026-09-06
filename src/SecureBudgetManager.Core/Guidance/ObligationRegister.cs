using SecureBudgetManager.Core.Debt;
using SecureBudgetManager.Core.Expenses;
using SecureBudgetManager.Core.Household;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Storage;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.Core.Guidance;

public sealed record ObligationRegisterLine
{
    public required Guid Id { get; init; }

    public required ObligationKind Kind { get; init; }

    public required string Name { get; init; }

    public required string Owner { get; init; }

    public required string Classification { get; init; }

    public required Money AmountRequired { get; init; }

    public DateOnly? DueDate { get; init; }

    public required string DueDateText { get; init; }

    public required Money AmountPaid { get; init; }

    public required Money AmountReserved { get; init; }

    public required Money StillRequired { get; init; }

    public required Money RequiredFromNextPaycheque { get; init; }

    public required IReadOnlyList<string> Statuses { get; init; }

    public required string StatusText { get; init; }

    public required bool IsEssential { get; init; }

    public required string AttentionText { get; init; }

    public required string Category { get; init; }

    public required string Priority { get; init; }

    public required string Frequency { get; init; }

    public required int PaydaysRemaining { get; init; }

    public required string Consequence { get; init; }

    public required bool IsUnassigned { get; init; }

    public bool RequiresAttentionBefore(DateOnly? nextIncomeDate)
    {
        if (StillRequired.IsZero)
        {
            return false;
        }

        if (DueDate is null)
        {
            return IsEssential;
        }

        if (nextIncomeDate is { } next && DueDate < next)
        {
            return true;
        }

        return Statuses.Contains("Overdue", StringComparer.Ordinal)
               || Statuses.Contains("Due soon", StringComparer.Ordinal);
    }
}

public sealed record ObligationRegister
{
    public required IReadOnlyList<ObligationRegisterLine> Lines { get; init; }

    public Money TotalRequired => Money.Sum(Lines.Select(line => line.AmountRequired)).Round();

    public Money TotalPaid => Money.Sum(Lines.Select(line => line.AmountPaid)).Round();

    public Money TotalReserved => Money.Sum(Lines.Select(line => line.AmountReserved)).Round();

    public Money TotalStillRequired => Money.Sum(Lines.Select(line => line.StillRequired)).Round();

    public static ObligationRegister Build(BudgetDocument document, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(document);

        var hierarchy = document.Hierarchy;
        var reserves = document.Reserves.ToDictionary(reserve => reserve.ObligationId);
        var paydays = ReservationPlanner.PaydaysBetween(document, today, today.AddYears(1));
        var nextPayday = OperationalPositionCalculator.NextConfirmedIncome(document, today)?.Date;
        var dated = ReservationPlanner.Plan(
            ReservationPlanner.ObligationsFrom(document, hierarchy, today, today.AddYears(1)),
            paydays,
            today,
            nextPayday);

        var lines = new List<ObligationRegisterLine>();
        var seen = new HashSet<Guid>();

        foreach (var planned in dated.Lines)
        {
            var paid = PaidToward(document, planned.Obligation.Id, planned.DueDate, today);
            lines.Add(ToLine(
                document,
                today,
                planned.Obligation.Id,
                planned.Name,
                planned.Obligation.Kind,
                planned.Obligation.IsEssential,
                planned.AmountDue,
                planned.DueDate,
                paid,
                planned.AlreadyReserved,
                planned.RequiredFromThisPaycheque,
                planned.PaydaysRemaining,
                dueDateUnknown: false,
                amountEstimated: false,
                awaitingConfirmation: false));
            seen.Add(planned.Obligation.Id);
        }

        foreach (var expense in document.Expenses.Where(expense =>
                     !expense.IsArchived && !expense.IsPaused && ExpenseCoverage.CreatesHouseholdOutflow(expense)))
        {
            if (seen.Contains(expense.Id))
            {
                continue;
            }

            if (!ReservationPlanner.IsDatedObligation(expense)
                && !expense.DueDateUnknown
                && expense.Variability != ExpenseVariability.Variable)
            {
                continue;
            }

            var reserved = reserves.TryGetValue(expense.Id, out var reserve) ? reserve.Reserved : Money.Zero;
            var paid = PaidToward(document, expense.Id, null, today);
            lines.Add(ToLine(
                document,
                today,
                expense.Id,
                expense.Name,
                ObligationKind.Expense,
                expense.Necessity == ExpenseNecessity.Essential,
                expense.ExpectedAmount,
                expense.DueDateUnknown ? null : expense.AnchorDueDate,
                paid,
                reserved,
                Money.Max(Money.Zero, (expense.ExpectedAmount - paid - reserved).Round()),
                paydaysRemaining: 0,
                expense.DueDateUnknown,
                expense.Variability == ExpenseVariability.Variable,
                !expense.ScheduleConfirmed));
            seen.Add(expense.Id);
        }

        foreach (var debt in document.Debts.Where(debt => !debt.MinimumPayment.IsZero))
        {
            if (seen.Contains(debt.Id))
            {
                continue;
            }

            var reserved = reserves.TryGetValue(debt.Id, out var reserve) ? reserve.Reserved : Money.Zero;
            lines.Add(ToLine(
                document,
                today,
                debt.Id,
                $"{debt.Name} minimum payment",
                ObligationKind.Debt,
                true,
                debt.MinimumPayment,
                NextDebtDue(debt, today),
                Money.Zero,
                reserved,
                Money.Max(Money.Zero, (debt.MinimumPayment - reserved).Round()),
                paydaysRemaining: 0,
                dueDateUnknown: debt.DueDayOfMonth is null,
                amountEstimated: false,
                awaitingConfirmation: false));
        }

        return new ObligationRegister
        {
            Lines = lines
                .OrderBy(line => line.DueDate ?? DateOnly.MaxValue)
                .ThenBy(line => line.Name, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    private static ObligationRegisterLine ToLine(
        BudgetDocument document,
        DateOnly today,
        Guid id,
        string name,
        ObligationKind kind,
        bool essential,
        Money required,
        DateOnly? dueDate,
        Money paid,
        Money reserved,
        Money requiredFromNext,
        int paydaysRemaining,
        bool dueDateUnknown,
        bool amountEstimated,
        bool awaitingConfirmation)
    {
        var still = Money.Max(Money.Zero, (required - paid - reserved).Round());
        var expense = document.Expenses.FirstOrDefault(item => item.Id == id);
        var debt = document.Debts.FirstOrDefault(item => item.Id == id);
        var isUnassigned = expense is not null
            ? BillAssignmentPlanner.IsUnassigned(expense)
            : debt is { OwnerMemberId: null };
        var statuses = new List<string>();
        if (isUnassigned && still > Money.Zero)
        {
            statuses.Add("Unassigned");
        }

        if (paid >= required && required > Money.Zero)
        {
            statuses.Add("Paid");
        }
        else if (paid > Money.Zero && paid < required)
        {
            statuses.Add("Partially paid");
        }

        if (still.IsZero && reserved > Money.Zero && paid < required)
        {
            statuses.Add("Fully reserved");
        }

        if (paid.IsZero && reserved.IsZero && still > Money.Zero)
        {
            statuses.Add("Not funded");
        }

        if (dueDate is { } due && due < today && still > Money.Zero)
        {
            statuses.Add("Overdue");
        }
        else if (dueDate is { } soon && still > Money.Zero && soon <= today.AddDays(7))
        {
            statuses.Add("Due soon");
        }

        if (dueDateUnknown || dueDate is null)
        {
            statuses.Add("Date unknown");
        }

        if (amountEstimated)
        {
            statuses.Add("Amount estimated");
        }

        if (awaitingConfirmation)
        {
            statuses.Add("Awaiting confirmation");
        }

        if (statuses.Count == 0)
        {
            statuses.Add(still.IsZero ? "Paid" : "Not funded");
        }

        var owner = expense is not null
            ? BillAssignmentPlanner.Describe(expense, document)
            : isUnassigned
                ? "Unassigned"
                : OwnerName(document, id, kind);
        var classification = essential ? "Essential" : "Discretionary";
        var dueText = dueDateUnknown || dueDate is null
            ? "Date unknown"
            : dueDate.Value.ToString("yyyy-MM-dd");
        var consequence = still.IsZero
            ? "Nothing further is required."
            : isUnassigned
                ? "Nobody has agreed to pay this yet. It remains a visible household shortfall and is not taken from either person."
                : essential
                    ? "If this is not funded, this essential obligation is the one harmed by further spending."
                    : "If this is not funded, the named discretionary envelope is short.";

        return new ObligationRegisterLine
        {
            Id = id,
            Kind = kind,
            Name = name,
            Owner = owner,
            Classification = classification,
            AmountRequired = required.Round(),
            DueDate = dueDateUnknown ? null : dueDate,
            DueDateText = dueText,
            AmountPaid = paid.Round(),
            AmountReserved = reserved.Round(),
            StillRequired = still,
            RequiredFromNextPaycheque = still.IsZero ? Money.Zero : requiredFromNext.Round(),
            Statuses = statuses,
            StatusText = string.Join(" · ", statuses),
            IsEssential = essential,
            AttentionText = still.IsZero
                ? $"{name}: paid or reserved in full."
                : $"{name}: {still.ToDisplayString()} still required by {dueText}. {owner}.",
            Category = expense?.Category.Name ?? (kind == ObligationKind.Debt ? "Debt payments" : "Other"),
            Priority = essential ? "Essential" : "Discretionary",
            Frequency = expense?.Frequency.ToDisplayName() ?? "Monthly",
            PaydaysRemaining = paydaysRemaining,
            Consequence = consequence,
            IsUnassigned = isUnassigned
        };
    }

    private static Money PaidToward(BudgetDocument document, Guid obligationId, DateOnly? dueDate, DateOnly today)
    {
        var from = dueDate is { } due
            ? new DateOnly(due.Year, due.Month, 1)
            : new DateOnly(today.Year, today.Month, 1);
        var to = dueDate ?? today;

        return Money.Sum(document.Transactions
            .Where(transaction => transaction.ExpenseItemId == obligationId)
            .Where(transaction => transaction.IsConfirmed && !transaction.IsRefund)
            .Where(transaction => transaction.Date >= from && transaction.Date <= to)
            .Select(transaction => transaction.Amount)).Round();
    }

    private static string OwnerName(BudgetDocument document, Guid obligationId, ObligationKind kind)
    {
        if (kind == ObligationKind.Debt)
        {
            var debt = document.Debts.FirstOrDefault(item => item.Id == obligationId);
            return debt?.OwnerMemberId is { } memberId
                ? document.MemberName(memberId)
                : "Shared";
        }

        var expense = document.Expenses.FirstOrDefault(item => item.Id == obligationId);
        if (expense is null)
        {
            return "Shared";
        }

        if (expense.Ownership == Ownership.Shared)
        {
            return "Shared";
        }

        var ownerId = expense.Split?.Participants.FirstOrDefault();
        return ownerId is { } id && id != Guid.Empty
            ? document.MemberName(id)
            : "Individual";
    }

    private static DateOnly? NextDebtDue(DebtAccount debt, DateOnly from)
    {
        if (debt.DueDayOfMonth is not { } day)
        {
            return null;
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
        return new DateOnly(next.Year, next.Month, Math.Min(day, DateTime.DaysInMonth(next.Year, next.Month)));
    }
}
