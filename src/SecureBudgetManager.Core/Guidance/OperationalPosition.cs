using SecureBudgetManager.Core.Budgeting;
using SecureBudgetManager.Core.Household;
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

    public required IReadOnlyList<PersonOperationalPosition> People { get; init; }

    public required IReadOnlyList<string> UnassignedObligations { get; init; }

    public required Money UnassignedStillRequired { get; init; }

    public required Money HouseholdAvailableNow { get; init; }

    public required Money HouseholdSafeToSpend { get; init; }

    public required string CombinedForecastLabel { get; init; }

    public bool FurtherSpendingIsUnsafe => AvailableNow.IsZero || SafeToSpend.IsZero;

    public bool ForecastIsNotSpendable => true;
}

public sealed record PersonOperationalPosition
{
    public required Guid MemberId { get; init; }

    public required string Name { get; init; }

    public required Money AvailableNow { get; init; }

    public required Money SafeToSpend { get; init; }

    public required Money AssignedBillsBeforeNextIncome { get; init; }

    public DateOnly? NextConfirmedIncomeDate { get; init; }

    public required string NextIncomeText { get; init; }

    public required string FirstDepositText { get; init; }

    public required IReadOnlyList<string> BillsBeforeIncome { get; init; }

    public required Money GrossForecast { get; init; }

    public required Money Taxes { get; init; }

    public required Money Deductions { get; init; }

    public required Money TakeHome { get; init; }

    public required Money RemainingPersonalBalance { get; init; }

    public string AvailableNowText => AvailableNow.ToDisplayString();

    public string SafeToSpendText => SafeToSpend.ToDisplayString();

    public string TakeHomeText => TakeHome.ToDisplayString();
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
        var unassigned = register.Lines.Where(line => line.IsUnassigned && line.StillRequired > Money.Zero).ToList();
        var accounts = BillAssignmentPlanner.AccountBalances(document);
        var householdRequired = Money.Sum(register.Lines
            .Where(line =>
            {
                var expense = document.Expenses.FirstOrDefault(item => item.Id == line.Id);
                return expense is { Assignment: BillAssignment.SharedAccount };
            })
            .Select(line => line.StillRequired)).Round()
            + allocation.EssentialGroceries
            + allocation.SafetyBuffer;
        var householdSafe = accounts.Household.IsZero
            ? Money.Zero
            : Money.Max(Money.Zero, (accounts.Household - householdRequired).Round());

        var people = document.Members
            .Where(member => !member.IsDependant && !member.IsArchived)
            .Select(member => BuildPerson(document, allocation, register, accounts, member, today))
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
            DeductionNotes = takeHome.Attention.Select(item => item.Message).ToList(),
            People = people,
            UnassignedObligations = unassigned.Select(line => line.AttentionText).ToList(),
            UnassignedStillRequired = Money.Sum(unassigned.Select(line => line.StillRequired)).Round(),
            HouseholdAvailableNow = accounts.Household,
            HouseholdSafeToSpend = householdSafe,
            CombinedForecastLabel = BillAssignmentPlanner.CombinedForecastLabel
        };
    }

    public static (IncomeSource Source, DateOnly Date)? NextConfirmedIncome(
        BudgetDocument document,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(document);

        return NextConfirmedIncomeFor(document, today, memberId: null);
    }

    public static (IncomeSource Source, DateOnly Date)? NextConfirmedIncomeFor(
        BudgetDocument document,
        DateOnly today,
        Guid? memberId)
    {
        ArgumentNullException.ThrowIfNull(document);

        return document.IncomeSources
            .Where(source => source.IsActive)
            .Where(source => memberId is null || source.MemberId == memberId)
            .SelectMany(source => source.PayDates(today.AddDays(1), today.AddYears(1))
                .Select(date => (Source: source, Date: date)))
            .OrderBy(item => item.Date)
            .Select(item => ((IncomeSource Source, DateOnly Date)?)item)
            .FirstOrDefault();
    }

    private static PersonOperationalPosition BuildPerson(
        BudgetDocument document,
        PaychequeAllocation allocation,
        ObligationRegister register,
        AccountBuckets accounts,
        HouseholdMember member,
        DateOnly today)
    {
        var next = NextConfirmedIncomeFor(document, today, member.Id);
        var earner = allocation.For(member.Id);
        var personalAvailable = accounts.Personal.TryGetValue(member.Id, out var owned)
            ? owned
            : Money.Zero;
        var assignedBefore = Money.Sum(register.Lines
            .Where(line => line.StillRequired > Money.Zero)
            .Where(line =>
            {
                var expense = document.Expenses.FirstOrDefault(item => item.Id == line.Id);
                if (expense is null || BillAssignmentPlanner.IsUnassigned(expense))
                {
                    return false;
                }

                if (next?.Date is { } payday && line.DueDate is { } due && due >= payday)
                {
                    return false;
                }

                return BillAssignmentPlanner.Shares(expense, line.StillRequired).ContainsKey(member.Id);
            })
            .Select(line =>
            {
                var expense = document.Expenses.First(item => item.Id == line.Id);
                return BillAssignmentPlanner.Shares(expense, line.StillRequired)
                    .TryGetValue(member.Id, out var share)
                    ? share
                    : Money.Zero;
            })).Round();
        var personalSafe = personalAvailable.IsZero
            ? Money.Zero
            : Money.Max(Money.Zero, (personalAvailable - assignedBefore).Round());
        var bills = register.Lines
            .Where(line => line.StillRequired > Money.Zero)
            .Where(line =>
            {
                var expense = document.Expenses.FirstOrDefault(item => item.Id == line.Id);
                return expense is not null
                       && !BillAssignmentPlanner.IsUnassigned(expense)
                       && BillAssignmentPlanner.Shares(expense, line.StillRequired).ContainsKey(member.Id)
                       && (next?.Date is not { } payday || line.DueDate is null || line.DueDate < payday);
            })
            .Select(line => line.AttentionText)
            .ToList();

        var nextText = next is null
            ? $"No confirmed income date is recorded for {member.Name}."
            : $"{next.Value.Source.Name} on {next.Value.Date:yyyy-MM-dd}.";
        var firstDeposit = next is null
            ? "No deposit is recorded."
            : BillAssignmentPlanner.FirstDepositExplanation(next.Value.Source, next.Value.Date, document.Payslips);

        return new PersonOperationalPosition
        {
            MemberId = member.Id,
            Name = member.Name,
            AvailableNow = personalAvailable,
            SafeToSpend = personalSafe,
            AssignedBillsBeforeNextIncome = assignedBefore,
            NextConfirmedIncomeDate = next?.Date,
            NextIncomeText = nextText,
            FirstDepositText = firstDeposit,
            BillsBeforeIncome = bills,
            GrossForecast = earner?.Income.GrossIncome ?? Money.Zero,
            Taxes = earner?.Income.Taxes ?? Money.Zero,
            Deductions = earner?.Income.PayrollDeductions ?? Money.Zero,
            TakeHome = earner?.Income.UsableNetIncome ?? Money.Zero,
            RemainingPersonalBalance = earner?.RemainingPersonalBalance ?? Money.Zero
        };
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

        if (!source.PayScheduleConfirmed
            || OpeningPeriodIsShorterThanAFullPayPeriod(source, payDate)
            || source.Role == IncomeRole.OneTime
            || !source.PayFrequency.IsRecurring())
        {
            return false;
        }

        return true;
    }
}
