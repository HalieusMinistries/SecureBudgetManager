using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.Core.Savings;

public enum FundPurpose
{
    EmergencyFund = 0,
    CarRepairs = 1,
    CarRegistration = 2,
    MedicalDeductible = 3,
    HomeRepairs = 4,
    Holiday = 5,
    Gifts = 6,
    Clothing = 7,
    AnnualSubscriptions = 8,
    TechnologyReplacement = 9,
    InsuranceDeductible = 10,
    PlannedPurchase = 11,
    VehicleReplacement = 12,
    Custom = 99
}

/// <summary>
/// A pot of money reserved for a predictable future cost. Sinking funds are the mechanism that
/// turns an annual bill into a weekly habit, which is what keeps irregular costs from becoming
/// emergencies.
/// </summary>
public sealed record SavingsFund
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required FundPurpose Purpose { get; init; }

    public Money CurrentBalance { get; init; } = Money.Zero;

    public Money? TargetAmount { get; init; }

    public DateOnly? TargetDate { get; init; }

    /// <summary>Contribution the household has committed to, per <see cref="ContributionFrequency"/>.</summary>
    public Money PlannedContribution { get; init; } = Money.Zero;

    public Frequency ContributionFrequency { get; init; } = Frequency.Monthly;

    /// <summary>Higher numbers are funded first when spare money is allocated.</summary>
    public int Priority { get; init; } = 1;

    public Guid? OwnerMemberId { get; init; }

    /// <summary>Set for funds that refill after being spent, such as a car-repair reserve.</summary>
    public bool IsRevolving { get; init; }

    public string? Notes { get; init; }

    public decimal ProgressPercent => TargetAmount is not { } target || target.IsZero
        ? 0m
        : Math.Min(100m, Math.Round(CurrentBalance.Amount / target.Amount * 100m, 1));

    public Money RemainingToTarget => TargetAmount is { } target
        ? Money.Max(Money.Zero, (target - CurrentBalance).Round())
        : Money.Zero;

    public bool IsFullyFunded => TargetAmount is { } target && CurrentBalance >= target;

    /// <summary>Contribution needed each period to reach the target by the deadline.</summary>
    public Money RequiredContribution(DateOnly asOf, Frequency frequency)
    {
        if (TargetAmount is not { } target || TargetDate is not { } deadline)
        {
            return Money.Zero;
        }

        var remaining = Money.Max(Money.Zero, (target - CurrentBalance).Round());

        if (remaining.IsZero)
        {
            return Money.Zero;
        }

        if (deadline <= asOf)
        {
            // The deadline has passed, so the whole remainder is needed now.
            return remaining;
        }

        var days = deadline.DayNumber - asOf.DayNumber;
        var periodsRemaining = days / 365.25m * frequency.PaymentsPerYear();

        return periodsRemaining < 1m
            ? remaining
            : (remaining / periodsRemaining).Round();
    }

    /// <summary>True when the committed contribution will not reach the target in time.</summary>
    public bool IsBehindSchedule(DateOnly asOf)
    {
        // A fund that has already reached its target needs nothing further, whatever is committed.
        if (TargetAmount is null || TargetDate is null || IsFullyFunded)
        {
            return false;
        }

        var required = RequiredContribution(asOf, ContributionFrequency);
        return PlannedContribution < required;
    }

    public Money AnnualContribution => FrequencyConverter.ToAnnual(PlannedContribution, ContributionFrequency);

    public void Validate()
    {
        if (Id == Guid.Empty)
        {
            throw new ArgumentException("A fund needs an identifier.");
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new ArgumentException("A fund needs a name.");
        }

        if (CurrentBalance.IsNegative)
        {
            throw new ArgumentException("A fund balance cannot be negative.");
        }

        if (TargetAmount is { } target && target.IsNegative)
        {
            throw new ArgumentException("A target amount cannot be negative.");
        }

        if (PlannedContribution.IsNegative)
        {
            throw new ArgumentException("A contribution cannot be negative.");
        }
    }
}

public sealed record FundSuggestion(
    Guid FundId,
    string FundName,
    Money SuggestedContribution,
    Frequency Frequency,
    string Reason);

public sealed record AllocationPlan(
    IReadOnlyList<FundSuggestion> Suggestions,
    Money TotalAllocated,
    Money Unallocated,
    Money Shortfall,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Suggests how to divide spare money between funds, respecting priority and deadlines.
/// </summary>
public static class SavingsAllocator
{
    /// <summary>
    /// Allocates available money across funds. Funds with deadlines are served first in priority
    /// order, then remaining money goes to the highest-priority unfunded targets.
    /// </summary>
    public static AllocationPlan Allocate(
        IEnumerable<SavingsFund> funds,
        Money available,
        Frequency frequency,
        DateOnly asOf)
    {
        ArgumentNullException.ThrowIfNull(funds);

        if (available.IsNegative)
        {
            throw new ArgumentException("Available money cannot be negative.", nameof(available));
        }

        var candidates = funds
            .Where(fund => !fund.IsFullyFunded || fund.IsRevolving)
            .ToList();

        foreach (var fund in candidates)
        {
            fund.Validate();
        }

        var suggestions = new List<FundSuggestion>();
        var warnings = new List<string>();
        var remaining = available;
        var totalRequired = Money.Zero;

        // Deadline-driven funds first, most urgent and highest priority ahead of the rest.
        var ordered = candidates
            .OrderByDescending(fund => fund.TargetDate is not null)
            .ThenByDescending(fund => fund.Priority)
            .ThenBy(fund => fund.TargetDate ?? DateOnly.MaxValue)
            .ToList();

        foreach (var fund in ordered)
        {
            var required = fund.RequiredContribution(asOf, frequency);

            if (required.IsZero && fund.PlannedContribution > Money.Zero)
            {
                required = FrequencyConverter.Convert(
                    fund.PlannedContribution,
                    fund.ContributionFrequency,
                    frequency);
            }

            if (required.IsZero)
            {
                continue;
            }

            totalRequired += required;

            var allocated = Money.Min(required, remaining);

            if (allocated.IsZero)
            {
                warnings.Add(
                    $"{fund.Name} needs {required.ToDisplayString()} per " +
                    $"{frequency.ToDisplayName().ToLowerInvariant()} but there is nothing left to allocate.");
                continue;
            }

            var reason = allocated < required
                ? $"Only {allocated.ToDisplayString()} of the {required.ToDisplayString()} needed could be allocated."
                : fund.TargetDate is { } deadline
                    ? $"Reaches {fund.TargetAmount?.ToDisplayString() ?? "the target"} by {deadline:yyyy-MM-dd}."
                    : "Matches the contribution you have planned.";

            suggestions.Add(new FundSuggestion(fund.Id, fund.Name, allocated, frequency, reason));
            remaining -= allocated;
        }

        var shortfall = Money.Max(Money.Zero, (totalRequired - available).Round());

        if (shortfall > Money.Zero)
        {
            warnings.Add(
                $"Your funds need {totalRequired.ToDisplayString()} in total but only " +
                $"{available.ToDisplayString()} is available. You are short {shortfall.ToDisplayString()}. " +
                "Either extend a deadline, lower a target, or accept that one fund will not be ready in time.");
        }

        return new AllocationPlan(
            suggestions,
            (available - remaining).Round(),
            remaining.Round(),
            shortfall,
            warnings);
    }

    /// <summary>
    /// Builds the standard set of sinking funds from a household's known irregular costs, so the
    /// household does not have to think of them unprompted.
    /// </summary>
    public static IReadOnlyList<SavingsFund> SuggestStandardFunds(
        Money monthlyEssentialSpending,
        decimal emergencyFundTargetMonths,
        Money? annualCarRegistration = null,
        Money? insuranceDeductible = null,
        Money? annualCarMaintenance = null)
    {
        var funds = new List<SavingsFund>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Emergency fund",
                Purpose = FundPurpose.EmergencyFund,
                TargetAmount = (monthlyEssentialSpending * emergencyFundTargetMonths).Round(),
                Priority = 10,
                IsRevolving = true,
                Notes = $"Covers {emergencyFundTargetMonths:0.#} months of essential spending."
            }
        };

        if (annualCarRegistration is { } registration && registration > Money.Zero)
        {
            funds.Add(new SavingsFund
            {
                Id = Guid.NewGuid(),
                Name = "Car registration",
                Purpose = FundPurpose.CarRegistration,
                TargetAmount = registration,
                PlannedContribution = (registration / 12m).Round(),
                ContributionFrequency = Frequency.Monthly,
                Priority = 6,
                IsRevolving = true
            });
        }

        if (insuranceDeductible is { } deductible && deductible > Money.Zero)
        {
            funds.Add(new SavingsFund
            {
                Id = Guid.NewGuid(),
                Name = "Insurance deductible",
                Purpose = FundPurpose.InsuranceDeductible,
                TargetAmount = deductible,
                Priority = 8,
                IsRevolving = true,
                Notes = "The amount you must find yourself before insurance pays."
            });
        }

        if (annualCarMaintenance is { } maintenance && maintenance > Money.Zero)
        {
            funds.Add(new SavingsFund
            {
                Id = Guid.NewGuid(),
                Name = "Car repairs and maintenance",
                Purpose = FundPurpose.CarRepairs,
                TargetAmount = maintenance,
                PlannedContribution = (maintenance / 12m).Round(),
                ContributionFrequency = Frequency.Monthly,
                Priority = 7,
                IsRevolving = true
            });
        }

        return funds;
    }
}
