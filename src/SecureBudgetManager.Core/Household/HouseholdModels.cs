using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.Core.Household;

public enum Ownership
{
    /// <summary>Belongs to one named member.</summary>
    Individual = 0,

    /// <summary>Shared by the household and split by a rule.</summary>
    Shared = 1
}

public enum SplitMethod
{
    /// <summary>Split evenly between the responsible members.</summary>
    Even = 0,

    /// <summary>Split by percentage weights that must total 100.</summary>
    Percentage = 1,

    /// <summary>One or more members pay fixed amounts; the remainder is split evenly.</summary>
    FixedAmount = 2,

    /// <summary>Split in proportion to each member's net income.</summary>
    ProportionalToIncome = 3
}

/// <summary>
/// Who has agreed to pay a bill. Unassigned bills stay visible and are not deducted from anyone.
/// </summary>
public enum BillAssignment
{
    Unassigned = 0,
    MemberPaysAll = 1,
    PercentageSplit = 2,
    FixedDollarSplit = 3,
    EnteredContributions = 4,
    SharedAccount = 5
}

public sealed record HouseholdMember
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public bool IsDependant { get; init; }

    public DateOnly? DateOfBirth { get; init; }

    /// <summary>Optional personal spending allowance, kept out of shared expenses.</summary>
    public Money PersonalAllowance { get; init; } = Money.Zero;

    public Frequency PersonalAllowanceFrequency { get; init; } = Frequency.Weekly;

    /// <summary>
    /// Whether the guidance system calculates a personal discretionary allowance for this person.
    ///
    /// It defaults to false and is never inferred. Being a household member, or even an earner,
    /// does not by itself create a personal envelope: a dependant is included in shared household
    /// needs without receiving spending money of their own, and turning eligibility on is an
    /// explicit decision the household records here.
    /// </summary>
    public bool IsDiscretionaryEligible { get; init; }

    /// <summary>
    /// Whether this person is counted when splitting shared costs. Adults default to true;
    /// dependants default to false unless the household records otherwise.
    /// </summary>
    public bool ParticipatesInSharedCosts { get; init; } = true;

    /// <summary>
    /// Archived members keep their identifier so historical income, expenses and transfers stay
    /// valid. They are excluded from new allocations.
    /// </summary>
    public bool IsArchived { get; init; }

    public DateOnly? EffectiveFrom { get; init; }

    public DateOnly? EffectiveTo { get; init; }

    public string? Notes { get; init; }

    public bool IsCurrentOn(DateOnly date) =>
        !IsArchived
        && (EffectiveFrom is null || EffectiveFrom <= date)
        && (EffectiveTo is null || EffectiveTo >= date);

    public void Validate()
    {
        if (Id == Guid.Empty)
        {
            throw new ArgumentException("A household member needs an identifier.");
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new ArgumentException("A household member needs a name.");
        }

        if (PersonalAllowance.IsNegative)
        {
            throw new ArgumentException("A personal allowance cannot be negative.");
        }

        if (EffectiveFrom is not null && EffectiveTo is not null && EffectiveTo < EffectiveFrom)
        {
            throw new ArgumentException("A member cannot end before their effective start date.");
        }
    }
}

/// <summary>
/// How a shared cost is divided. Shares always sum exactly to the original amount:
/// leftover cents are distributed rather than dropped.
/// </summary>
public sealed record SplitRule
{
    public required SplitMethod Method { get; init; }

    /// <summary>Member identifiers taking part, in a stable order.</summary>
    public required IReadOnlyList<Guid> Participants { get; init; }

    /// <summary>Percentage weights for <see cref="SplitMethod.Percentage"/>, aligned with participants.</summary>
    public IReadOnlyList<decimal> Percentages { get; init; } = [];

    /// <summary>Fixed contributions for <see cref="SplitMethod.FixedAmount"/>, aligned with participants.</summary>
    public IReadOnlyList<Money> FixedAmounts { get; init; } = [];

    public static SplitRule Even(params Guid[] participants) =>
        new() { Method = SplitMethod.Even, Participants = participants };

    public static SplitRule SoleResponsibility(Guid member) =>
        new() { Method = SplitMethod.Even, Participants = [member] };

    public void Validate()
    {
        if (Participants.Count == 0)
        {
            throw new ArgumentException("A split rule needs at least one participant.");
        }

        switch (Method)
        {
            case SplitMethod.Percentage:
                if (Percentages.Count != Participants.Count)
                {
                    throw new ArgumentException("Each participant needs a percentage.");
                }

                if (Percentages.Any(percentage => percentage < 0m))
                {
                    throw new ArgumentException("Percentages cannot be negative.");
                }

                if (Math.Abs(Percentages.Sum() - 100m) > 0.001m)
                {
                    throw new ArgumentException("Percentages must total 100.");
                }

                break;

            case SplitMethod.FixedAmount:
                if (FixedAmounts.Count != Participants.Count)
                {
                    throw new ArgumentException("Each participant needs a fixed amount.");
                }

                if (FixedAmounts.Any(amount => amount.IsNegative))
                {
                    throw new ArgumentException("Fixed amounts cannot be negative.");
                }

                break;

            case SplitMethod.Even:
            case SplitMethod.ProportionalToIncome:
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(Method), Method, "Unknown split method.");
        }
    }

    /// <summary>
    /// Divides an amount between participants. <paramref name="netIncomeByMember"/> is only
    /// consulted for <see cref="SplitMethod.ProportionalToIncome"/>.
    /// </summary>
    public IReadOnlyDictionary<Guid, Money> Divide(
        Money amount,
        IReadOnlyDictionary<Guid, Money>? netIncomeByMember = null)
    {
        Validate();

        var shares = Method switch
        {
            SplitMethod.Even => amount.Allocate(Participants.Count),
            SplitMethod.Percentage => amount.AllocateByWeight(Percentages),
            SplitMethod.FixedAmount => DivideWithFixedAmounts(amount),
            SplitMethod.ProportionalToIncome => DivideByIncome(amount, netIncomeByMember),
            _ => throw new ArgumentOutOfRangeException(nameof(Method), Method, "Unknown split method.")
        };

        var result = new Dictionary<Guid, Money>(Participants.Count);
        for (var i = 0; i < Participants.Count; i++)
        {
            result[Participants[i]] = shares[i];
        }

        return result;
    }

    private Money[] DivideWithFixedAmounts(Money amount)
    {
        var fixedTotal = Money.Sum(FixedAmounts);
        var remainder = amount - fixedTotal;

        // If the fixed contributions already cover the bill, nobody owes the remainder.
        if (remainder <= Money.Zero)
        {
            return FixedAmounts.ToArray();
        }

        var payersOfRemainder = Enumerable.Range(0, Participants.Count)
            .Where(index => FixedAmounts[index].IsZero)
            .ToArray();

        // When everyone has a fixed amount, share the shortfall evenly.
        if (payersOfRemainder.Length == 0)
        {
            payersOfRemainder = Enumerable.Range(0, Participants.Count).ToArray();
        }

        var extra = remainder.Allocate(payersOfRemainder.Length);
        var shares = FixedAmounts.ToArray();

        for (var i = 0; i < payersOfRemainder.Length; i++)
        {
            shares[payersOfRemainder[i]] += extra[i];
        }

        return shares;
    }

    private Money[] DivideByIncome(Money amount, IReadOnlyDictionary<Guid, Money>? netIncomeByMember)
    {
        if (netIncomeByMember is null)
        {
            throw new ArgumentNullException(
                nameof(netIncomeByMember),
                "Income-proportional splitting needs each participant's net income.");
        }

        var weights = Participants
            .Select(participant => netIncomeByMember.TryGetValue(participant, out var income)
                ? Math.Max(0m, income.Amount)
                : 0m)
            .ToList();

        // Fall back to an even split rather than failing when nobody has recorded income yet.
        return weights.Sum() == 0m
            ? amount.Allocate(Participants.Count)
            : amount.AllocateByWeight(weights);
    }
}

public sealed record HouseholdPreferences
{
    public string CurrencySymbol { get; init; } = "$";

    public string DateFormat { get; init; } = "yyyy-MM-dd";

    /// <summary>Balance the household never wants to spend below.</summary>
    public Money MinimumBalanceReserve { get; init; } = Money.Zero;

    /// <summary>Extra breathing room required before a purchase counts as comfortable.</summary>
    public Money MinimumBreathingRoom { get; init; } = new(200m);

    /// <summary>Target emergency fund, expressed in months of essential spending.</summary>
    public decimal EmergencyFundTargetMonths { get; init; } = 3m;

    /// <summary>How far ahead planning engines should look, in months. Defaults to 12.</summary>
    public int ForecastHorizonMonths { get; init; } = 12;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(CurrencySymbol))
        {
            throw new ArgumentException("A currency symbol is required.");
        }

        if (EmergencyFundTargetMonths < 0m)
        {
            throw new ArgumentException("The emergency fund target cannot be negative.");
        }

        if (ForecastHorizonMonths is < 1 or > 60)
        {
            throw new ArgumentException("The forecast horizon must be between 1 and 60 months.");
        }
    }
}

public sealed record Household
{
    public required string Name { get; init; }

    public required IReadOnlyList<HouseholdMember> Members { get; init; }

    public HouseholdPreferences Preferences { get; init; } = new();

    public IEnumerable<HouseholdMember> Adults => Members.Where(member => !member.IsDependant);

    public IEnumerable<HouseholdMember> Dependants => Members.Where(member => member.IsDependant);

    public IEnumerable<HouseholdMember> ActiveMembers => Members.Where(member => !member.IsArchived);

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new ArgumentException("A household needs a name.");
        }

        if (Members.Count == 0)
        {
            throw new ArgumentException("A household needs at least one member.");
        }

        foreach (var member in Members)
        {
            member.Validate();
        }

        if (Members.Select(member => member.Id).Distinct().Count() != Members.Count)
        {
            throw new ArgumentException("Household members must have distinct identifiers.");
        }

        if (!Members.Any(member => !member.IsDependant && !member.IsArchived))
        {
            throw new ArgumentException("A household needs at least one active adult member.");
        }

        Preferences.Validate();
    }
}
