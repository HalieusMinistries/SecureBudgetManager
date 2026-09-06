using SecureBudgetManager.Core.Expenses;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.Core.CashFlow;

public sealed record SafeToSpendResult
{
    public required Money Amount { get; init; }

    public required Money CurrentBalance { get; init; }

    public required Money CommittedBeforeNextPayday { get; init; }

    public required Money Reserve { get; init; }

    public DateOnly? NextPayday { get; init; }

    public required string Explanation { get; init; }

    public bool IsNegative => Amount.IsNegative;
}

/// <summary>
/// Works out what is genuinely free to spend, rather than what happens to be in the account.
/// </summary>
public static class SafeToSpendCalculator
{
    /// <summary>
    /// Safe-to-spend is the current balance minus every essential commitment due before the next
    /// payday, minus the household's minimum reserve. A large balance the day before rent is due
    /// is not spending money.
    /// </summary>
    public static SafeToSpendResult Calculate(
        CashFlowProjection projection,
        DateOnly today,
        Money reserve,
        bool includeOptionalCommitments = false)
    {
        ArgumentNullException.ThrowIfNull(projection);

        var currentDay = projection.Days.FirstOrDefault(day => day.Date == today);
        var currentBalance = currentDay?.ClosingBalance ?? projection.StartingBalance;

        var nextPayday = FindNextPayday(projection, today);
        var horizon = nextPayday ?? projection.To;

        var committed = Money.Sum(projection.Days
            .Where(day => day.Date > today && day.Date <= horizon)
            .SelectMany(day => day.Events)
            .Where(cashEvent => cashEvent.Direction == CashFlowDirection.Withdrawal)
            .Where(cashEvent => includeOptionalCommitments
                                || cashEvent.Necessity != ExpenseNecessity.Optional)
            .Select(cashEvent => cashEvent.Amount)).Round();

        var safe = (currentBalance - committed - reserve).Round();

        var horizonText = nextPayday is null
            ? "the end of the projection"
            : $"the next payday on {nextPayday:yyyy-MM-dd}";

        var explanation = safe.IsNegative
            ? $"There is not enough to cover {committed.ToDisplayString()} of commitments before {horizonText} " +
              $"while keeping {reserve.ToDisplayString()} in reserve. The shortfall is {safe.Abs().ToDisplayString()}."
            : $"{currentBalance.ToDisplayString()} in the account, less {committed.ToDisplayString()} " +
              $"committed before {horizonText} and {reserve.ToDisplayString()} held in reserve.";

        return new SafeToSpendResult
        {
            Amount = safe,
            CurrentBalance = currentBalance,
            CommittedBeforeNextPayday = committed,
            Reserve = reserve,
            NextPayday = nextPayday,
            Explanation = explanation
        };
    }

    private static DateOnly? FindNextPayday(CashFlowProjection projection, DateOnly today)
    {
        return projection.Days
            .Where(day => day.Date > today)
            .Where(day => day.Events.Any(cashEvent => cashEvent.Direction == CashFlowDirection.Deposit))
            .Select(day => (DateOnly?)day.Date)
            .FirstOrDefault();
    }

    /// <summary>
    /// Bills falling due between today and the next payday, which is the list a household
    /// actually needs before deciding whether it can afford something this week.
    /// </summary>
    public static IReadOnlyList<CashFlowEvent> BillsBeforeNextPayday(
        CashFlowProjection projection,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(projection);

        var nextPayday = FindNextPayday(projection, today) ?? projection.To;

        return projection.Days
            .Where(day => day.Date >= today && day.Date <= nextPayday)
            .SelectMany(day => day.Events)
            .Where(cashEvent => cashEvent.Direction == CashFlowDirection.Withdrawal)
            .OrderBy(cashEvent => cashEvent.Date)
            .ToList();
    }

    /// <summary>
    /// A pay-period budget: what arrives, what is committed, and what is left over.
    /// </summary>
    public static PayPeriodBudget BuildPayPeriodBudget(CashFlowProjection projection, PayPeriod period)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(period);

        var days = projection.Days
            .Where(day => day.Date >= period.Start && day.Date <= period.End)
            .ToList();

        var income = Money.Sum(days.Select(day => day.Deposits)).Round();

        var essential = Money.Sum(days
            .SelectMany(day => day.Events)
            .Where(cashEvent => cashEvent.Direction == CashFlowDirection.Withdrawal)
            .Where(cashEvent => cashEvent.Necessity != ExpenseNecessity.Optional)
            .Select(cashEvent => cashEvent.Amount)).Round();

        var optional = Money.Sum(days
            .SelectMany(day => day.Events)
            .Where(cashEvent => cashEvent.Direction == CashFlowDirection.Withdrawal)
            .Where(cashEvent => cashEvent.Necessity == ExpenseNecessity.Optional)
            .Select(cashEvent => cashEvent.Amount)).Round();

        var carryIn = days.Count == 0 ? projection.StartingBalance : days[0].OpeningBalance;
        var carryOut = days.Count == 0 ? projection.StartingBalance : days[^1].ClosingBalance;

        return new PayPeriodBudget
        {
            Period = period,
            CarriedIn = carryIn,
            Income = income,
            EssentialCommitments = essential,
            OptionalCommitments = optional,
            CarriedOut = carryOut,
            LowestBalance = days.Count == 0
                ? projection.StartingBalance
                : days.Min(day => day.ClosingBalance)
        };
    }
}

public sealed record PayPeriodBudget
{
    public required PayPeriod Period { get; init; }

    public required Money CarriedIn { get; init; }

    public required Money Income { get; init; }

    public required Money EssentialCommitments { get; init; }

    public required Money OptionalCommitments { get; init; }

    public required Money CarriedOut { get; init; }

    public required Money LowestBalance { get; init; }

    public Money TotalCommitments => EssentialCommitments + OptionalCommitments;

    public Money Leftover => (Income - TotalCommitments).Round();

    public bool IncomeCoversEssentials => Income >= EssentialCommitments;
}
