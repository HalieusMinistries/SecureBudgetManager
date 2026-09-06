using SecureBudgetManager.Core.Models;

namespace SecureBudgetManager.Core.Household;

/// <summary>
/// A balance the household tracks by hand. Deliberately not linked to a bank: no credentials, no
/// third parties, no cloud. The household types in the balance it can see.
/// </summary>
public sealed record BankAccount
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public Money CurrentBalance { get; init; } = Money.Zero;

    /// <summary>The account cash-flow projections start from.</summary>
    public bool IsPrimary { get; init; }

    public Guid? OwnerMemberId { get; init; }

    /// <summary>When the balance was last confirmed, so a stale figure is visible as stale.</summary>
    public DateOnly UpdatedOn { get; init; } = DateOnly.FromDateTime(DateTime.Today);

    public void Validate()
    {
        if (Id == Guid.Empty)
        {
            throw new ArgumentException("An account needs an identifier.");
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new ArgumentException("An account needs a name.");
        }
    }
}
