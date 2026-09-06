using SecureBudgetManager.Core.Budgeting;
using SecureBudgetManager.Core.Income;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Storage;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.Core.Guidance;

/// <summary>
/// Day-to-day money position. Safe-to-spend is taken from cash already recorded, never from a
/// projected monthly surplus or from a paycheque that has not yet arrived.
/// </summary>
public sealed record OperationalPosition
{
    public required DateOnly Today { get; init; }

    public required Money AvailableNow { get; init; }

    public required Money Reserved { get; init; }

    public required Money SafeToSpend { get; init; }

    public required Money EssentialRequired { get; init; }

    public required Money EssentialFunded { get; init; }

    public required Money EssentialShortfall { get; init; }

    public required Money BillsDueBeforeNextIncome { get; init; }

    public required Money ForecastMonthlySurplus { get; init; }

    public DateOnly? NextConfirmedIncomeDate { get; init; }

    public Money? NextConfirmedIncomeAmount { get; init; }

    public required string NextIncomeText { get; init; }

    public required string SafetyNotice { get; init; }

    public required string HarmedObligation { get; init; }

    public required IReadOnlyList<string> BillsRequiringAttention { get; init; }

    public required IReadOnlyList<string> DeductionNotes { get; init; }

    public bool FurtherSpendingIsUnsafe => AvailableNow.IsZero || SafeToSpend.IsZero;

    public bool ForecastIsNotSpendable => true;
}

public static class OperationalPositionCalculator
{
    public static OperationalPosition Build(BudgetDocument document, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(document);

        var allocation = PaychequeAllocator.Allocate(document, today);
        var register = ObligationRegister.Build(document, today);
        var overview = BudgetOverviewCalculator.Build(document, DisplayPeriod.AverageMonthly, today);
        var takeHome = TakeHomeCalculator.From(document, IncomeEstimate.Normal, today);

        var available = document.TotalBalance.Round();
        var reserved = Money.Sum(document.Reserves.Select(reserve => reserve.Reserved)).Round();

        var heldFromCash = (
            reserved
            + allocation.BillsDueBeforeNextPayday
            + allocation.EssentialGroceries
            + allocation.EssentialTransport
            + allocation.OtherEssentialSpending
            + allocation.MinimumDebtPayments
            + allocation.RequiredSinkingFunds
            + allocation.SafetyBuffer).Round();

        var required = Money.Max(heldFromCash, allocation.Essentials.Required).Round();
        var funded = Money.Min(required, available).Round();
        var shortfall = Money.Max(Money.Zero, (required - funded).Round());
        var safe = shortfall.IsZero
            ? Money.Max(Money.Zero, (available - required).Round())
            : Money.Zero;

        var nextIncome = NextConfirmedIncome(document, today);
        var nextAmountKnown = nextIncome is { } income
            && IncomeOccurrence.HasConfirmedPayableAmount(income.Source, income.Date, document.Payslips);

        var harmed = register.Lines
            .Where(line => line.StillRequired > Money.Zero && line.IsEssential)
            .OrderBy(line => line.DueDate ?? DateOnly.MaxValue)
            .ThenByDescending(line => line.StillRequired.Amount)
            .FirstOrDefault();

        var notice = available.IsZero
            ? "Available now: $0.00. Further spending is unsafe until additional income arrives " +
              "unless assistance or another confirmed balance is recorded."
            : safe.IsZero
                ? $"Available now: {available.ToDisplayString()}. None of it is safe to spend " +
                  "before the next confirmed income after reserved money and essential needs."
                : $"{safe.ToDisplayString()} may be spent before the next confirmed income without " +
                  "using reserved money or money needed for essentials.";

        var nextText = nextIncome is null
            ? "No confirmed income date is recorded."
            : nextAmountKnown
                ? $"{nextIncome.Value.Source.Name} on {nextIncome.Value.Date:yyyy-MM-dd}."
                : $"{nextIncome.Value.Source.Name} on {nextIncome.Value.Date:yyyy-MM-dd}. " +
                  "The amount is not treated as a full normal paycheque until payable hours are recorded.";

        var attention = register.Lines
            .Where(line => line.RequiresAttentionBefore(nextIncome?.Date))
            .Select(line => line.AttentionText)
            .ToList();

        return new OperationalPosition
        {
            Today = today,
            AvailableNow = available,
            Reserved = reserved,
            SafeToSpend = safe,
            EssentialRequired = required,
            EssentialFunded = funded,
            EssentialShortfall = shortfall,
            BillsDueBeforeNextIncome = allocation.BillsDueBeforeNextPayday,
            ForecastMonthlySurplus = overview.NetCashFlow.Amount ?? Money.Zero,
            NextConfirmedIncomeDate = nextIncome?.Date,
            NextConfirmedIncomeAmount = nextAmountKnown && nextIncome is { } known
                ? known.Source.GrossPerPeriod(IncomeEstimate.Conservative).Round()
                : null,
            NextIncomeText = nextText,
            SafetyNotice = notice,
            HarmedObligation = harmed is null
                ? "No dated essential obligation is currently marked as harmed by further spending."
                : $"{harmed.Name} would be harmed by further spending. {harmed.StillRequired.ToDisplayString()} is still required.",
            BillsRequiringAttention = attention,
            DeductionNotes = takeHome.Attention.Select(item => item.Message).ToList()
        };
    }

    public static (IncomeSource Source, DateOnly Date)? NextConfirmedIncome(
        BudgetDocument document,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(document);

        return document.IncomeSources
            .Where(source => source.IsActive)
            .SelectMany(source => source.PayDates(today.AddDays(1), today.AddYears(1))
                .Select(date => (Source: source, Date: date)))
            .OrderBy(item => item.Date)
            .Select(item => ((IncomeSource Source, DateOnly Date)?)item)
            .FirstOrDefault();
    }
}

/// <summary>
/// Whether a projected payday may be treated as a full normal period.
/// </summary>
public static class IncomeOccurrence
{
    public static bool IsOpeningPayDate(IncomeSource source, DateOnly payDate)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source.StartsOn is null)
        {
            return false;
        }

        var first = source.PayDates(source.StartsOn.Value, source.StartsOn.Value.AddYears(1)).FirstOrDefault();
        return first != default && payDate == first;
    }

    public static bool OpeningPeriodIsShorterThanAFullPayPeriod(IncomeSource source, DateOnly payDate)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source.StartsOn is null || !source.PayFrequency.IsRecurring())
        {
            return false;
        }

        if (!IsOpeningPayDate(source, payDate))
        {
            return false;
        }

        var periodDays = (int)Math.Round(source.WeeksPerPeriod * 7m);
        return (payDate.DayNumber - source.StartsOn.Value.DayNumber) + 1 < periodDays;
    }

    public static bool HasConfirmedPayableAmount(
        IncomeSource source,
        DateOnly payDate,
        IReadOnlyList<Payslip> payslips)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(payslips);

        var slip = payslips.FirstOrDefault(item => item.IncomeSourceId == source.Id && item.PayDate == payDate);
        if (slip is not null && slip.HoursWorked > 0m)
        {
            return true;
        }

        if (OpeningPeriodIsShorterThanAFullPayPeriod(source, payDate))
        {
            return false;
        }

        return source.PayFrequency.IsRecurring() && source.Role != IncomeRole.OneTime;
    }
}
