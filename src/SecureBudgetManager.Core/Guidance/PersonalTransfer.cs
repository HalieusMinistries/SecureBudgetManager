using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.Core.Guidance;

/// <summary>
/// A deliberate movement of money from one person's discretionary balance to another's.
///
/// The system never creates one of these on its own. Sharing personal money is a decision the
/// household makes, so it is recorded only when someone enters it, and it moves exactly the two
/// balances named here.
/// </summary>
public sealed record PersonalTransfer
{
    public required Guid Id { get; init; }

    public required Guid FromMemberId { get; init; }

    public required Guid ToMemberId { get; init; }

    public required Money Amount { get; init; }

    public required DateOnly Date { get; init; }

    /// <summary>Optional note. Left to the household; the system never infers a reason.</summary>
    public string? Purpose { get; init; }

    public bool IsRecurring { get; init; }

    /// <summary>Only meaningful when <see cref="IsRecurring"/> is true.</summary>
    public Frequency RecurringFrequency { get; init; } = Frequency.OneOff;

    /// <summary>The transfers that actually apply on a given payday.</summary>
    public bool AppliesOn(DateOnly date)
    {
        if (!IsRecurring)
        {
            return Date == date;
        }

        if (!RecurringFrequency.IsRecurring())
        {
            return Date == date;
        }

        return RecurrenceSchedule.Enumerate(RecurringFrequency, Date, date, date).Any();
    }

    public void Validate()
    {
        if (Id == Guid.Empty)
        {
            throw new ArgumentException("A transfer needs an identifier.");
        }

        if (FromMemberId == Guid.Empty || ToMemberId == Guid.Empty)
        {
            throw new ArgumentException("A transfer needs both a sender and a recipient.");
        }

        if (FromMemberId == ToMemberId)
        {
            throw new ArgumentException("A transfer cannot send money to the person who sent it.");
        }

        if (Amount.IsNegative || Amount.IsZero)
        {
            throw new ArgumentException("A transfer needs an amount above zero.");
        }

        if (IsRecurring && !RecurringFrequency.IsRecurring())
        {
            throw new ArgumentException("A recurring transfer needs a recurring frequency.");
        }
    }
}

/// <summary>What kind of record an obligation reserve is attached to.</summary>
public enum ObligationKind
{
    Expense = 0,
    Debt = 1,
    SavingsFund = 2,
    Goal = 3
}

/// <summary>
/// Money already set aside towards a future obligation.
///
/// Without this, every payday would be told to reserve the whole bill again. It is stored rather
/// than derived because the household may have moved money to a separate account, and only they
/// know how much actually made it there.
/// </summary>
public sealed record ObligationReserve
{
    public required Guid Id { get; init; }

    public required Guid ObligationId { get; init; }

    public required ObligationKind Kind { get; init; }

    public Money Reserved { get; init; } = Money.Zero;

    public required DateOnly UpdatedOn { get; init; }

    /// <summary>True when this money must never appear in safe-to-spend, even under pressure.</summary>
    public bool IsProtected { get; init; } = true;

    public string? Notes { get; init; }

    public void Validate()
    {
        if (Id == Guid.Empty)
        {
            throw new ArgumentException("A reserve needs an identifier.");
        }

        if (ObligationId == Guid.Empty)
        {
            throw new ArgumentException("A reserve must point at an obligation.");
        }

        if (Reserved.IsNegative)
        {
            throw new ArgumentException("A reserved amount cannot be negative.");
        }
    }
}
