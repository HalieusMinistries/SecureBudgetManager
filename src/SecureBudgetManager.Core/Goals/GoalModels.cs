using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.Core.Goals;

public enum GoalPriority
{
    Low = 0,
    Medium = 1,
    High = 2,
    Essential = 3
}

public enum GoalScope
{
    Household = 0,
    Individual = 1
}

public sealed record Goal
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required Money TargetAmount { get; init; }

    public required DateOnly TargetDate { get; init; }

    public Money CurrentAmount { get; init; } = Money.Zero;

    public GoalPriority Priority { get; init; } = GoalPriority.Medium;

    public GoalScope Scope { get; init; } = GoalScope.Household;

    public Guid? OwnerMemberId { get; init; }

    public Money PlannedContribution { get; init; } = Money.Zero;

    public Frequency ContributionFrequency { get; init; } = Frequency.Monthly;

    /// <summary>Set when the goal came from an approved planning scenario.</summary>
    public Guid? SourceScenarioId { get; init; }

    public string? Notes { get; init; }

    public Money Remaining => Money.Max(Money.Zero, (TargetAmount - CurrentAmount).Round());

    public decimal ProgressPercent => TargetAmount.IsZero
        ? 0m
        : Math.Min(100m, Math.Round(CurrentAmount.Amount / TargetAmount.Amount * 100m, 1));

    public bool IsComplete => CurrentAmount >= TargetAmount;

    /// <summary>Contribution needed each period to hit the target on time.</summary>
    public Money RequiredContribution(DateOnly asOf, Frequency frequency)
    {
        if (Remaining.IsZero)
        {
            return Money.Zero;
        }

        if (TargetDate <= asOf)
        {
            return Remaining;
        }

        var periods = (TargetDate.DayNumber - asOf.DayNumber) / 365.25m * frequency.PaymentsPerYear();
        return periods < 1m ? Remaining : (Remaining / periods).Round();
    }

    /// <summary>Date the goal will actually be reached at the current contribution rate.</summary>
    public DateOnly? ProjectedCompletionDate(DateOnly asOf)
    {
        if (Remaining.IsZero)
        {
            return asOf;
        }

        var annual = FrequencyConverter.ToAnnual(PlannedContribution, ContributionFrequency);

        if (annual <= Money.Zero)
        {
            return null;
        }

        var years = Remaining.Amount / annual.Amount;
        var days = (int)Math.Ceiling(years * 365.25m);

        // Keep the projection inside DateOnly's range for goals funded at a trickle.
        return days > 365 * 200 ? null : asOf.AddDays(days);
    }

    public void Validate()
    {
        if (Id == Guid.Empty)
        {
            throw new ArgumentException("A goal needs an identifier.");
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new ArgumentException("A goal needs a name.");
        }

        if (TargetAmount.IsNegative)
        {
            throw new ArgumentException("A goal target cannot be negative.");
        }

        if (CurrentAmount.IsNegative)
        {
            throw new ArgumentException("A goal balance cannot be negative.");
        }

        if (PlannedContribution.IsNegative)
        {
            throw new ArgumentException("A goal contribution cannot be negative.");
        }

        if (Scope == GoalScope.Individual && OwnerMemberId is null)
        {
            throw new ArgumentException("An individual goal needs an owner.");
        }
    }
}

public sealed record GoalConflict(
    IReadOnlyList<string> GoalNames,
    Money RequiredTotal,
    Money Available,
    Money Shortfall,
    string Explanation,
    IReadOnlyList<string> SuggestedTradeOffs);

public sealed record GoalStatus(
    Goal Goal,
    Money RequiredContribution,
    bool OnTrack,
    DateOnly? ProjectedCompletion,
    string Explanation);

public sealed record ContributionChangeEffect(
    Money OldContribution,
    Money NewContribution,
    DateOnly? OldCompletion,
    DateOnly? NewCompletion,
    int DaysDifference,
    string Explanation);

/// <summary>
/// Reviews goals together rather than one at a time, because goals compete for the same money and
/// the conflict is the useful information.
/// </summary>
public static class GoalPlanner
{
    public static IReadOnlyList<GoalStatus> Review(
        IEnumerable<Goal> goals,
        DateOnly asOf,
        Frequency frequency)
    {
        ArgumentNullException.ThrowIfNull(goals);

        var statuses = new List<GoalStatus>();

        foreach (var goal in goals)
        {
            goal.Validate();

            var required = goal.RequiredContribution(asOf, frequency);
            var planned = FrequencyConverter.Convert(
                goal.PlannedContribution,
                goal.ContributionFrequency,
                frequency);

            var onTrack = goal.IsComplete || planned >= required;
            var projected = goal.ProjectedCompletionDate(asOf);

            var explanation = goal.IsComplete
                ? "This goal is complete."
                : onTrack
                    ? $"Contributing {planned.ToDisplayString()} per " +
                      $"{frequency.ToDisplayName().ToLowerInvariant()} meets the " +
                      $"{required.ToDisplayString()} needed to finish by {goal.TargetDate:yyyy-MM-dd}."
                    : projected is { } date
                        ? $"At {planned.ToDisplayString()} per " +
                          $"{frequency.ToDisplayName().ToLowerInvariant()} this finishes around " +
                          $"{date:yyyy-MM-dd}, after your {goal.TargetDate:yyyy-MM-dd} target. " +
                          $"You would need {required.ToDisplayString()} instead."
                        : $"No contribution is set, so this goal will not be reached. " +
                          $"It needs {required.ToDisplayString()} per " +
                          $"{frequency.ToDisplayName().ToLowerInvariant()}.";

            statuses.Add(new GoalStatus(goal, required, onTrack, projected, explanation));
        }

        return statuses
            .OrderByDescending(status => status.Goal.Priority)
            .ThenBy(status => status.Goal.TargetDate)
            .ToList();
    }

    /// <summary>
    /// Detects the case where the goals collectively need more than the household has, and suggests
    /// which trade-offs would resolve it.
    /// </summary>
    public static GoalConflict? FindConflict(
        IEnumerable<Goal> goals,
        Money availablePerPeriod,
        DateOnly asOf,
        Frequency frequency)
    {
        var statuses = Review(goals, asOf, frequency)
            .Where(status => !status.Goal.IsComplete)
            .ToList();

        if (statuses.Count == 0)
        {
            return null;
        }

        var required = Money.Sum(statuses.Select(status => status.RequiredContribution));

        if (required <= availablePerPeriod)
        {
            return null;
        }

        var shortfall = (required - availablePerPeriod).Round();
        var tradeOffs = new List<string>();

        // Suggest deferring the lowest-priority goal that would close the gap on its own.
        var deferrable = statuses
            .OrderBy(status => status.Goal.Priority)
            .ThenByDescending(status => status.RequiredContribution.Amount)
            .FirstOrDefault(status => status.RequiredContribution >= shortfall);

        if (deferrable is not null)
        {
            tradeOffs.Add(
                $"Pausing \"{deferrable.Goal.Name}\" frees " +
                $"{deferrable.RequiredContribution.ToDisplayString()} per " +
                $"{frequency.ToDisplayName().ToLowerInvariant()}, which covers the shortfall on its own.");
        }

        // Suggest extending the deadline of the largest goal instead.
        var largest = statuses.MaxBy(status => status.RequiredContribution.Amount);
        if (largest is not null)
        {
            var extendedRequired = (largest.RequiredContribution - shortfall).Round();
            if (extendedRequired > Money.Zero)
            {
                var remaining = largest.Goal.Remaining;
                var periodsNeeded = extendedRequired.IsZero
                    ? 0m
                    : remaining.Amount / extendedRequired.Amount;
                var yearsNeeded = periodsNeeded / frequency.PaymentsPerYear();
                var newDate = asOf.AddDays((int)Math.Ceiling(yearsNeeded * 365.25m));

                tradeOffs.Add(
                    $"Moving \"{largest.Goal.Name}\" to {newDate:yyyy-MM-dd} lowers its contribution to " +
                    $"{extendedRequired.ToDisplayString()} and removes the shortfall.");
            }
        }

        tradeOffs.Add(
            $"Alternatively, find {shortfall.ToDisplayString()} more per " +
            $"{frequency.ToDisplayName().ToLowerInvariant()} by reducing optional spending.");

        return new GoalConflict(
            statuses.Select(status => status.Goal.Name).ToList(),
            required,
            availablePerPeriod,
            shortfall,
            $"Your {statuses.Count} active goals need {required.ToDisplayString()} per " +
            $"{frequency.ToDisplayName().ToLowerInvariant()} to finish on time, but only " +
            $"{availablePerPeriod.ToDisplayString()} is available. " +
            $"They cannot all be met as planned.",
            tradeOffs);
    }

    /// <summary>Shows what changing a contribution does to the completion date.</summary>
    public static ContributionChangeEffect ModelContributionChange(
        Goal goal,
        Money newContribution,
        DateOnly asOf)
    {
        ArgumentNullException.ThrowIfNull(goal);
        goal.Validate();

        if (newContribution.IsNegative)
        {
            throw new ArgumentException("A contribution cannot be negative.", nameof(newContribution));
        }

        var oldCompletion = goal.ProjectedCompletionDate(asOf);
        var updated = goal with { PlannedContribution = newContribution };
        var newCompletion = updated.ProjectedCompletionDate(asOf);

        var difference = oldCompletion is { } oldDate && newCompletion is { } newDate
            ? oldDate.DayNumber - newDate.DayNumber
            : 0;

        var explanation = newCompletion is not { } finish
            ? "With no contribution this goal will never be reached."
            : oldCompletion is null
                ? $"Contributing {newContribution.ToDisplayString()} finishes this goal around {finish:yyyy-MM-dd}."
                : difference > 0
                    ? $"Raising the contribution to {newContribution.ToDisplayString()} finishes " +
                      $"{difference} day(s) sooner, around {finish:yyyy-MM-dd}."
                    : difference < 0
                        ? $"Lowering the contribution to {newContribution.ToDisplayString()} delays this by " +
                          $"{Math.Abs(difference)} day(s), to around {finish:yyyy-MM-dd}."
                        : "The completion date does not change.";

        return new ContributionChangeEffect(
            goal.PlannedContribution,
            newContribution,
            oldCompletion,
            newCompletion,
            difference,
            explanation);
    }
}
