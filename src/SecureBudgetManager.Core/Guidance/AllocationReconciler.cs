using SecureBudgetManager.Core.Models;

namespace SecureBudgetManager.Core.Guidance;

/// <summary>One person's share of a shared obligation, and how it was arrived at.</summary>
public sealed record ContributionShare
{
    public required Guid MemberId { get; init; }

    public required string MemberName { get; init; }

    /// <summary>Share of the combined usable net income, as a percentage.</summary>
    public required decimal Percent { get; init; }

    public required Money Amount { get; init; }

    /// <summary>True when this person absorbed the odd cent left over by rounding.</summary>
    public bool CarriesRoundingRemainder { get; init; }

    public Money RoundingRemainder { get; init; } = Money.Zero;
}

/// <summary>A shared obligation divided between people, with the rounding accounted for.</summary>
public sealed record ContributionSplit
{
    public required Money Total { get; init; }

    public required IReadOnlyList<ContributionShare> Shares { get; init; }

    public required string Explanation { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public Money AllocatedTotal => Money.Sum(Shares.Select(share => share.Amount)).Round();

    /// <summary>The whole point of the exercise: nothing is created or lost by rounding.</summary>
    public bool ReconcilesExactly => AllocatedTotal == Total.Round();
}

public static class AllocationReconciler
{
    /// <summary>
    /// Divides a shared obligation in proportion to usable net income.
    ///
    /// Percentages that look tidy rarely divide tidily: three ways of $100 is $33.33 three times,
    /// which is a cent short. The shares are therefore allocated by weight so they always add back
    /// to the original amount, and the person who absorbed the odd cent is named rather than left
    /// to be discovered.
    /// </summary>
    public static ContributionSplit Split(
        Money total,
        IReadOnlyList<(Guid MemberId, string Name, Money UsableNetIncome)> earners,
        Guid? remainderMemberId = null,
        IReadOnlyDictionary<Guid, decimal>? overridePercents = null)
    {
        ArgumentNullException.ThrowIfNull(earners);

        var warnings = new List<string>();
        var rounded = total.Round();

        var contributing = earners
            .Where(earner => earner.UsableNetIncome.Amount > 0m)
            .ToList();

        foreach (var excluded in earners.Where(earner => earner.UsableNetIncome.Amount <= 0m))
        {
            warnings.Add(
                $"{excluded.Name} has no usable net income this period, so no share of shared costs " +
                "has been assigned to them. Record an override if they should still contribute.");
        }

        if (contributing.Count == 0)
        {
            return new ContributionSplit
            {
                Total = rounded,
                Shares = [],
                Explanation = rounded.IsZero
                    ? "There are no shared obligations to divide."
                    : $"{rounded.ToDisplayString()} of shared obligations could not be divided because " +
                      "nobody has usable net income this period. The whole amount is unfunded.",
                Warnings = warnings
            };
        }

        if (overridePercents is { Count: > 0 })
        {
            return SplitByOverride(rounded, contributing, overridePercents, remainderMemberId, warnings);
        }

        var combined = Money.Sum(contributing.Select(earner => earner.UsableNetIncome)).Round();
        var weights = contributing.Select(earner => earner.UsableNetIncome.Amount).ToList();
        var amounts = rounded.AllocateByWeight(weights);

        var shares = BuildShares(contributing, combined, amounts, weights, rounded, remainderMemberId);

        var breakdown = string.Join(
            ", ",
            shares.Select(share =>
                $"{share.MemberName} {share.Percent:0.##}% ({share.Amount.ToDisplayString()})"));

        return new ContributionSplit
        {
            Total = rounded,
            Shares = shares,
            Explanation = rounded.IsZero
                ? "There are no shared obligations to divide."
                : $"{rounded.ToDisplayString()} of shared obligations divided in proportion to usable " +
                  $"net income of {combined.ToDisplayString()}: {breakdown}.",
            Warnings = warnings
        };
    }

    private static ContributionSplit SplitByOverride(
        Money total,
        IReadOnlyList<(Guid MemberId, string Name, Money UsableNetIncome)> contributing,
        IReadOnlyDictionary<Guid, decimal> overridePercents,
        Guid? remainderMemberId,
        List<string> warnings)
    {
        var weights = contributing
            .Select(earner => overridePercents.TryGetValue(earner.MemberId, out var percent) ? percent : 0m)
            .ToList();

        var stated = weights.Sum();

        if (stated <= 0m)
        {
            warnings.Add(
                "The agreed contribution override does not add up to anything, so the proportional " +
                "result has been used instead.");

            return Split(total, contributing, remainderMemberId);
        }

        if (Math.Abs(stated - 100m) > 0.01m)
        {
            warnings.Add(
                $"The agreed contribution shares add up to {stated:0.##}% rather than 100%. " +
                "They have been scaled to cover the shared obligations exactly.");
        }

        var amounts = total.AllocateByWeight(weights);
        var combined = Money.Sum(contributing.Select(earner => earner.UsableNetIncome)).Round();
        var shares = BuildShares(contributing, combined, amounts, weights, total, remainderMemberId);

        var breakdown = string.Join(
            ", ",
            shares.Select(share => $"{share.MemberName} {share.Amount.ToDisplayString()}"));

        return new ContributionSplit
        {
            Total = total,
            Shares = shares,
            Explanation =
                $"{total.ToDisplayString()} of shared obligations divided using the household's agreed " +
                $"override rather than the proportional result: {breakdown}.",
            Warnings = warnings
        };
    }

    private static List<ContributionShare> BuildShares(
        IReadOnlyList<(Guid MemberId, string Name, Money UsableNetIncome)> contributing,
        Money combined,
        Money[] amounts,
        IReadOnlyList<decimal> weights,
        Money total,
        Guid? remainderMemberId)
    {
        var weightTotal = weights.Sum();

        var shares = new List<ContributionShare>(contributing.Count);

        for (var i = 0; i < contributing.Count; i++)
        {
            var earner = contributing[i];

            var percent = weightTotal == 0m
                ? 0m
                : Math.Round(weights[i] / weightTotal * 100m, 2, MidpointRounding.ToEven);

            var ideal = weightTotal == 0m
                ? Money.Zero
                : new Money(Math.Round(
                    total.Amount * weights[i] / weightTotal,
                    2,
                    MidpointRounding.ToEven));

            shares.Add(new ContributionShare
            {
                MemberId = earner.MemberId,
                MemberName = earner.Name,
                Percent = percent,
                Amount = amounts[i].Round(),
                RoundingRemainder = (amounts[i] - ideal).Round()
            });
        }

        AssignRemainder(shares, total, remainderMemberId);

        return shares;
    }

    /// <summary>
    /// Moves the odd cent to whoever the household nominated, so the assignment is a decision
    /// rather than an accident of ordering, and marks who ended up with it.
    /// </summary>
    private static void AssignRemainder(
        List<ContributionShare> shares,
        Money total,
        Guid? remainderMemberId)
    {
        if (shares.Count == 0)
        {
            return;
        }

        if (remainderMemberId is { } memberId)
        {
            var target = shares.FindIndex(share => share.MemberId == memberId);

            if (target >= 0)
            {
                var carrier = shares.FirstOrDefault(share => !share.RoundingRemainder.IsZero);

                if (carrier is not null && carrier.MemberId != memberId)
                {
                    var remainder = carrier.RoundingRemainder;
                    var carrierIndex = shares.IndexOf(carrier);

                    shares[carrierIndex] = carrier with
                    {
                        Amount = (carrier.Amount - remainder).Round(),
                        RoundingRemainder = Money.Zero
                    };

                    shares[target] = shares[target] with
                    {
                        Amount = (shares[target].Amount + remainder).Round(),
                        RoundingRemainder = remainder,
                        CarriesRoundingRemainder = true
                    };

                    return;
                }
            }
        }

        for (var i = 0; i < shares.Count; i++)
        {
            if (shares[i].RoundingRemainder.Amount > 0m)
            {
                shares[i] = shares[i] with { CarriesRoundingRemainder = true };
            }
        }

        // Final guard: the shares must equal the original amount exactly.
        var allocated = Money.Sum(shares.Select(share => share.Amount)).Round();
        var drift = (total.Round() - allocated).Round();

        if (!drift.IsZero)
        {
            shares[0] = shares[0] with
            {
                Amount = (shares[0].Amount + drift).Round(),
                RoundingRemainder = (shares[0].RoundingRemainder + drift).Round(),
                CarriesRoundingRemainder = true
            };
        }
    }

    /// <summary>Describes where the odd cent went, for the explanation panel.</summary>
    public static string DescribeRounding(ContributionSplit split)
    {
        ArgumentNullException.ThrowIfNull(split);

        var carrier = split.Shares.FirstOrDefault(share => share.CarriesRoundingRemainder);

        if (carrier is null || carrier.RoundingRemainder.IsZero)
        {
            return "The shares divided exactly, so there was no rounding remainder to assign.";
        }

        return $"The split left {carrier.RoundingRemainder.Abs().ToDisplayString()} that could not be " +
               $"divided evenly. It was assigned to {carrier.MemberName}, so the shares add back to " +
               $"{split.Total.ToDisplayString()} exactly.";
    }
}
