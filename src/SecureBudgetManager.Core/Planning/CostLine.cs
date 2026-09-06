using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.Core.Planning;

/// <summary>
/// Whether a cost is genuinely known, still missing, or deliberately excluded.
/// The distinction is what allows an honest "insufficient information" verdict instead of a
/// falsely confident one.
/// </summary>
public enum CostLineState
{
    /// <summary>The household has not answered this yet.</summary>
    Unanswered = 0,

    /// <summary>A figure has been entered.</summary>
    Provided = 1,

    /// <summary>The household has said this does not apply to them.</summary>
    NotApplicable = 2,

    /// <summary>No figure, but the template's typical value is being used as a placeholder.</summary>
    EstimatedFromTemplate = 3
}

/// <summary>
/// One line in a true-cost checklist: what it is, why it is being asked, and what it costs.
/// </summary>
public sealed record CostLine
{
    public required string Area { get; init; }

    public required string Name { get; init; }

    /// <summary>The question put to the household, in plain language.</summary>
    public required string Question { get; init; }

    public CostLineState State { get; init; } = CostLineState.Unanswered;

    public Money Amount { get; init; } = Money.Zero;

    public Frequency Frequency { get; init; } = Frequency.Monthly;

    /// <summary>A typical figure offered as a starting point, never used silently.</summary>
    public Money? TypicalAmount { get; init; }

    /// <summary>True when leaving this blank makes the whole assessment unreliable.</summary>
    public bool IsCritical { get; init; }

    /// <summary>
    /// True for costs that are not cash leaving the account, such as depreciation and opportunity
    /// cost. They belong in the true cost of ownership but must not appear in the cash-flow calendar.
    /// </summary>
    public bool IsNonCash { get; init; }

    public string? Notes { get; init; }

    public bool CountsTowardsCost => State is CostLineState.Provided or CostLineState.EstimatedFromTemplate;

    public bool IsMissing => State == CostLineState.Unanswered;

    public Money AnnualAmount => CountsTowardsCost && Frequency.IsRecurring()
        ? FrequencyConverter.ToAnnual(Amount, Frequency)
        : Money.Zero;

    /// <summary>One-off costs paid up front, such as a deposit or sales tax.</summary>
    public Money UpfrontAmount => CountsTowardsCost && Frequency == Frequency.OneOff
        ? Amount
        : Money.Zero;

    public Money MonthlyAmount => (AnnualAmount / 12m);

    public CostLine WithAmount(Money amount, Frequency frequency) => this with
    {
        Amount = amount,
        Frequency = frequency,
        State = CostLineState.Provided
    };

    public CostLine MarkNotApplicable() => this with
    {
        State = CostLineState.NotApplicable,
        Amount = Money.Zero
    };

    public CostLine UseTypicalValue() => TypicalAmount is null
        ? this
        : this with { Amount = TypicalAmount.Value, State = CostLineState.EstimatedFromTemplate };

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Area))
        {
            throw new ArgumentException("A cost line needs an area.");
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new ArgumentException("A cost line needs a name.");
        }

        if (Amount.IsNegative)
        {
            throw new ArgumentException($"The cost for {Name} cannot be negative.");
        }
    }
}
