using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.Core.Planning;

public enum ScenarioStatus
{
    Draft = 0,
    UnderConsideration = 1,
    Approved = 2,
    Rejected = 3,
    Postponed = 4,
    ConvertedToBudget = 5
}

public enum ScenarioCase
{
    Best = 0,
    Expected = 1,
    Worst = 2
}

/// <summary>
/// A proposed decision with its full cost checklist. Multiple scenarios can be kept side by side
/// so alternatives are compared rather than considered one at a time.
/// </summary>
public sealed record Scenario
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required ScenarioKind Kind { get; init; }

    public required DateOnly ProposedStartDate { get; init; }

    /// <summary>The figure being advertised, kept separate so the true cost can be contrasted with it.</summary>
    public Money AdvertisedAmount { get; init; } = Money.Zero;

    public Frequency AdvertisedFrequency { get; init; } = Frequency.Monthly;

    public required IReadOnlyList<CostLine> Lines { get; init; }

    public ScenarioStatus Status { get; init; } = ScenarioStatus.Draft;

    /// <summary>
    /// Multiplier applied to variable running costs under each case, so a worst case can be
    /// modelled without re-entering every figure.
    /// </summary>
    public decimal WorstCaseUpliftPercent { get; init; } = 25m;

    public decimal BestCaseReductionPercent { get; init; } = 10m;

    public string? Notes { get; init; }

    /// <summary>Set when the household overrides the program's affordability verdict.</summary>
    public string? OverrideReason { get; init; }

    public bool HasOverride => !string.IsNullOrWhiteSpace(OverrideReason);

    public static Scenario FromTemplate(
        CostTemplate template,
        string name,
        DateOnly proposedStartDate,
        Guid? id = null)
    {
        ArgumentNullException.ThrowIfNull(template);
        template.Validate();

        return new Scenario
        {
            Id = id ?? Guid.NewGuid(),
            Name = name,
            Kind = template.Kind,
            ProposedStartDate = proposedStartDate,
            Lines = template.Lines.ToList()
        };
    }

    public Scenario WithLine(string lineName, Money amount, Frequency frequency)
    {
        var updated = Lines
            .Select(line => line.Name == lineName ? line.WithAmount(amount, frequency) : line)
            .ToList();

        return this with { Lines = updated };
    }

    public Scenario WithLineNotApplicable(string lineName)
    {
        var updated = Lines
            .Select(line => line.Name == lineName ? line.MarkNotApplicable() : line)
            .ToList();

        return this with { Lines = updated };
    }

    public void Validate()
    {
        if (Id == Guid.Empty)
        {
            throw new ArgumentException("A scenario needs an identifier.");
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new ArgumentException("A scenario needs a name.");
        }

        if (Lines.Count == 0)
        {
            throw new ArgumentException("A scenario needs at least one cost line.");
        }

        if (WorstCaseUpliftPercent < 0m || BestCaseReductionPercent is < 0m or > 100m)
        {
            throw new ArgumentException("Case adjustments are out of range.");
        }

        foreach (var line in Lines)
        {
            line.Validate();
        }
    }
}
