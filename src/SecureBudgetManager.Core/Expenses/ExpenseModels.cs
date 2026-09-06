using SecureBudgetManager.Core.Household;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.Core.Expenses;

public enum ExpenseNecessity
{
    /// <summary>Rent, utilities, food, insurance, minimum debt payments.</summary>
    Essential = 0,

    /// <summary>Subscriptions, eating out, hobbies. Reducible under pressure.</summary>
    Optional = 1
}

public enum ExpenseVariability
{
    /// <summary>Same amount every time, such as rent.</summary>
    Fixed = 0,

    /// <summary>Varies, such as groceries or fuel.</summary>
    Variable = 1
}

public sealed record ExpenseCategory(string Name, bool IsBuiltIn)
{
    public static ExpenseCategory Housing { get; } = new("Housing", true);
    public static ExpenseCategory Utilities { get; } = new("Utilities", true);
    public static ExpenseCategory Groceries { get; } = new("Groceries", true);
    public static ExpenseCategory Transport { get; } = new("Transport", true);
    public static ExpenseCategory Insurance { get; } = new("Insurance", true);
    public static ExpenseCategory Healthcare { get; } = new("Healthcare", true);
    public static ExpenseCategory DebtPayments { get; } = new("Debt payments", true);
    public static ExpenseCategory Subscriptions { get; } = new("Subscriptions", true);
    public static ExpenseCategory Childcare { get; } = new("Childcare", true);
    public static ExpenseCategory Personal { get; } = new("Personal", true);
    public static ExpenseCategory Savings { get; } = new("Savings", true);
    public static ExpenseCategory Other { get; } = new("Other", true);

    public static IReadOnlyList<ExpenseCategory> BuiltIn { get; } =
    [
        Housing, Utilities, Groceries, Transport, Insurance, Healthcare,
        DebtPayments, Subscriptions, Childcare, Personal, Savings, Other
    ];

    public static ExpenseCategory Custom(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new ExpenseCategory(name.Trim(), false);
    }
}

/// <summary>
/// A recurring or one-off household cost.
/// </summary>
public sealed record ExpenseItem
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required ExpenseCategory Category { get; init; }

    /// <summary>The planned amount for each occurrence.</summary>
    public required Money ExpectedAmount { get; init; }

    public required Frequency Frequency { get; init; }

    /// <summary>A known due date, used to anchor the schedule.</summary>
    public required DateOnly AnchorDueDate { get; init; }

    public ExpenseNecessity Necessity { get; init; } = ExpenseNecessity.Essential;

    public ExpenseVariability Variability { get; init; } = ExpenseVariability.Fixed;

    public Ownership Ownership { get; init; } = Ownership.Shared;

    /// <summary>Who has agreed to pay. Unassigned bills are not deducted from anyone.</summary>
    public BillAssignment Assignment { get; init; } = BillAssignment.Unassigned;

    /// <summary>Who pays, and in what proportion, once an assignment is made.</summary>
    public SplitRule? Split { get; init; }

    /// <summary>Set when the bill is collected automatically on a different day from the due date.</summary>
    public DateOnly? AutopayAnchorDate { get; init; }

    /// <summary>
    /// Paused bills keep their history and settings but generate no future cash-flow events.
    /// This is how a subscription is suspended without losing its record.
    /// </summary>
    public bool IsPaused { get; init; }

    /// <summary>
    /// The amount and frequency are known, but no due date has been confirmed. The cost still
    /// counts in monthly totals; it does not create calendar transactions.
    /// </summary>
    public bool DueDateUnknown { get; init; }

    /// <summary>
    /// When false, only <see cref="AnchorDueDate"/> is treated as a known due date. Later dates
    /// are not generated until the household confirms the recurring rule.
    /// </summary>
    public bool ScheduleConfirmed { get; init; } = true;

    /// <summary>
    /// Archived expenses keep history but generate no new cash-flow events. Distinct from a
    /// temporary pause.
    /// </summary>
    public bool IsArchived { get; init; }

    public DueDateAdjustment DueDateAdjustment { get; init; } = DueDateAdjustment.None;

    public DateOnly? EndsOn { get; init; }

    public string? Notes { get; init; }

    /// <summary>Annual percentage increase applied when projecting more than a year ahead.</summary>
    public decimal AnnualIncreasePercent { get; init; }

    public Money AnnualCost => FrequencyConverter.ToAnnual(ExpectedAmount, Frequency);

    public Money MonthlyCost => FrequencyConverter.ToMonthly(ExpectedAmount, Frequency);

    /// <summary>The amount due on a given date, including any compounded annual increase.</summary>
    public Money AmountOn(DateOnly date)
    {
        if (AnnualIncreasePercent == 0m)
        {
            return ExpectedAmount;
        }

        var years = (date.DayNumber - AnchorDueDate.DayNumber) / 365.25m;
        if (years <= 0m)
        {
            return ExpectedAmount;
        }

        // Compound only whole years so a bill does not creep up mid-year.
        var wholeYears = (int)Math.Floor(years);
        var multiplier = 1m;
        for (var i = 0; i < wholeYears; i++)
        {
            multiplier *= 1m + (AnnualIncreasePercent / 100m);
        }

        return ExpectedAmount * multiplier;
    }

    public IEnumerable<DateOnly> DueDates(DateOnly from, DateOnly to)
    {
        if (IsPaused || IsArchived || DueDateUnknown)
        {
            return [];
        }

        var effectiveTo = EndsOn is not null && EndsOn < to ? EndsOn.Value : to;
        if (effectiveTo < from)
        {
            return [];
        }

        var anchor = AutopayAnchorDate ?? AnchorDueDate;
        if (!ScheduleConfirmed)
        {
            return anchor >= from && anchor <= effectiveTo
                ? [DueDateAdjustment == DueDateAdjustment.None
                    ? anchor
                    : BusinessDayCalendar.Adjust(anchor, DueDateAdjustment)]
                : [];
        }

        var raw = RecurrenceSchedule.Enumerate(Frequency, anchor, from, effectiveTo);
        return DueDateAdjustment == DueDateAdjustment.None
            ? raw
            : raw.Select(date => BusinessDayCalendar.Adjust(date, DueDateAdjustment)).Distinct();
    }

    public void Validate()
    {
        if (Id == Guid.Empty)
        {
            throw new ArgumentException("An expense needs an identifier.");
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new ArgumentException("An expense needs a name.");
        }

        if (ExpectedAmount.IsNegative)
        {
            throw new ArgumentException("An expense amount cannot be negative. Record refunds separately.");
        }

        if (AnnualIncreasePercent < -100m)
        {
            throw new ArgumentException("An annual increase below -100 percent is not meaningful.");
        }

        if (EndsOn is not null && EndsOn < AnchorDueDate)
        {
            throw new ArgumentException("An expense cannot end before its first due date.");
        }

        if (Ownership == Ownership.Shared && Split is not null)
        {
            Split.Validate();
        }

        Split?.Validate();
    }
}

/// <summary>
/// What actually happened, so expected and actual can be compared.
/// </summary>
public sealed record ExpenseTransaction
{
    public required Guid Id { get; init; }

    public Guid? ExpenseItemId { get; init; }

    public required DateOnly Date { get; init; }

    public required string Description { get; init; }

    public required Money Amount { get; init; }

    public required ExpenseCategory Category { get; init; }

    /// <summary>True for a refund or reimbursement, which offsets spending rather than adding to it.</summary>
    public bool IsRefund { get; init; }

    /// <summary>Set when this row is one line of a split transaction.</summary>
    public Guid? SplitParentId { get; init; }

    public bool IsConfirmed { get; init; } = true;

    /// <summary>
    /// The member whose personal discretionary allowance this spending came out of. Null for
    /// shared household spending. Keeping it on the transaction is what stops one person's
    /// spending from being charged against another person's allowance.
    /// </summary>
    public Guid? SpentByMemberId { get; init; }

    public string? Notes { get; init; }

    /// <summary>Signed effect on spending: refunds count as negative.</summary>
    public Money SignedAmount => IsRefund ? Amount.Negate() : Amount;

    public void Validate()
    {
        if (Id == Guid.Empty)
        {
            throw new ArgumentException("A transaction needs an identifier.");
        }

        if (string.IsNullOrWhiteSpace(Description))
        {
            throw new ArgumentException("A transaction needs a description.");
        }

        if (Amount.IsNegative)
        {
            throw new ArgumentException("Record a refund with IsRefund rather than a negative amount.");
        }
    }
}

public sealed record BudgetVersusActual(
    ExpenseItem Item,
    Money Expected,
    Money Actual,
    Money Difference,
    string Explanation);

public static class ExpenseReconciler
{
    /// <summary>
    /// Compares planned against actual spending for one expense over a date range.
    /// </summary>
    public static BudgetVersusActual Compare(
        ExpenseItem item,
        IEnumerable<ExpenseTransaction> transactions,
        DateOnly from,
        DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(transactions);

        var expected = Money.Sum(item.DueDates(from, to).Select(item.AmountOn)).Round();

        var actual = Money.Sum(transactions
            .Where(transaction => transaction.ExpenseItemId == item.Id)
            .Where(transaction => transaction.Date >= from && transaction.Date <= to)
            .Select(transaction => transaction.SignedAmount)).Round();

        var difference = (actual - expected).Round();

        var explanation = difference.IsZero
            ? $"{item.Name} matched its plan."
            : difference.IsNegative
                ? $"{item.Name} cost {difference.Abs().ToDisplayString()} less than planned."
                : $"{item.Name} cost {difference.ToDisplayString()} more than planned.";

        return new BudgetVersusActual(item, expected, actual, difference, explanation);
    }
}

/// <summary>
/// A pot set aside for a predictable irregular cost, so an annual bill does not arrive as a shock.
/// </summary>
public sealed record SinkingFund
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required Money TargetAmount { get; init; }

    public Money CurrentBalance { get; init; } = Money.Zero;

    public DateOnly? TargetDate { get; init; }

    /// <summary>Set when the fund exists to pay a specific recurring bill.</summary>
    public Guid? LinkedExpenseId { get; init; }

    public Money Shortfall => Money.Max(Money.Zero, TargetAmount - CurrentBalance);

    public decimal ProgressPercent => TargetAmount.IsZero
        ? 100m
        : Math.Round(Math.Min(100m, CurrentBalance.Amount / TargetAmount.Amount * 100m), 1);

    public bool IsFunded => CurrentBalance >= TargetAmount;

    /// <summary>
    /// How much to set aside each week to reach the target by the deadline.
    /// Returns null when there is no deadline.
    /// </summary>
    public Money? RequiredWeeklyContribution(DateOnly today)
    {
        if (TargetDate is null)
        {
            return null;
        }

        var days = TargetDate.Value.DayNumber - today.DayNumber;
        if (days <= 0)
        {
            return Shortfall;
        }

        var weeks = Math.Max(1m, days / 7m);
        return (Shortfall / weeks).Round();
    }

    public void Validate()
    {
        if (Id == Guid.Empty)
        {
            throw new ArgumentException("A sinking fund needs an identifier.");
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new ArgumentException("A sinking fund needs a name.");
        }

        if (TargetAmount.IsNegative || CurrentBalance.IsNegative)
        {
            throw new ArgumentException("Sinking fund amounts cannot be negative.");
        }
    }

    public static IReadOnlyList<string> SuggestedFunds { get; } =
    [
        "Emergency fund",
        "Car repairs",
        "Car registration",
        "Medical deductible",
        "Home repairs",
        "Holidays",
        "Gifts",
        "Clothing",
        "Annual subscriptions",
        "Technology replacement",
        "Insurance deductibles",
        "Planned purchases"
    ];
}
