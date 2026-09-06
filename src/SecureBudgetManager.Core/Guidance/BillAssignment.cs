using SecureBudgetManager.Core.Expenses;
using SecureBudgetManager.Core.Household;
using SecureBudgetManager.Core.Income;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Storage;

namespace SecureBudgetManager.Core.Guidance;

public sealed record AssignmentEffect
{
    public required string Name { get; init; }

    public required Money Before { get; init; }

    public required Money After { get; init; }

    public Money Change => (After - Before).Round();
}

public sealed record AssignmentPreview
{
    public required string Summary { get; init; }

    public required IReadOnlyList<AssignmentEffect> People { get; init; }

    public required Money UnassignedBefore { get; init; }

    public required Money UnassignedAfter { get; init; }

    public required string Risk { get; init; }
}

public static class BillAssignmentPlanner
{
    public const string CombinedForecastLabel =
        "Combined household forecast — income is not automatically pooled or available to either person.";

    public static bool IsUnassigned(ExpenseItem expense)
    {
        ArgumentNullException.ThrowIfNull(expense);
        return expense.Assignment == BillAssignment.Unassigned;
    }

    public static string Describe(ExpenseItem expense, BudgetDocument document)
    {
        ArgumentNullException.ThrowIfNull(expense);
        ArgumentNullException.ThrowIfNull(document);

        return expense.Assignment switch
        {
            BillAssignment.Unassigned => "Unassigned",
            BillAssignment.MemberPaysAll => $"{PayerName(expense, document)} pays all",
            BillAssignment.PercentageSplit => "Percentage split",
            BillAssignment.FixedDollarSplit => "Fixed dollar split",
            BillAssignment.EnteredContributions => "Separate entered contributions",
            BillAssignment.SharedAccount => "Shared account pays",
            _ => "Unassigned"
        };
    }

    public static IReadOnlyDictionary<Guid, Money> Shares(
        ExpenseItem expense,
        Money amount,
        IReadOnlyDictionary<Guid, Money>? netIncomeByMember = null)
    {
        ArgumentNullException.ThrowIfNull(expense);

        if (expense.Assignment is BillAssignment.Unassigned or BillAssignment.SharedAccount)
        {
            return new Dictionary<Guid, Money>();
        }

        if (expense.Assignment == BillAssignment.MemberPaysAll)
        {
            var payer = PayerId(expense);
            return payer is { } id
                ? new Dictionary<Guid, Money> { [id] = amount.Round() }
                : new Dictionary<Guid, Money>();
        }

        if (expense.Split is { } split && split.Participants.Count > 0)
        {
            return split.Divide(amount, netIncomeByMember);
        }

        return new Dictionary<Guid, Money>();
    }

    public static Money UnassignedRemaining(BudgetDocument document, ObligationRegister register)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(register);

        return Money.Sum(register.Lines
            .Where(line => line.IsUnassigned)
            .Select(line => line.StillRequired)).Round();
    }

    public static AssignmentPreview Preview(
        BudgetDocument document,
        DateOnly today,
        Guid expenseId,
        BillAssignment assignment,
        SplitRule? split)
    {
        ArgumentNullException.ThrowIfNull(document);

        var beforeRegister = ObligationRegister.Build(document, today);
        var beforeAllocation = PaychequeAllocator.Allocate(document, today);
        var beforeUnassigned = UnassignedRemaining(document, beforeRegister);

        var expenses = document.Expenses.Select(expense =>
        {
            if (expense.Id != expenseId)
            {
                return expense;
            }

            var ownership = assignment is BillAssignment.MemberPaysAll
                ? Ownership.Individual
                : Ownership.Shared;

            return expense with
            {
                Assignment = assignment,
                Ownership = ownership,
                Split = split
            };
        }).ToList();

        var afterDocument = document with { Expenses = expenses };
        var afterRegister = ObligationRegister.Build(afterDocument, today);
        var afterAllocation = PaychequeAllocator.Allocate(afterDocument, today);
        var afterUnassigned = UnassignedRemaining(afterDocument, afterRegister);

        var people = document.Members
            .Where(member => !member.IsDependant && !member.IsArchived)
            .Select(member =>
            {
                var before = beforeAllocation.For(member.Id);
                var after = afterAllocation.For(member.Id);
                return new AssignmentEffect
                {
                    Name = member.Name,
                    Before = (before?.ShareOfSharedObligations ?? Money.Zero)
                             + (before?.Income.IndividualObligations ?? Money.Zero),
                    After = (after?.ShareOfSharedObligations ?? Money.Zero)
                            + (after?.Income.IndividualObligations ?? Money.Zero)
                };
            })
            .ToList();

        var expense = document.Expenses.First(item => item.Id == expenseId);
        var still = afterRegister.Lines.FirstOrDefault(line => line.Id == expenseId)?.StillRequired
                    ?? expense.ExpectedAmount;

        return new AssignmentPreview
        {
            Summary =
                $"{expense.Name}: {Describe(expense with { Assignment = assignment, Split = split }, document)}. " +
                $"Still required {still.ToDisplayString()}.",
            People = people,
            UnassignedBefore = beforeUnassigned,
            UnassignedAfter = afterUnassigned,
            Risk = still.IsZero
                ? "Nothing further is required."
                : $"{expense.Name} of {still.ToDisplayString()} remains due. " +
                  "If it is not funded, that obligation is the one harmed."
        };
    }

    public static ExpenseItem Apply(
        ExpenseItem expense,
        BillAssignment assignment,
        SplitRule? split)
    {
        ArgumentNullException.ThrowIfNull(expense);

        return expense with
        {
            Assignment = assignment,
            Ownership = assignment == BillAssignment.MemberPaysAll ? Ownership.Individual : Ownership.Shared,
            Split = split
        };
    }

    public static bool IsUnassignedLine(BudgetDocument document, ObligationRegisterLine line)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(line);

        var expense = document.Expenses.FirstOrDefault(item => item.Id == line.Id);
        return expense is not null && IsUnassigned(expense);
    }

    public static AccountBuckets AccountBalances(BudgetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var personal = new Dictionary<Guid, Money>();
        var household = Money.Zero;

        foreach (var account in document.Accounts)
        {
            if (account.OwnerMemberId is { } owner)
            {
                personal[owner] = personal.TryGetValue(owner, out var current)
                    ? (current + account.CurrentBalance).Round()
                    : account.CurrentBalance.Round();
            }
            else
            {
                household = (household + account.CurrentBalance).Round();
            }
        }

        return new AccountBuckets(household, personal);
    }

    public static string FirstDepositExplanation(IncomeSource source, DateOnly payDate, IReadOnlyList<Payslip> payslips)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(payslips);

        var slip = payslips.FirstOrDefault(item => item.IncomeSourceId == source.Id && item.PayDate == payDate);
        if (slip is not null)
        {
            return $"Actual deposit {slip.NetPay.ToDisplayString()} on {payDate:yyyy-MM-dd}.";
        }

        var weekGross = WeeklyGross(source, IncomeEstimate.Normal);
        if (IncomeOccurrence.OpeningPeriodIsShorterThanAFullPayPeriod(source, payDate)
            || !IncomeOccurrence.HasConfirmedPayableAmount(source, payDate, payslips))
        {
            return
                $"Expected earnings for the working week: {weekGross.ToDisplayString()} gross. " +
                "Expected first deposit: estimated pending payroll-period confirmation. " +
                "Actual first deposit: awaiting payslip.";
        }

        return $"Next expected deposit around {weekGross.ToDisplayString()} gross, pending the payslip.";
    }

    public static Money WeeklyGross(IncomeSource source, IncomeEstimate estimate)
    {
        ArgumentNullException.ThrowIfNull(source);

        return source is HourlyIncome hourly
            ? (hourly.HourlyRate * hourly.WeeklyHours.For(estimate)).Round()
            : source.GrossPerPeriod(estimate).Round();
    }

    private static Guid? PayerId(ExpenseItem expense) =>
        expense.Split?.Participants.FirstOrDefault();

    private static string PayerName(ExpenseItem expense, BudgetDocument document)
    {
        var id = PayerId(expense);
        return id is { } payer && payer != Guid.Empty
            ? document.MemberName(payer)
            : "Named payer";
    }
}

public sealed record AccountBuckets(
    Money Household,
    IReadOnlyDictionary<Guid, Money> Personal);
