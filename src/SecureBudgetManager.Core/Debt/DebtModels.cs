using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Planning;

namespace SecureBudgetManager.Core.Debt;

public enum DebtKind
{
    CreditCard = 0,
    CarLoan = 1,
    PersonalLoan = 2,
    StudentLoan = 3,
    MedicalDebt = 4,
    Mortgage = 5,
    Other = 6
}

public sealed record DebtAccount
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required DebtKind Kind { get; init; }

    public required Money Balance { get; init; }

    /// <summary>Annual percentage rate as a percentage, for example 22.9.</summary>
    public required decimal AnnualPercentageRate { get; init; }

    public required Money MinimumPayment { get; init; }

    /// <summary>Promotional rate, if one applies until <see cref="PromotionalRateEnds"/>.</summary>
    public decimal? PromotionalRate { get; init; }

    public DateOnly? PromotionalRateEnds { get; init; }

    public Guid? OwnerMemberId { get; init; }

    public int? DueDayOfMonth { get; init; }

    /// <summary>The rate in force on a given date, accounting for a promotional period.</summary>
    public decimal RateOn(DateOnly date) =>
        PromotionalRate is { } promotional && PromotionalRateEnds is { } ends && date < ends
            ? promotional
            : AnnualPercentageRate;

    public Money MonthlyInterestAt(decimal rate) => (Balance * (rate / 100m / 12m)).Round();

    public void Validate()
    {
        if (Id == Guid.Empty)
        {
            throw new ArgumentException("A debt needs an identifier.");
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new ArgumentException("A debt needs a name.");
        }

        if (Balance.IsNegative)
        {
            throw new ArgumentException("A debt balance cannot be negative.");
        }

        if (AnnualPercentageRate < 0m)
        {
            throw new ArgumentException("An APR cannot be negative.");
        }

        if (MinimumPayment.IsNegative)
        {
            throw new ArgumentException("A minimum payment cannot be negative.");
        }

        if (PromotionalRate is not null && PromotionalRateEnds is null)
        {
            throw new ArgumentException("A promotional rate needs an end date.");
        }

        if (DueDayOfMonth is { } day && day is < 1 or > 31)
        {
            throw new ArgumentException("A due day must be between 1 and 31.");
        }
    }
}

public enum PayoffStrategy
{
    /// <summary>Smallest balance first. Slower mathematically but easier to sustain.</summary>
    Snowball = 0,

    /// <summary>Highest interest rate first. Cheapest overall.</summary>
    Avalanche = 1
}

public sealed record PayoffPlan
{
    public required PayoffStrategy Strategy { get; init; }

    public required int MonthsToClear { get; init; }

    public required Money TotalInterest { get; init; }

    public required Money TotalPaid { get; init; }

    /// <summary>
    /// The order debts were actually cleared, not the order they were targeted. A small balance
    /// can finish first on its minimum payment alone even when the strategy is attacking a
    /// larger, more expensive debt.
    /// </summary>
    public required IReadOnlyList<DebtPayoffOrder> Order { get; init; }

    public bool ClearedWithinHorizon { get; init; }
}

public sealed record DebtPayoffOrder(string Name, int ClearedInMonth, Money InterestPaid);

public sealed record PayoffComparison(
    PayoffPlan Snowball,
    PayoffPlan Avalanche,
    Money InterestSavedByAvalanche,
    int MonthsSavedByAvalanche,
    string Recommendation);

/// <summary>
/// Models paying debts down with a fixed total monthly budget, rolling each cleared payment into
/// the next debt.
/// </summary>
public static class PayoffPlanner
{
    private const int MaximumMonths = 600;

    public static PayoffPlan Plan(
        IEnumerable<DebtAccount> debts,
        Money monthlyBudget,
        PayoffStrategy strategy,
        DateOnly startDate)
    {
        ArgumentNullException.ThrowIfNull(debts);

        var working = debts
            .Where(debt => debt.Balance > Money.Zero)
            .Select(debt => new WorkingDebt(debt, debt.Balance))
            .ToList();

        foreach (var entry in working)
        {
            entry.Source.Validate();
        }

        if (working.Count == 0)
        {
            return new PayoffPlan
            {
                Strategy = strategy,
                MonthsToClear = 0,
                TotalInterest = Money.Zero,
                TotalPaid = Money.Zero,
                Order = [],
                ClearedWithinHorizon = true
            };
        }

        var minimumTotal = Money.Sum(working.Select(entry => entry.Source.MinimumPayment));
        if (monthlyBudget < minimumTotal)
        {
            throw new ArgumentException(
                $"A budget of {monthlyBudget.ToDisplayString()} is below the combined minimum payments of " +
                $"{minimumTotal.ToDisplayString()}.",
                nameof(monthlyBudget));
        }

        var totalInterest = Money.Zero;
        var totalPaid = Money.Zero;
        var order = new List<DebtPayoffOrder>();
        var month = 0;

        while (working.Any(entry => entry.Balance > Money.Zero) && month < MaximumMonths)
        {
            month++;
            var date = startDate.AddMonths(month - 1);
            var available = monthlyBudget;

            // Interest first, then minimum payments, then everything spare onto the target debt.
            foreach (var entry in working.Where(entry => entry.Balance > Money.Zero))
            {
                var rate = entry.Source.RateOn(date);
                var interest = (entry.Balance * (rate / 100m / 12m)).Round();
                entry.Balance += interest;
                entry.InterestPaid += interest;
                totalInterest += interest;
            }

            foreach (var entry in working.Where(entry => entry.Balance > Money.Zero))
            {
                var payment = Money.Min(entry.Source.MinimumPayment, entry.Balance);
                payment = Money.Min(payment, available);

                entry.Balance -= payment;
                available -= payment;
                totalPaid += payment;
            }

            var target = SelectTarget(working, strategy, date);
            if (target is not null && available > Money.Zero)
            {
                var extra = Money.Min(available, target.Balance);
                target.Balance -= extra;
                totalPaid += extra;
            }

            foreach (var entry in working.Where(entry => entry.Balance <= Money.Zero && !entry.Recorded))
            {
                entry.Recorded = true;
                order.Add(new DebtPayoffOrder(entry.Source.Name, month, entry.InterestPaid.Round()));
            }
        }

        return new PayoffPlan
        {
            Strategy = strategy,
            MonthsToClear = month,
            TotalInterest = totalInterest.Round(),
            TotalPaid = totalPaid.Round(),
            Order = order,
            ClearedWithinHorizon = working.All(entry => entry.Balance <= Money.Zero)
        };
    }

    public static PayoffComparison Compare(
        IEnumerable<DebtAccount> debts,
        Money monthlyBudget,
        DateOnly startDate)
    {
        var list = debts.ToList();
        var snowball = Plan(list, monthlyBudget, PayoffStrategy.Snowball, startDate);
        var avalanche = Plan(list, monthlyBudget, PayoffStrategy.Avalanche, startDate);

        var interestSaved = (snowball.TotalInterest - avalanche.TotalInterest).Round();
        var monthsSaved = snowball.MonthsToClear - avalanche.MonthsToClear;

        var recommendation = interestSaved <= new Money(50m)
            ? "The two strategies cost almost the same here, so choose whichever you will stick to. " +
              "Snowball clears small balances sooner, which many people find easier to maintain."
            : $"Avalanche saves {interestSaved.ToDisplayString()} in interest" +
              (monthsSaved > 0 ? $" and finishes {monthsSaved} month(s) sooner" : string.Empty) +
              ". Snowball clears individual debts faster, which can be easier to keep going.";

        return new PayoffComparison(snowball, avalanche, interestSaved, monthsSaved, recommendation);
    }

    /// <summary>Debt payments as a percentage of net income.</summary>
    public static decimal DebtToIncomeRatio(IEnumerable<DebtAccount> debts, Money monthlyNetIncome)
    {
        ArgumentNullException.ThrowIfNull(debts);

        if (monthlyNetIncome.IsZero)
        {
            return 0m;
        }

        var payments = Money.Sum(debts.Select(debt => debt.MinimumPayment));
        return Math.Round(payments.Amount / monthlyNetIncome.Amount * 100m, 1);
    }

    /// <summary>
    /// What paying extra each month achieves, in months and interest saved.
    /// </summary>
    public static EarlyPayoffResult ModelExtraPayment(DebtAccount debt, Money extraMonthlyPayment)
    {
        ArgumentNullException.ThrowIfNull(debt);
        debt.Validate();

        if (debt.MinimumPayment.IsZero)
        {
            throw new ArgumentException("A minimum payment is needed to model extra payments.", nameof(debt));
        }

        if (extraMonthlyPayment.IsNegative)
        {
            throw new ArgumentException("An extra payment cannot be negative.", nameof(extraMonthlyPayment));
        }

        var baseline = Amortise(debt, debt.MinimumPayment);
        var accelerated = Amortise(debt, debt.MinimumPayment + extraMonthlyPayment);

        var interestSaved = baseline.Clears
            ? (baseline.Interest - accelerated.Interest).Round()
            : Money.Zero;

        var monthsSaved = baseline.Clears && accelerated.Clears
            ? baseline.Months - accelerated.Months
            : 0;

        var explanation = !baseline.Clears
            ? $"The {debt.MinimumPayment.ToDisplayString()} minimum payment does not cover the " +
              $"{debt.MonthlyInterestAt(debt.AnnualPercentageRate).ToDisplayString()} of monthly interest, " +
              "so the balance would never fall. " +
              (accelerated.Clears
                  ? $"Paying {extraMonthlyPayment.ToDisplayString()} more clears it in " +
                    $"{accelerated.Months} month(s)."
                  : "Even with the extra payment the balance would still not fall.")
            : extraMonthlyPayment.IsZero
                ? $"At the minimum payment this clears in {baseline.Months} month(s) and costs " +
                  $"{baseline.Interest.ToDisplayString()} in interest."
                : $"Paying {extraMonthlyPayment.ToDisplayString()} extra clears this {monthsSaved} month(s) " +
                  $"sooner and saves {interestSaved.ToDisplayString()} in interest.";

        return new EarlyPayoffResult(
            baseline.Months,
            accelerated.Months,
            baseline.Interest,
            accelerated.Interest,
            interestSaved,
            monthsSaved,
            baseline.Clears,
            accelerated.Clears,
            explanation);
    }

    /// <summary>
    /// Runs a fixed payment against a balance until it clears, or reports that it never will.
    /// </summary>
    private static (int Months, Money Interest, bool Clears) Amortise(DebtAccount debt, Money monthlyPayment)
    {
        var monthlyRate = debt.AnnualPercentageRate / 100m / 12m;
        var balance = debt.Balance;
        var interestPaid = Money.Zero;
        var months = 0;

        while (balance > Money.Zero && months < MaximumMonths)
        {
            var interest = (balance * monthlyRate).Round();
            var principal = (monthlyPayment - interest).Round();

            if (principal <= Money.Zero)
            {
                return (months, interestPaid.Round(), false);
            }

            months++;
            interestPaid += interest;
            balance = (balance - principal).Round();
        }

        return (months, interestPaid.Round(), balance <= Money.Zero);
    }

    private static WorkingDebt? SelectTarget(
        List<WorkingDebt> working,
        PayoffStrategy strategy,
        DateOnly date)
    {
        var outstanding = working.Where(entry => entry.Balance > Money.Zero).ToList();

        if (outstanding.Count == 0)
        {
            return null;
        }

        return strategy == PayoffStrategy.Snowball
            ? outstanding.OrderBy(entry => entry.Balance.Amount).First()
            : outstanding
                .OrderByDescending(entry => entry.Source.RateOn(date))
                .ThenBy(entry => entry.Balance.Amount)
                .First();
    }

    private sealed class WorkingDebt(DebtAccount source, Money balance)
    {
        public DebtAccount Source { get; } = source;

        public Money Balance { get; set; } = balance;

        public Money InterestPaid { get; set; } = Money.Zero;

        public bool Recorded { get; set; }
    }
}

public sealed record EarlyPayoffResult(
    int BaselineMonths,
    int AcceleratedMonths,
    Money BaselineInterest,
    Money AcceleratedInterest,
    Money InterestSaved,
    int MonthsSaved,
    bool BaselineClears,
    bool AcceleratedClears,
    string Explanation);
