using SecureBudgetManager.Core.Models;

namespace SecureBudgetManager.Core.Guidance;

/// <summary>
/// One slice of leftover money after protected essentials. Positive assignment and shortfall
/// reduction are separate operations and must not be treated as reverses of each other.
/// </summary>
public sealed record AllocationSlice(
    Money Entertainment,
    Money Goals,
    Money Discretionary,
    Money FlexibleSavings,
    Money Surplus)
{
    public Money Total =>
        (Entertainment + Goals + Discretionary + FlexibleSavings + Surplus).Round();

    public Money For(ReducibleBucket bucket) => bucket switch
    {
        ReducibleBucket.SharedEntertainment => Entertainment,
        ReducibleBucket.LowerPriorityGoals => Goals,
        ReducibleBucket.OptionalDiscretionary => Discretionary,
        ReducibleBucket.FlexibleSavings => FlexibleSavings,
        ReducibleBucket.UnallocatedSurplus => Surplus,
        _ => Money.Zero
    };

    public AllocationSlice With(ReducibleBucket bucket, Money amount) => bucket switch
    {
        ReducibleBucket.SharedEntertainment => this with { Entertainment = amount.Round() },
        ReducibleBucket.LowerPriorityGoals => this with { Goals = amount.Round() },
        ReducibleBucket.OptionalDiscretionary => this with { Discretionary = amount.Round() },
        ReducibleBucket.FlexibleSavings => this with { FlexibleSavings = amount.Round() },
        ReducibleBucket.UnallocatedSurplus => this with { Surplus = amount.Round() },
        _ => this
    };
}

/// <summary>
/// Positive funding walks <see cref="FundingBucket"/> and never parks leftover in surplus
/// before configured allocations. Shortfall reduction walks <see cref="ReducibleBucket"/>
/// and consumes unallocated surplus first.
/// </summary>
public static class AllocationOrder
{
    public static AllocationSlice AssignByFundingOrder(
        Money leftover,
        Money requestedEntertainment,
        Money requestedGoals,
        Money requestedDiscretionary,
        Money requestedFlexible,
        IReadOnlyList<FundingBucket>? order = null)
    {
        var remaining = Money.Max(Money.Zero, leftover.Round());
        var entertainment = Money.Zero;
        var goals = Money.Zero;
        var discretionary = Money.Zero;
        var flexible = Money.Zero;

        foreach (var bucket in order is { Count: > 0 } ? order : AllocationRules.DefaultFundingOrder)
        {
            switch (bucket)
            {
                case FundingBucket.FamilyAndBelonging:
                    (entertainment, remaining) = Take(requestedEntertainment, remaining);
                    break;
                case FundingBucket.Growth:
                    (goals, remaining) = Take(requestedGoals, remaining);
                    break;
                case FundingBucket.PersonalDiscretionary:
                    (discretionary, remaining) = Take(requestedDiscretionary, remaining);
                    break;
                case FundingBucket.FlexibleAllocations:
                    (flexible, remaining) = Take(requestedFlexible, remaining);
                    break;
            }
        }

        return new AllocationSlice(entertainment, goals, discretionary, flexible, remaining);
    }

    public static AllocationSlice ReduceByReductionOrder(
        AllocationSlice current,
        Money amountToFree,
        IReadOnlyList<ReducibleBucket>? order = null)
    {
        var remaining = Money.Max(Money.Zero, amountToFree.Round());
        var result = current;

        if (remaining.IsZero)
        {
            return result;
        }

        foreach (var bucket in order is { Count: > 0 } ? order : AllocationRules.DefaultReductionOrder)
        {
            var present = result.For(bucket);
            var cut = Money.Min(present, remaining);
            result = result.With(bucket, Money.Max(Money.Zero, (present - cut).Round()));
            remaining = Money.Max(Money.Zero, (remaining - cut).Round());

            if (remaining.IsZero)
            {
                break;
            }
        }

        return result;
    }

    public static AllocationSlice FillConfiguredOrReduce(
        Money leftover,
        Money requestedEntertainment,
        Money requestedGoals,
        Money requestedDiscretionary,
        Money requestedFlexible,
        IReadOnlyList<ReducibleBucket>? reductionOrder = null)
    {
        var requested = (requestedEntertainment + requestedGoals + requestedDiscretionary + requestedFlexible).Round();
        leftover = Money.Max(Money.Zero, leftover.Round());

        if (leftover >= requested)
        {
            return new AllocationSlice(
                requestedEntertainment.Round(),
                requestedGoals.Round(),
                requestedDiscretionary.Round(),
                requestedFlexible.Round(),
                (leftover - requested).Round());
        }

        return ReduceByReductionOrder(
            new AllocationSlice(
                requestedEntertainment.Round(),
                requestedGoals.Round(),
                requestedDiscretionary.Round(),
                requestedFlexible.Round(),
                Money.Zero),
            (requested - leftover).Round(),
            reductionOrder);
    }

    private static (Money Allocated, Money Remaining) Take(Money requested, Money available)
    {
        var allocated = Money.Min(Money.Max(Money.Zero, requested), available);
        return (allocated.Round(), Money.Max(Money.Zero, (available - allocated).Round()));
    }
}
