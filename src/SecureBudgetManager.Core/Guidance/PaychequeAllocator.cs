using SecureBudgetManager.Core.CashFlow;
using SecureBudgetManager.Core.Expenses;
using SecureBudgetManager.Core.Goals;
using SecureBudgetManager.Core.Household;
using SecureBudgetManager.Core.International;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Storage;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.Core.Guidance;

/// <summary>How safe a spending decision is, and why the household cannot always be told.</summary>
public enum SpendingSafety
{
    /// <summary>Fits without harming protected needs, required reserves or the safety buffer.</summary>
    Safe = 0,

    /// <summary>Fits, but reduces the margin, flexible savings or a lower-priority goal.</summary>
    Caution = 1,

    /// <summary>Would underfund an upcoming obligation, protected allocation or essential category.</summary>
    NotRecommended = 2,

    /// <summary>Creates a projected deficit, a missed essential obligation or new borrowing.</summary>
    Unsafe = 3,

    /// <summary>Income, balance, bill, tax, deduction or due-date information is missing.</summary>
    InsufficientInformation = 4
}

public static class SpendingSafetyExtensions
{
    public static string ToDisplayName(this SpendingSafety safety) => safety switch
    {
        SpendingSafety.Safe => "Safe",
        SpendingSafety.Caution => "Caution",
        SpendingSafety.NotRecommended => "Not recommended",
        SpendingSafety.Unsafe => "Unsafe",
        SpendingSafety.InsufficientInformation => "Insufficient information",
        _ => throw new ArgumentOutOfRangeException(nameof(safety), safety, "Unknown safety rating.")
    };
}

/// <summary>One eligible earner's part of the paycheque, kept separate from everyone else's.</summary>
public sealed record EarnerAllocation
{
    public required EarnerIncome Income { get; init; }

    public required decimal ContributionPercent { get; init; }

    public required Money ShareOfSharedObligations { get; init; }

    public required Money SavingsContribution { get; init; }

    /// <summary>What is left of this person's own money once their agreed obligations are met.</summary>
    public required Money RemainingBeforeDiscretionary { get; init; }

    public required Money DiscretionaryAllowance { get; init; }

    public required Money PersonalSpendingUsed { get; init; }

    public Money TransfersSent { get; init; } = Money.Zero;

    public Money TransfersReceived { get; init; } = Money.Zero;

    public bool CarriesRoundingRemainder { get; init; }

    public required string Explanation { get; init; }

    public Guid MemberId => Income.MemberId;

    public string MemberName => Income.MemberName;

    public Money RemainingPersonalBalance =>
        (DiscretionaryAllowance - PersonalSpendingUsed - TransfersSent + TransfersReceived).Round();
}

/// <summary>
/// The reconciliation check. Every dollar that came in is accounted for exactly once, or the
/// difference is named as an unfunded shortfall rather than quietly absorbed.
/// </summary>
public sealed record AllocationLedger
{
    public required Money TotalIn { get; init; }

    public required Money TotalAllocated { get; init; }

    public required Money UnfundedShortfall { get; init; }

    public required IReadOnlyList<(string Label, Money Amount)> Lines { get; init; }

    public Money Difference => (TotalIn + UnfundedShortfall - TotalAllocated).Round();

    public bool ReconcilesExactly => Difference.IsZero;
}

/// <summary>
/// Conservative income may limit what can be assigned. It never reduces the genuine requirement.
/// </summary>
public sealed record EssentialCoverage
{
    public required Money Required { get; init; }

    public required Money Available { get; init; }

    public required Money Funded { get; init; }

    public required Money Unfunded { get; init; }

    public required string Explanation { get; init; }

    public bool ReconcilesExactly => (Funded + Unfunded).Round() == Required.Round();
}

/// <summary>Everything one payday's guidance needs to say, with the working behind it.</summary>
public sealed record PaychequeAllocation
{
    public required DateOnly Today { get; init; }

    public DateOnly? NextPayday { get; init; }

    /// <summary>The last date this guidance covers. After it, the figures must be recalculated.</summary>
    public required DateOnly ValidThrough { get; init; }

    public required IncomeBasis Basis { get; init; }

    public required Money AvailableNow { get; init; }

    public required Money IncomeReceived { get; init; }

    public required Money IncomeExpectedBeforeNextPayday { get; init; }

    /// <summary>Income that may be relied on. Optimistic income above this never funds essentials.</summary>
    public required Money ReliableIncome { get; init; }

    public required Money AdditionalIncomeAboveBaseline { get; init; }

    public required Money BillsDueBeforeNextPayday { get; init; }

    public required Money ReservationsForLaterBills { get; init; }

    public required Money EssentialGroceries { get; init; }

    public required Money OptionalGroceries { get; init; }

    public required Money EssentialTransport { get; init; }

    public required Money OtherEssentialSpending { get; init; }

    public required Money MinimumDebtPayments { get; init; }

    public required Money RequiredSinkingFunds { get; init; }

    public required Money ProtectedSavings { get; init; }

    public required Money CommittedFunds { get; init; }

    public required Money SafetyBuffer { get; init; }

    public required Money FlexibleSavings { get; init; }

    public required Money SharedEntertainment { get; init; }

    public required Money LowerPriorityGoals { get; init; }

    public required Money UnallocatedSurplus { get; init; }

    public required Money SafeToSpend { get; init; }

    public required Money MustNotSpend { get; init; }

    public required EssentialCoverage Essentials { get; init; }

    public required SpendingSafety Safety { get; init; }

    public required IReadOnlyList<EarnerAllocation> Earners { get; init; }

    public required ReservationPlan Reservations { get; init; }

    public required ContributionSplit SharedSplit { get; init; }

    /// <summary>The proportional result, shown alongside an override so the difference is visible.</summary>
    public ContributionSplit? DefaultSplit { get; init; }

    public required IReadOnlyList<ProtectedAmount> Protections { get; init; }

    public required IReadOnlyList<ReductionApplied> Reductions { get; init; }

    public ShortfallReport? Shortfall { get; init; }

    public required IReadOnlyList<Recommendation> Recommendations { get; init; }

    public required IReadOnlyList<string> Warnings { get; init; }

    public required IReadOnlyList<string> MissingInformation { get; init; }

    public required AllocationLedger Ledger { get; init; }

    public required GuidanceExplanation Explanation { get; init; }

    public Money PersonalDiscretionaryTotal =>
        Money.Sum(Earners.Select(earner => earner.DiscretionaryAllowance)).Round();

    /// <summary>Cash already recorded. Projected deposits are not treated as money in hand.</summary>
    public Money TotalIn => AvailableNow.Round();

    public bool HasShortfall => Shortfall is not null;

    public EarnerAllocation? For(Guid memberId) =>
        Earners.FirstOrDefault(earner => earner.MemberId == memberId);
}

public static class PaychequeAllocator
{
    /// <summary>
    /// Turns one household's records into a single payday's guidance.
    ///
    /// The order matters and is fixed: money in, then obligations by due date, then essentials,
    /// then each person's own remaining money, and only then discretionary spending. Anything the
    /// household cannot cover is reported as a shortfall rather than absorbed to make the columns
    /// agree.
    /// </summary>
    public static PaychequeAllocation Allocate(BudgetDocument document, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(document);

        var rules = document.Rules;
        var hierarchy = document.Hierarchy;
        var basis = rules.Basis;
        var allocationFrequency = AllocationFrequency(document);

        var projection = BuildProjection(document, basis, today);
        var nextPayday = OperationalPositionCalculator.NextConfirmedIncome(document, today)?.Date
                         ?? FindNextPayday(projection, today);
        var validThrough = nextPayday is { } next ? next.AddDays(-1) : PeriodEnd(today, allocationFrequency);

        var availableNow = document.TotalBalance.Round();
        var incomeReceived = DepositsOn(projection, today);
        var incomeExpected = DepositsBetween(projection, today.AddDays(1), validThrough);

        var conservative = basis == IncomeBasis.Conservative
            ? (incomeReceived + incomeExpected).Round()
            : ConservativeIncome(document, today, validThrough);

        var totalIncome = (incomeReceived + incomeExpected).Round();

        var reliableIncome = rules is { Basis: IncomeBasis.Optimistic, OptimisticIncomeMayFundEssentials: false }
            ? Money.Min(totalIncome, conservative)
            : totalIncome;

        var additional = Money.Max(Money.Zero, (totalIncome - reliableIncome).Round());
        var conservativeIn = availableNow.Round();
        var totalIn = availableNow.Round();

        // Obligations are looked at a year ahead so annual bills start reserving in time.
        var paydays = ReservationPlanner.PaydaysBetween(document, today, today.AddYears(1));
        var obligations = ReservationPlanner.ObligationsFrom(document, hierarchy, today, today.AddYears(1));
        var reservations = ReservationPlanner.Plan(obligations, paydays, today, nextPayday);

        var dueFromCash = reservations.Lines
            .Where(line => IsDueFromCashOnHand(line, today, nextPayday))
            .ToList();
        var laterReservationLines = reservations.RequiringContribution
            .Where(line => dueFromCash.All(due => due.Obligation.Id != line.Obligation.Id || due.DueDate != line.DueDate))
            .ToList();

        var billsDueNow = Money.Sum(dueFromCash
            .Where(line => line.Obligation.Kind == ObligationKind.Expense)
            .Select(line => line.Obligation.Remaining)).Round();
        var laterExpenses = SumOf(laterReservationLines, ObligationKind.Expense);
        var minimumDebt = (SumOf(laterReservationLines, ObligationKind.Debt)
                           + Money.Sum(dueFromCash
                               .Where(line => line.Obligation.Kind == ObligationKind.Debt)
                               .Select(line => line.Obligation.Remaining))).Round();
        var requiredSinking = (SumOf(laterReservationLines, ObligationKind.SavingsFund)
                               + Money.Sum(dueFromCash
                                   .Where(line => line.Obligation.Kind == ObligationKind.SavingsFund)
                                   .Select(line => line.Obligation.Remaining))).Round();

        var grocery = GroceryRequirementFor(document, today, allocationFrequency);
        var spending = EssentialSpendingAllowances(document, hierarchy, allocationFrequency);

        var protectedReserves = ProtectedReserves(document);
        var protectedSavings = protectedReserves.Savings;
        var committedFunds = protectedReserves.Committed;

        var safetyBuffer = rules.SafetyBuffer.IsZero
            ? document.Preferences.MinimumBalanceReserve.Round()
            : rules.SafetyBuffer.Round();

        var protectedSupport = SupportForTiers(
            document,
            allocationFrequency,
            nextPayday ?? validThrough,
            NeedTier.Survival,
            NeedTier.Safety,
            NeedTier.Stability);

        var protectedCashCalls = (billsDueNow
                                  + laterExpenses
                                  + minimumDebt
                                  + requiredSinking
                                  + grocery.EssentialCash
                                  + grocery.OptionalCash
                                  + spending.Transport
                                  + spending.Other
                                  + protectedSupport).Round();

        var mustNotSpend = (protectedCashCalls + protectedSavings + committedFunds + safetyBuffer).Round();
        var essentialRequired = mustNotSpend;
        var essentialFunded = Money.Min(essentialRequired, availableNow);
        var essentialUnfunded = Money.Max(Money.Zero, (essentialRequired - essentialFunded).Round());
        var conservativeRemainder = Money.Max(Money.Zero, (availableNow - essentialRequired).Round());
        var safeToSpend = essentialUnfunded.IsZero
            ? conservativeRemainder
            : Money.Zero;

        var essentials = new EssentialCoverage
        {
            Required = essentialRequired,
            Available = availableNow,
            Funded = essentialFunded,
            Unfunded = essentialUnfunded,
            Explanation =
                $"Essential obligations required {essentialRequired.ToDisplayString()}. " +
                $"Conservative money available {conservativeIn.ToDisplayString()}. " +
                $"Essential obligations funded {essentialFunded.ToDisplayString()}. " +
                $"Visible unresolved shortfall {essentialUnfunded.ToDisplayString()}."
        };

        var earnerIncomes = EarnerIncomeCalculator.ForHousehold(
            document,
            basis,
            allocationFrequency,
            today,
            new PayPeriod(today, validThrough, today));

        var individualTotal = Money.Sum(earnerIncomes.Select(income => income.IndividualObligations)).Round();
        var register = ObligationRegister.Build(document, today);
        var unassignedStill = BillAssignmentPlanner.UnassignedRemaining(document, register);
        var householdAccountStill = Money.Sum(register.Lines
            .Where(line =>
            {
                var expense = document.Expenses.FirstOrDefault(item => item.Id == line.Id);
                return expense is { Assignment: BillAssignment.SharedAccount };
            })
            .Select(line => line.StillRequired)).Round();
        var sharedObligations = Money.Max(
            Money.Zero,
            (protectedCashCalls - individualTotal - unassignedStill - householdAccountStill).Round());

        var eligible = earnerIncomes.Where(income => income.IsDiscretionaryEligible).ToList();

        var splitInput = earnerIncomes
            .Select(income => (income.MemberId, income.MemberName, income.UsableNetIncome))
            .ToList();

        var defaultSplit = AllocationReconciler.Split(
            sharedObligations,
            splitInput,
            rules.RoundingRemainderMemberId);

        var sharedSplit = rules.SharedContributionOverrides.Count > 0
            ? AllocationReconciler.Split(
                sharedObligations,
                splitInput,
                rules.RoundingRemainderMemberId,
                rules.SharedContributionOverrides)
            : defaultSplit;

        var reductions = new List<ReductionApplied>();
        var leftover = Money.Max(Money.Zero, safeToSpend);

        var familySupport = SupportForTiers(
            document,
            allocationFrequency,
            validThrough,
            NeedTier.FamilyAndBelonging);
        var growthSupport = SupportForTiers(
            document,
            allocationFrequency,
            validThrough,
            NeedTier.Growth);

        var goalsRequested = (LowerPriorityGoalRequirement(document, today, allocationFrequency) + growthSupport).Round();
        var requestedEntertainment = (rules.SharedEntertainmentPerPayday + familySupport).Round();
        var requestedFlexible = rules.FlexibleSavingsPerPayday.Round();
        var provisionalDiscretionary = RequestedDiscretionary(
            document,
            earnerIncomes,
            eligible,
            sharedSplit,
            rules);
        var requestedDiscretionary = Money.Sum(provisionalDiscretionary.Values).Round();

        var slice = rules.UsesCustomFundingOrder
            ? AllocationOrder.AssignByFundingOrder(
                leftover,
                requestedEntertainment,
                goalsRequested,
                requestedDiscretionary,
                requestedFlexible,
                rules.FundingOrder)
            : AllocationOrder.FillConfiguredOrReduce(
                leftover,
                requestedEntertainment,
                goalsRequested,
                requestedDiscretionary,
                requestedFlexible,
                rules.ReductionOrder);

        RecordCut(requestedEntertainment, slice.Entertainment, ReducibleBucket.SharedEntertainment, reductions);
        RecordCut(goalsRequested, slice.Goals, ReducibleBucket.LowerPriorityGoals, reductions);
        RecordCut(requestedDiscretionary, slice.Discretionary, ReducibleBucket.OptionalDiscretionary, reductions);
        RecordCut(requestedFlexible, slice.FlexibleSavings, ReducibleBucket.FlexibleSavings, reductions);
        RecordCut(
            Money.Max(Money.Zero, (leftover - requestedEntertainment - goalsRequested - requestedDiscretionary - requestedFlexible).Round()),
            slice.Surplus,
            ReducibleBucket.UnallocatedSurplus,
            reductions);

        var funded = new FundedBuckets(
            slice.FlexibleSavings,
            slice.Entertainment,
            slice.Goals,
            slice.Discretionary,
            slice.Surplus);

        var earners = BuildEarnerAllocations(
            document,
            earnerIncomes,
            sharedSplit,
            rules,
            today,
            validThrough,
            funded.Discretionary,
            provisionalDiscretionary);

        var personalTotal = Money.Sum(earners.Select(earner => earner.DiscretionaryAllowance)).Round();
        var surplus = funded.Surplus;

        var surplusApplied = ApplySurplusRules(rules, additional, surplus);

        var protections = BuildProtections(
            reservations,
            grocery,
            spending,
            protectedSavings,
            committedFunds,
            safetyBuffer,
            requiredSinking);

        if (!protectedSupport.IsZero)
        {
            protections = protections
                .Append(new ProtectedAmount
                {
                    Label = "Configured international support",
                    Amount = protectedSupport,
                    Tier = NeedTier.Stability,
                    ProtectedBy = "the household-chosen hierarchy tier for the transfer",
                    Reason = "This reserve is required by a configured support commitment, not by an automatic classification."
                })
                .ToList();
        }

        var missing = CollectMissingInformation(document, earnerIncomes, grocery, today);
        var warnings = CollectWarnings(document, reservations, grocery, rules, today);
        AddInternationalNotes(document, missing, warnings);

        var shortfall = BuildShortfall(
            conservativeIn,
            mustNotSpend,
            reservations,
            projection,
            reductions,
            today,
            essentials.Unfunded);

        var safety = Classify(safeToSpend, safetyBuffer, reservations, projection, missing, reductions, shortfall);

        var ledger = BuildLedger(
            totalIn,
            billsDueNow,
            laterExpenses,
            minimumDebt,
            requiredSinking,
            grocery,
            spending,
            protectedSavings,
            committedFunds,
            safetyBuffer,
            funded.FlexibleSavings,
            funded.Entertainment,
            funded.Goals,
            personalTotal,
            surplusApplied.Unallocated,
            shortfall);

        var explanation = BuildExplanation(
            document,
            basis,
            today,
            validThrough,
            nextPayday,
            availableNow,
            totalIncome,
            reliableIncome,
            mustNotSpend,
            safeToSpend,
            reservations,
            sharedSplit,
            grocery,
            missing);

        var recommendations = BuildRecommendations(
            reservations,
            grocery,
            safeToSpend,
            mustNotSpend,
            earners,
            shortfall,
            missing,
            explanation,
            safety);

        return new PaychequeAllocation
        {
            Today = today,
            NextPayday = nextPayday,
            ValidThrough = validThrough,
            Basis = basis,
            AvailableNow = availableNow,
            IncomeReceived = incomeReceived,
            IncomeExpectedBeforeNextPayday = incomeExpected,
            ReliableIncome = reliableIncome,
            AdditionalIncomeAboveBaseline = additional,
            BillsDueBeforeNextPayday = billsDueNow,
            ReservationsForLaterBills = laterExpenses,
            EssentialGroceries = grocery.EssentialCash,
            OptionalGroceries = grocery.OptionalCash,
            EssentialTransport = spending.Transport,
            OtherEssentialSpending = spending.Other,
            MinimumDebtPayments = minimumDebt,
            RequiredSinkingFunds = requiredSinking,
            ProtectedSavings = protectedSavings,
            CommittedFunds = committedFunds,
            SafetyBuffer = safetyBuffer,
            FlexibleSavings = funded.FlexibleSavings,
            SharedEntertainment = funded.Entertainment,
            LowerPriorityGoals = funded.Goals,
            UnallocatedSurplus = surplusApplied.Unallocated,
            SafeToSpend = safeToSpend,
            MustNotSpend = mustNotSpend,
            Essentials = essentials,
            Safety = safety,
            Earners = earners,
            Reservations = reservations,
            SharedSplit = sharedSplit,
            DefaultSplit = ReferenceEquals(sharedSplit, defaultSplit) ? null : defaultSplit,
            Protections = protections,
            Reductions = reductions,
            Shortfall = shortfall,
            Recommendations = recommendations,
            Warnings = warnings,
            MissingInformation = missing,
            Ledger = ledger,
            Explanation = explanation
        };
    }

    /// <summary>
    /// Classifies a possible purchase against the current allocation, naming what it would harm.
    /// </summary>
    public static (SpendingSafety Safety, string Explanation) Classify(
        PaychequeAllocation allocation,
        Money amount,
        string? categoryName = null)
    {
        ArgumentNullException.ThrowIfNull(allocation);

        if (allocation.Safety == SpendingSafety.InsufficientInformation)
        {
            return (SpendingSafety.InsufficientInformation,
                "Insufficient information: " + string.Join(" ", allocation.MissingInformation));
        }

        var label = categoryName is null ? "This spending" : $"Spending on {categoryName}";
        var rounded = amount.Round();

        if (rounded > allocation.SafeToSpend)
        {
            var over = (rounded - allocation.SafeToSpend).Round();
            var harmed = allocation.Protections.FirstOrDefault();

            var because = harmed is null
                ? "there is not enough left after protected obligations."
                : $"it would take {over.ToDisplayString()} from money held for {harmed.ProtectedBy}.";

            return (SpendingSafety.Unsafe,
                $"{label} of {rounded.ToDisplayString()} is {over.ToDisplayString()} more than the " +
                $"{allocation.SafeToSpend.ToDisplayString()} that is safe to spend, because {because}");
        }

        var discretionary = (allocation.SharedEntertainment
                             + allocation.PersonalDiscretionaryTotal
                             + allocation.UnallocatedSurplus).Round();

        if (rounded > discretionary)
        {
            var into = (rounded - discretionary).Round();

            return (SpendingSafety.NotRecommended,
                $"{label} of {rounded.ToDisplayString()} is within safe-to-spend, but " +
                $"{into.ToDisplayString()} of it would come out of flexible savings or a lower-priority " +
                "goal rather than money set aside for spending.");
        }

        if (rounded > allocation.UnallocatedSurplus)
        {
            var margin = (rounded - allocation.UnallocatedSurplus).Round();

            return (SpendingSafety.Caution,
                $"{label} of {rounded.ToDisplayString()} fits, but {margin.ToDisplayString()} of it uses " +
                "entertainment or personal allowance rather than spare money, so the margin is smaller " +
                "for the rest of the week.");
        }

        return (SpendingSafety.Safe,
            $"{label} of {rounded.ToDisplayString()} fits within the " +
            $"{allocation.UnallocatedSurplus.ToDisplayString()} of unallocated money, so no protected " +
            "need, reserve or buffer is affected.");
    }

    #region Income and dates

    /// <summary>The frequency the household is actually paid at, which the guidance period follows.</summary>
    public static Frequency AllocationFrequency(BudgetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var frequencies = document.IncomeSources
            .Where(source => source.IsActive && source.PayFrequency.IsRecurring())
            .GroupBy(source => source.PayFrequency)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .ToList();

        return frequencies.Count == 0 ? Frequency.Weekly : frequencies[0].Key;
    }

    private static DateOnly PeriodEnd(DateOnly today, Frequency frequency) => frequency switch
    {
        Frequency.Weekly => today.AddDays(6),
        Frequency.Fortnightly => today.AddDays(13),
        Frequency.TwiceMonthly => today.AddDays(14),
        Frequency.Monthly => today.AddDays(29),
        _ => today.AddDays(6)
    };

    private static CashFlowProjection BuildProjection(
        BudgetDocument document,
        IncomeBasis basis,
        DateOnly today)
    {
        var estimate = basis.ToEstimate();
        var takeHome = Budgeting.TakeHomeCalculator.From(document, estimate, today);

        return CashFlowProjector.Project(new CashFlowInputs
        {
            From = today,
            To = today.AddDays(120),
            StartingBalance = document.TotalBalance,
            IncomeSources = document.IncomeSources,
            NetPayPerPeriod = takeHome.NetPayPerPeriod,
            Expenses = document.Expenses,
            Payslips = document.Payslips,
            Assumption = estimate
        });
    }

    private static DateOnly? FindNextPayday(CashFlowProjection projection, DateOnly today) =>
        projection.Days
            .Where(day => day.Date > today)
            .Where(day => day.Events.Any(item => item.Direction == CashFlowDirection.Deposit))
            .Select(day => (DateOnly?)day.Date)
            .FirstOrDefault();

    private static Money DepositsOn(CashFlowProjection projection, DateOnly date) =>
        Money.Sum(projection.Days
            .Where(day => day.Date == date)
            .Select(day => day.Deposits)).Round();

    private static Money DepositsBetween(CashFlowProjection projection, DateOnly from, DateOnly to) =>
        to < from
            ? Money.Zero
            : Money.Sum(projection.Days
                .Where(day => day.Date >= from && day.Date <= to)
                .Select(day => day.Deposits)).Round();

    private static Money ConservativeIncome(BudgetDocument document, DateOnly today, DateOnly through)
    {
        var projection = BuildProjection(document, IncomeBasis.Conservative, today);

        return DepositsBetween(projection, today, through);
    }

    #endregion

    #region Essentials

    private sealed record EssentialSpending(Money Transport, Money Other, IReadOnlyList<string> Names);

    /// <summary>
    /// Variable essential costs are weekly allowances rather than dated bills, so they are
    /// budgeted here instead of being reserved against a due date they do not have.
    /// </summary>
    private static EssentialSpending EssentialSpendingAllowances(
        BudgetDocument document,
        NeedsHierarchy hierarchy,
        Frequency allocationFrequency)
    {
        var transport = Money.Zero;
        var other = Money.Zero;
        var names = new List<string>();

        foreach (var expense in document.Expenses)
        {
            if (expense.IsPaused || ReservationPlanner.IsDatedObligation(expense))
            {
                continue;
            }

            if (string.Equals(
                    expense.Category.Name,
                    ExpenseCategory.Groceries.Name,
                    StringComparison.OrdinalIgnoreCase))
            {
                // Groceries are planned by category on the Grocery Plan page.
                continue;
            }

            if (expense.Necessity != ExpenseNecessity.Essential)
            {
                continue;
            }

            if (!expense.Frequency.IsRecurring())
            {
                continue;
            }

            var amount = FrequencyConverter
                .Convert(expense.ExpectedAmount, expense.Frequency, allocationFrequency)
                .Round();

            var tier = hierarchy.TierFor(expense);

            if (tier == NeedTier.Safety
                && string.Equals(
                    expense.Category.Name,
                    ExpenseCategory.Transport.Name,
                    StringComparison.OrdinalIgnoreCase))
            {
                transport += amount;
            }
            else
            {
                other += amount;
            }

            names.Add(expense.Name);
        }

        return new EssentialSpending(transport.Round(), other.Round(), names);
    }

    private static GroceryRequirement GroceryRequirementFor(
        BudgetDocument document,
        DateOnly today,
        Frequency allocationFrequency)
    {
        var plan = document.GroceryPlanOf(GroceryPlanKind.Current);

        if (plan is null)
        {
            return new GroceryRequirement
            {
                Kind = GroceryPlanKind.Current,
                EssentialCash = Money.Zero,
                OptionalCash = Money.Zero,
                CoveredByAssistance = Money.Zero,
                CategoriesRequiringCash = [],
                CategoriesSuppliedByAssistance = [],
                Warnings = ["No grocery plan has been set up, so no food money has been set aside."]
            };
        }

        var weekly = GroceryPlanner.Require(plan, today);

        if (allocationFrequency == Frequency.Weekly)
        {
            return weekly;
        }

        Money Scale(Money amount) =>
            FrequencyConverter.Convert(amount, Frequency.Weekly, allocationFrequency).Round();

        return weekly with
        {
            EssentialCash = Scale(weekly.EssentialCash),
            OptionalCash = Scale(weekly.OptionalCash),
            CoveredByAssistance = Scale(weekly.CoveredByAssistance)
        };
    }

    private static (Money Savings, Money Committed) ProtectedReserves(BudgetDocument document)
    {
        var savings = Money.Zero;
        var committed = Money.Zero;

        foreach (var reserve in document.Reserves.Where(reserve => reserve.IsProtected))
        {
            if (reserve.Kind is ObligationKind.SavingsFund or ObligationKind.Goal)
            {
                savings += reserve.Reserved;
            }
            else
            {
                committed += reserve.Reserved;
            }
        }

        return (savings.Round(), committed.Round());
    }

    private static bool IsDueFromCashOnHand(
        ReservationLine line,
        DateOnly today,
        DateOnly? nextPayday)
    {
        if (line.Obligation.Remaining.IsZero)
        {
            return false;
        }

        if (line.Status is ReservationStatus.DueBeforeNextPayday
            or ReservationStatus.Overdue
            or ReservationStatus.Underfunded)
        {
            return true;
        }

        if (nextPayday is { } next && line.DueDate < next)
        {
            return true;
        }

        return line.Obligation.IsEssential && line.DueDate <= today.AddDays(7);
    }

    private static Money SumOf(IEnumerable<ReservationLine> lines, ObligationKind kind) =>
        Money.Sum(lines
            .Where(line => line.Obligation.Kind == kind)
            .Select(line => line.RequiredFromThisPaycheque)).Round();

    private static Money SupportForTiers(
        BudgetDocument document,
        Frequency allocationFrequency,
        DateOnly validThrough,
        params NeedTier[] tiers)
    {
        var allowed = tiers.ToHashSet();

        return Money.Sum(document.SupportCommitments
            .Where(commitment => commitment.HierarchyTier is { } tier && allowed.Contains(tier))
            .Select(commitment => ReserveThisPeriod(commitment, allocationFrequency, validThrough))).Round();
    }

    private static Money ReserveThisPeriod(
        RecurringSupportCommitment commitment,
        Frequency allocationFrequency,
        DateOnly validThrough)
    {
        var reserve = commitment.EstimatedUsdReserve();

        if (commitment.Frequency == Frequency.OneOff)
        {
            if (commitment.DueDate is { } due && due > validThrough)
            {
                return Money.Zero;
            }

            return reserve;
        }

        return FrequencyConverter.Convert(reserve, commitment.Frequency, allocationFrequency).Round();
    }

    private static void AddInternationalNotes(
        BudgetDocument document,
        List<string> missing,
        List<string> warnings)
    {
        if (document.SupportCommitments.Any(commitment => commitment.HierarchyTier is null))
        {
            missing.Add(
                "Insufficient information: an international support commitment has no hierarchy tier. " +
                "It is not automatically treated as essential or discretionary.");
        }

        if (document.InternationalTransfers.Any(transfer => transfer.ExcludedFromConfidentSafeToSpend))
        {
            warnings.Add(
                "Some international money is excluded from confident safe-to-spend because its purpose " +
                "is unknown, it is a same-owner movement, or its classification is incomplete.");
        }

        foreach (var transfer in document.InternationalTransfers)
        {
            warnings.AddRange(SouthAfricanReview.FlagsFor(transfer));
        }
    }

    private static Money LowerPriorityGoalRequirement(
        BudgetDocument document,
        DateOnly today,
        Frequency allocationFrequency) =>
        Money.Sum(document.Goals
            .Where(goal => goal.Priority is GoalPriority.Low or GoalPriority.Medium)
            .Where(goal => !goal.IsComplete)
            .Select(goal => goal.RequiredContribution(today, allocationFrequency))).Round();

    #endregion

    #region Reducible buckets

    private sealed record FundedBuckets(
        Money FlexibleSavings,
        Money Entertainment,
        Money Goals,
        Money Discretionary,
        Money Surplus);

    private static void RecordCut(
        Money requested,
        Money allocated,
        ReducibleBucket bucket,
        List<ReductionApplied> reductions)
    {
        if (allocated >= requested)
        {
            return;
        }

        reductions.Add(new ReductionApplied
        {
            Bucket = bucket,
            Requested = requested,
            Allocated = allocated,
            Explanation =
                $"{bucket.ToDisplayName()} was reduced from {requested.ToDisplayString()} to " +
                $"{allocated.ToDisplayString()} because the money was needed for a higher priority."
        });
    }

    private static (Money Unallocated, IReadOnlyList<string> Notes) ApplySurplusRules(
        AllocationRules rules,
        Money additional,
        Money surplus)
    {
        if (rules.SurplusRules.Count == 0 || additional.IsZero || surplus.IsZero)
        {
            return (surplus, []);
        }

        var directed = Money.Zero;
        var notes = new List<string>();

        foreach (var rule in rules.SurplusRules.Where(rule => rule.Target != SurplusTarget.Unallocated))
        {
            var share = Money.Min(surplus, (additional * (rule.Percent / 100m)).Round());

            if (share.IsZero)
            {
                continue;
            }

            directed += share;
            notes.Add($"{share.ToDisplayString()} of additional income directed to {rule.Target}.");
        }

        return (Money.Max(Money.Zero, (surplus - directed).Round()), notes);
    }

    #endregion

    #region Earners

    private static Dictionary<Guid, Money> RequestedDiscretionary(
        BudgetDocument document,
        IReadOnlyList<EarnerIncome> incomes,
        IReadOnlyList<EarnerIncome> eligible,
        ContributionSplit split,
        AllocationRules rules)
    {
        var provisional = new Dictionary<Guid, Money>();

        foreach (var income in incomes)
        {
            var share = split.Shares.FirstOrDefault(item => item.MemberId == income.MemberId);
            var shareAmount = share?.Amount ?? Money.Zero;
            var savings = SavingsShare(document, income);
            var remainingBefore = Money.Max(
                Money.Zero,
                (income.UsableNetIncome - shareAmount - savings).Round());

            provisional[income.MemberId] = income.IsDiscretionaryEligible
                ? DiscretionaryFor(rules, income, eligible, remainingBefore)
                : Money.Zero;
        }

        return provisional;
    }

    private static List<EarnerAllocation> BuildEarnerAllocations(
        BudgetDocument document,
        IReadOnlyList<EarnerIncome> incomes,
        ContributionSplit split,
        AllocationRules rules,
        DateOnly today,
        DateOnly validThrough,
        Money discretionaryCap,
        IReadOnlyDictionary<Guid, Money> provisional)
    {
        var allocations = new List<EarnerAllocation>();
        var requested = Money.Sum(provisional.Values).Round();
        var scale = 1m;

        if (requested > discretionaryCap && !requested.IsZero)
        {
            scale = discretionaryCap.Amount / requested.Amount;
        }

        foreach (var income in incomes)
        {
            var share = split.Shares.FirstOrDefault(item => item.MemberId == income.MemberId);
            var shareAmount = share?.Amount ?? Money.Zero;
            var savings = SavingsShare(document, income);

            var remainingBefore = Money.Max(
                Money.Zero,
                (income.UsableNetIncome - shareAmount - savings).Round());

            var discretionary = (provisional[income.MemberId] * scale).Round();

            var spent = PersonalSpending(document, income.MemberId, today, validThrough);
            var sent = TransfersFrom(document, income.MemberId, today, validThrough);
            var received = TransfersTo(document, income.MemberId, today, validThrough);

            allocations.Add(new EarnerAllocation
            {
                Income = income,
                ContributionPercent = share?.Percent ?? 0m,
                ShareOfSharedObligations = shareAmount,
                SavingsContribution = savings,
                RemainingBeforeDiscretionary = remainingBefore,
                DiscretionaryAllowance = discretionary,
                PersonalSpendingUsed = spent,
                TransfersSent = sent,
                TransfersReceived = received,
                CarriesRoundingRemainder = share?.CarriesRoundingRemainder ?? false,
                Explanation = ExplainEarner(income, shareAmount, savings, remainingBefore, discretionary, rules)
            });
        }

        return allocations;
    }

    private static Money DiscretionaryFor(
        AllocationRules rules,
        EarnerIncome income,
        IReadOnlyList<EarnerIncome> eligible,
        Money remainingBefore) => rules.DiscretionaryMethod switch
        {
            DiscretionaryMethod.None => Money.Zero,

            DiscretionaryMethod.AllRemainingPersonalMoney => remainingBefore,

            DiscretionaryMethod.FixedAmountEach =>
                Money.Min(rules.FixedDiscretionaryAmount.Round(), remainingBefore),

            DiscretionaryMethod.CustomPercentages =>
                (remainingBefore * (rules.PercentFor(income.MemberId) / 100m)).Round(),

            DiscretionaryMethod.ProportionalToUsableNetIncome =>
                ProportionalDiscretionary(rules, income, eligible),

            // The default: the same percentage of each person's own remaining money, so nobody's
            // allowance is taken from a pooled balance the other person also contributed to.
            _ => (remainingBefore * (rules.DiscretionaryPercent / 100m)).Round()
        };

    private static Money ProportionalDiscretionary(
        AllocationRules rules,
        EarnerIncome income,
        IReadOnlyList<EarnerIncome> eligible)
    {
        var combined = Money.Sum(eligible.Select(item => item.UsableNetIncome)).Round();

        if (combined.IsZero)
        {
            return Money.Zero;
        }

        var pot = (combined * (rules.DiscretionaryPercent / 100m)).Round();
        var weights = eligible.Select(item => item.UsableNetIncome.Amount).ToList();

        if (weights.Sum() <= 0m)
        {
            return Money.Zero;
        }

        var shares = pot.AllocateByWeight(weights);
        var index = eligible.ToList().FindIndex(item => item.MemberId == income.MemberId);

        return index < 0 ? Money.Zero : shares[index];
    }

    /// <summary>
    /// The savings this person has agreed to fund from their own money. Shared funds are already
    /// covered by their share of the household's obligations, so only funds they own are counted
    /// here; counting both would charge them twice for the same savings.
    /// </summary>
    private static Money SavingsShare(BudgetDocument document, EarnerIncome income) =>
        Money.Sum(document.Funds
            .Where(fund => fund.OwnerMemberId == income.MemberId)
            .Select(fund => FrequencyConverter.Convert(
                fund.PlannedContribution,
                fund.ContributionFrequency,
                income.PayFrequency))).Round();

    private static Money PersonalSpending(
        BudgetDocument document,
        Guid memberId,
        DateOnly from,
        DateOnly to) =>
        Money.Max(Money.Zero, Money.Sum(document.Transactions
            .Where(transaction => transaction.SpentByMemberId == memberId)
            .Where(transaction => transaction.Date >= from && transaction.Date <= to)
            .Select(transaction => transaction.SignedAmount)).Round());

    private static Money TransfersFrom(
        BudgetDocument document,
        Guid memberId,
        DateOnly from,
        DateOnly to) =>
        Money.Sum(document.Transfers
            .Where(transfer => transfer.FromMemberId == memberId)
            .Where(transfer => AppliesInWindow(transfer, from, to))
            .Select(transfer => transfer.Amount)).Round();

    private static Money TransfersTo(
        BudgetDocument document,
        Guid memberId,
        DateOnly from,
        DateOnly to) =>
        Money.Sum(document.Transfers
            .Where(transfer => transfer.ToMemberId == memberId)
            .Where(transfer => AppliesInWindow(transfer, from, to))
            .Select(transfer => transfer.Amount)).Round();

    private static bool AppliesInWindow(PersonalTransfer transfer, DateOnly from, DateOnly to)
    {
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            if (transfer.AppliesOn(date))
            {
                return true;
            }
        }

        return false;
    }

    private static string ExplainEarner(
        EarnerIncome income,
        Money share,
        Money savings,
        Money remaining,
        Money discretionary,
        AllocationRules rules)
    {
        if (!income.IsDiscretionaryEligible)
        {
            return $"{income.MemberName} is not set up for a personal discretionary allowance, so none " +
                   "has been calculated. Their income still contributes to shared household costs.";
        }

        var method = rules.DiscretionaryMethod switch
        {
            DiscretionaryMethod.SamePercentageOfOwnRemaining =>
                $"{rules.DiscretionaryPercent:0.##}% of their own remaining money",
            DiscretionaryMethod.CustomPercentages =>
                $"{rules.PercentFor(income.MemberId):0.##}% of their own remaining money",
            DiscretionaryMethod.FixedAmountEach =>
                $"a fixed {rules.FixedDiscretionaryAmount.ToDisplayString()}",
            DiscretionaryMethod.ProportionalToUsableNetIncome =>
                "a share of the household discretionary pot, in proportion to usable net income",
            DiscretionaryMethod.AllRemainingPersonalMoney => "everything they have left",
            _ => "no discretionary allocation this period"
        };

        return $"{income.MemberName} has {income.UsableNetIncome.ToDisplayString()} of usable net income. " +
               $"After {share.ToDisplayString()} towards shared obligations and " +
               $"{savings.ToDisplayString()} of savings, {remaining.ToDisplayString()} is left. " +
               $"Their allowance of {discretionary.ToDisplayString()} is {method}. " +
               "It comes from their own income and is not affected by anyone else's spending.";
    }

    #endregion

    #region Explanations, warnings and shortfalls

    private static List<ProtectedAmount> BuildProtections(
        ReservationPlan reservations,
        GroceryRequirement grocery,
        EssentialSpending spending,
        Money protectedSavings,
        Money committedFunds,
        Money safetyBuffer,
        Money requiredSinking)
    {
        var protections = new List<ProtectedAmount>();

        foreach (var line in reservations.DueBeforeNextPayday.Concat(reservations.RequiringContribution))
        {
            if (line.RequiredFromThisPaycheque.IsZero)
            {
                continue;
            }

            protections.Add(new ProtectedAmount
            {
                Label = line.Name,
                Amount = line.RequiredFromThisPaycheque,
                Tier = line.Tier,
                ProtectedBy = $"{line.Name} due {line.DueDate:yyyy-MM-dd}",
                Date = line.DueDate,
                Reason = line.Explanation
            });
        }

        if (!grocery.EssentialCash.IsZero)
        {
            protections.Add(new ProtectedAmount
            {
                Label = "Essential groceries",
                Amount = grocery.EssentialCash,
                Tier = NeedTier.Survival,
                ProtectedBy = "essential food for the household",
                Reason =
                    $"{grocery.EssentialCash.ToDisplayString()} covers the essential grocery categories " +
                    $"that still need cash: {string.Join(", ", grocery.CategoriesRequiringCash)}."
            });
        }

        if (!spending.Transport.IsZero)
        {
            protections.Add(new ProtectedAmount
            {
                Label = "Essential transport",
                Amount = spending.Transport,
                Tier = NeedTier.Safety,
                ProtectedBy = "getting to work",
                Reason = "Transport to work is protected before discretionary spending."
            });
        }

        if (!requiredSinking.IsZero)
        {
            protections.Add(new ProtectedAmount
            {
                Label = "Required sinking funds",
                Amount = requiredSinking,
                Tier = NeedTier.Stability,
                ProtectedBy = "dated savings targets",
                Reason = "These contributions are what stop an irregular bill becoming an emergency."
            });
        }

        if (!protectedSavings.IsZero)
        {
            protections.Add(new ProtectedAmount
            {
                Label = "Protected savings already held",
                Amount = protectedSavings,
                Tier = NeedTier.Stability,
                ProtectedBy = "savings the household marked as protected",
                Reason = "This money is in the account but is already spoken for."
            });
        }

        if (!committedFunds.IsZero)
        {
            protections.Add(new ProtectedAmount
            {
                Label = "Committed funds already held",
                Amount = committedFunds,
                Tier = NeedTier.Stability,
                ProtectedBy = "bills and debts already reserved for",
                Reason = "This money has already been set aside against a named obligation."
            });
        }

        if (!safetyBuffer.IsZero)
        {
            protections.Add(new ProtectedAmount
            {
                Label = "Household safety buffer",
                Amount = safetyBuffer,
                Tier = NeedTier.Stability,
                ProtectedBy = "the household's chosen minimum balance",
                Reason = "The buffer absorbs the cost nobody saw coming."
            });
        }

        return protections
            .OrderBy(item => item.Tier)
            .ThenBy(item => item.Date ?? DateOnly.MaxValue)
            .ToList();
    }

    private static List<string> CollectMissingInformation(
        BudgetDocument document,
        IReadOnlyList<EarnerIncome> incomes,
        GroceryRequirement grocery,
        DateOnly today)
    {
        var missing = new List<string>();

        foreach (var note in incomes.SelectMany(income => income.MissingInformation))
        {
            missing.Add($"Insufficient information: {note}");
        }

        if (document.Accounts.Count == 0)
        {
            missing.Add(
                "Insufficient information: no account balance has been entered, so the money actually " +
                "available is unknown.");
        }
        else
        {
            var stale = document.Accounts
                .Where(account => account.UpdatedOn < today.AddDays(-30))
                .ToList();

            foreach (var account in stale)
            {
                missing.Add(
                    $"Insufficient information: the balance for {account.Name} was last confirmed on " +
                    $"{account.UpdatedOn:yyyy-MM-dd} and may be out of date.");
            }
        }

        if (document.IncomeSources.Count == 0)
        {
            missing.Add("Insufficient information: no income has been entered.");
        }

        if (document.GroceryPlanOf(GroceryPlanKind.Current) is null)
        {
            missing.Add(
                "Insufficient information: no grocery plan has been set up, so no food money is being " +
                "protected.");
        }

        foreach (var warning in grocery.Warnings)
        {
            missing.Add(warning);
        }

        foreach (var question in document.OutstandingQuestions)
        {
            if (!string.IsNullOrWhiteSpace(question))
            {
                missing.Add(question.Trim());
            }
        }

        return missing.Distinct(StringComparer.Ordinal).ToList();
    }

    private static List<string> CollectWarnings(
        BudgetDocument document,
        ReservationPlan reservations,
        GroceryRequirement grocery,
        AllocationRules rules,
        DateOnly today)
    {
        var warnings = new List<string>();

        foreach (var line in reservations.Short)
        {
            warnings.Add(
                $"{line.Name} is short by {line.Shortfall.ToDisplayString()} for " +
                $"{line.DueDate:yyyy-MM-dd}.");
        }

        var outdated = CostGuidanceLibrary.NeedingReview(document.CostGuidance, today);

        foreach (var record in outdated.Take(5))
        {
            warnings.Add(
                $"The {record.Locality.Describe()} guidance for {record.Category} requires review.");
        }

        if (outdated.Count > 5)
        {
            warnings.Add($"{outdated.Count - 5} further local guidance records also require review.");
        }

        var current = document.GroceryPlanOf(GroceryPlanKind.Current);

        if (current is not null && current.Assistance.AppliesOn(today))
        {
            var fallback = document.GroceryPlanOf(GroceryPlanKind.FallbackWithoutAssistance);
            var (additional, explanation) = GroceryPlanner.IfAssistanceEnds(current, fallback, today);

            if (!additional.IsZero)
            {
                warnings.Add(explanation);
            }
        }

        if (current is { CashAmountNeedsConfirmation: true })
        {
            warnings.Add(
                "The cash grocery figure still needs confirmation that it excludes tobacco and alcohol.");
        }

        if (rules is { DiscretionaryMethod: DiscretionaryMethod.SamePercentageOfOwnRemaining, DiscretionaryPercentConfigured: false })
        {
            warnings.Add(
                "A personal discretionary percentage has not been chosen. No allowance is being allocated.");
        }

        foreach (var question in document.OutstandingQuestions)
        {
            if (!string.IsNullOrWhiteSpace(question))
            {
                warnings.Add(question.Trim());
            }
        }

        if (rules is { Basis: IncomeBasis.Optimistic, OptimisticIncomeMayFundEssentials: true })
        {
            warnings.Add(
                "Essential obligations are being funded from optimistic income. If the extra hours do " +
                "not happen, the shortfall falls on protected needs.");
        }

        warnings.AddRange(grocery.Warnings);

        return warnings.Distinct(StringComparer.Ordinal).ToList();
    }

    private static ShortfallReport? BuildShortfall(
        Money conservativeIn,
        Money mustNotSpend,
        ReservationPlan reservations,
        CashFlowProjection projection,
        IReadOnlyList<ReductionApplied> reductions,
        DateOnly today,
        Money essentialUnfunded)
    {
        var deficit = Money.Max(essentialUnfunded, (mustNotSpend - conservativeIn).Round());
        var underfunded = reservations.Short;

        if (deficit <= Money.Zero && underfunded.Count == 0)
        {
            return null;
        }

        var total = Money.Max(deficit, Money.Sum(underfunded.Select(line => line.Shortfall)).Round());

        var affected = underfunded
            .Select(line => new ShortfallItem
            {
                Name = line.Name,
                AmountMissing = line.Shortfall,
                Date = line.DueDate,
                Tier = line.Tier
            })
            .OrderBy(item => item.Date)
            .ToList();

        var occursOn = affected.Count > 0
            ? affected[0].Date
            : projection.FirstNegativeDate ?? today;

        return new ShortfallReport
        {
            TotalShortfall = total,
            OccursOn = occursOn,
            Affected = affected,
            EarliestNegativeBalance = projection.FirstNegativeDate,
            AlreadyReduced = reductions.ToList(),
            UnresolvedDeficit = Money.Max(Money.Zero, deficit),
            Explanation =
                $"The household is projected to run short by {total.ToDisplayString()} on " +
                $"{occursOn:yyyy-MM-dd}. " +
                (reductions.Count == 0
                    ? "No lower-priority allocations were available to reduce."
                    : $"{reductions.Count} lower-priority allocation(s) have already been reduced.") +
                (projection.FirstNegativeDate is { } negative
                    ? $" The projected balance first goes below zero on {negative:yyyy-MM-dd}."
                    : string.Empty)
        };
    }

    private static SpendingSafety Classify(
        Money safeToSpend,
        Money safetyBuffer,
        ReservationPlan reservations,
        CashFlowProjection projection,
        IReadOnlyList<string> missing,
        IReadOnlyList<ReductionApplied> reductions,
        ShortfallReport? shortfall)
    {
        if (missing.Count > 0)
        {
            return SpendingSafety.InsufficientInformation;
        }

        if (safeToSpend.IsNegative || projection.GoesNegative || shortfall is { UnresolvedDeficit.IsZero: false })
        {
            return SpendingSafety.Unsafe;
        }

        if (reservations.Short.Count > 0)
        {
            return SpendingSafety.NotRecommended;
        }

        if (reductions.Count > 0 || safeToSpend < safetyBuffer)
        {
            return SpendingSafety.Caution;
        }

        return SpendingSafety.Safe;
    }

    private static AllocationLedger BuildLedger(
        Money totalIn,
        Money billsDueNow,
        Money laterExpenses,
        Money minimumDebt,
        Money requiredSinking,
        GroceryRequirement grocery,
        EssentialSpending spending,
        Money protectedSavings,
        Money committedFunds,
        Money safetyBuffer,
        Money flexibleSavings,
        Money entertainment,
        Money goals,
        Money personalTotal,
        Money surplus,
        ShortfallReport? shortfall)
    {
        var lines = new List<(string Label, Money Amount)>
        {
            ("Bills due before the next payday", billsDueNow),
            ("Reserved for later bills", laterExpenses),
            ("Minimum debt payments", minimumDebt),
            ("Required sinking funds", requiredSinking),
            ("Essential groceries", grocery.EssentialCash),
            ("Optional groceries", grocery.OptionalCash),
            ("Essential transport", spending.Transport),
            ("Other essential spending", spending.Other),
            ("Protected savings already held", protectedSavings),
            ("Committed funds already held", committedFunds),
            ("Household safety buffer", safetyBuffer),
            ("Flexible savings", flexibleSavings),
            ("Shared entertainment", entertainment),
            ("Lower-priority goals", goals),
            ("Personal discretionary allowances", personalTotal),
            ("Unallocated surplus", surplus)
        };

        var allocated = Money.Sum(lines.Select(line => line.Amount)).Round();
        var unfunded = shortfall?.UnresolvedDeficit ?? Money.Zero;

        return new AllocationLedger
        {
            TotalIn = totalIn,
            TotalAllocated = allocated,
            UnfundedShortfall = unfunded,
            Lines = lines
        };
    }

    private static GuidanceExplanation BuildExplanation(
        BudgetDocument document,
        IncomeBasis basis,
        DateOnly today,
        DateOnly validThrough,
        DateOnly? nextPayday,
        Money availableNow,
        Money income,
        Money reliableIncome,
        Money mustNotSpend,
        Money safeToSpend,
        ReservationPlan reservations,
        ContributionSplit split,
        GroceryRequirement grocery,
        IReadOnlyList<string> missing)
    {
        var sources = document.CostGuidance
            .Where(record => record.HasAmounts)
            .Select(record => record.DescribeSource())
            .Distinct(StringComparer.Ordinal)
            .Take(10)
            .ToList();

        return new GuidanceExplanation
        {
            IncomeBasis = basis.ToDisplayName(),
            InputsUsed =
            [
                $"{document.Accounts.Count} account(s) totalling {availableNow.ToDisplayString()}",
                $"{document.IncomeSources.Count(source => source.IsActive)} active income source(s)",
                $"{document.Expenses.Count} expense record(s)",
                $"{document.Debts.Count} debt(s) and {document.Funds.Count} savings fund(s)",
                $"{document.Reserves.Count} recorded reserve(s)"
            ],
            DatesConsidered =
            [
                $"Today: {today:yyyy-MM-dd}",
                nextPayday is { } next
                    ? $"Next payday: {next:yyyy-MM-dd}"
                    : "No further payday found in the projection",
                $"Guidance valid through: {validThrough:yyyy-MM-dd}",
                $"{reservations.Lines.Count} dated obligation(s) considered up to {today.AddYears(1):yyyy-MM-dd}"
            ],
            ObligationsProtected = reservations.Lines
                .Where(line => !line.RequiredFromThisPaycheque.IsZero)
                .Select(line =>
                    $"{line.Name}: {line.RequiredFromThisPaycheque.ToDisplayString()} for {line.DueDate:yyyy-MM-dd}")
                .ToList(),
            Calculations =
            [
                $"Money in: {availableNow.ToDisplayString()} available plus {income.ToDisplayString()} of income.",
                $"Reliable income used for essentials: {reliableIncome.ToDisplayString()}.",
                $"Protected and buffered: {mustNotSpend.ToDisplayString()}.",
                $"Safe to spend: {safeToSpend.ToDisplayString()}.",
                split.Explanation,
                AllocationReconciler.DescribeRounding(split)
            ],
            Assumptions =
            [
                "Account balances are taken as recorded, and projected income is added on top of them.",
                "Costs that vary rather than falling due are budgeted as weekly allowances.",
                grocery.CoveredByAssistance.IsZero
                    ? "No food assistance has been counted against the grocery requirement."
                    : $"{grocery.CoveredByAssistance.ToDisplayString()} of groceries is expected in goods " +
                      "through food assistance and is not counted as income.",
                "Reimbursements are assumed to repay a cost unless an offset says otherwise."
            ],
            MissingInformation = missing,
            GuidanceSources = sources.Count == 0
                ? ["No local cost guidance has been recorded."]
                : sources,
            FinancialEffect = safeToSpend.IsNegative
                ? $"The household is {safeToSpend.Abs().ToDisplayString()} short of its protected needs " +
                  $"through {validThrough:yyyy-MM-dd}."
                : $"{safeToSpend.ToDisplayString()} may be spent through {validThrough:yyyy-MM-dd} without " +
                  "harming a protected need."
        };
    }

    private static List<Recommendation> BuildRecommendations(
        ReservationPlan reservations,
        GroceryRequirement grocery,
        Money safeToSpend,
        Money mustNotSpend,
        IReadOnlyList<EarnerAllocation> earners,
        ShortfallReport? shortfall,
        IReadOnlyList<string> missing,
        GuidanceExplanation explanation,
        SpendingSafety safety)
    {
        var recommendations = new List<Recommendation>();

        foreach (var line in reservations.DueBeforeNextPayday)
        {
            recommendations.Add(new Recommendation
            {
                Instruction = $"Pay this now: {line.Name}, {line.RequiredFromThisPaycheque.ToDisplayString()}.",
                Urgency = RecommendationUrgency.ActNow,
                Amount = line.RequiredFromThisPaycheque,
                Tier = line.Tier,
                Explanation = explanation with { FinancialEffect = line.Explanation }
            });
        }

        foreach (var line in reservations.RequiringContribution)
        {
            recommendations.Add(new Recommendation
            {
                Instruction =
                    $"Put aside {line.RequiredFromThisPaycheque.ToDisplayString()} from this paycheque " +
                    $"for {line.Name}, due {line.DueDate:yyyy-MM-dd}.",
                Urgency = RecommendationUrgency.SetAside,
                Amount = line.RequiredFromThisPaycheque,
                Tier = line.Tier,
                Explanation = explanation with { FinancialEffect = line.Explanation }
            });
        }

        if (!grocery.EssentialCash.IsZero)
        {
            recommendations.Add(new Recommendation
            {
                Instruction =
                    $"Set aside {grocery.EssentialCash.ToDisplayString()} for essential groceries.",
                Urgency = RecommendationUrgency.SetAside,
                Amount = grocery.EssentialCash,
                Tier = NeedTier.Survival,
                Explanation = explanation with
                {
                    FinancialEffect =
                        $"Covers {grocery.CategoriesRequiringCash.Count} grocery categories that still " +
                        "need cash this week."
                }
            });
        }

        foreach (var earner in earners.Where(item => !item.DiscretionaryAllowance.IsZero))
        {
            recommendations.Add(new Recommendation
            {
                Instruction =
                    $"{earner.MemberName} may safely spend up to " +
                    $"{earner.RemainingPersonalBalance.ToDisplayString()} of their personal allowance.",
                Urgency = RecommendationUrgency.MaySpend,
                Amount = earner.RemainingPersonalBalance,
                Tier = NeedTier.DiscretionaryFreedom,
                Explanation = explanation with { FinancialEffect = earner.Explanation }
            });
        }

        if (!mustNotSpend.IsZero)
        {
            recommendations.Add(new Recommendation
            {
                Instruction =
                    $"Do not spend {mustNotSpend.ToDisplayString()}. It is held for bills, reserves, " +
                    "essential needs and the household buffer.",
                Urgency = RecommendationUrgency.SetAside,
                Amount = mustNotSpend,
                Tier = NeedTier.Survival,
                Explanation = explanation
            });
        }

        if (shortfall is not null)
        {
            recommendations.Add(new Recommendation
            {
                Instruction = shortfall.Explanation,
                Urgency = RecommendationUrgency.Warning,
                Amount = shortfall.TotalShortfall,
                Explanation = explanation with { FinancialEffect = shortfall.Explanation }
            });
        }

        foreach (var note in missing)
        {
            recommendations.Add(new Recommendation
            {
                Instruction = note,
                Urgency = RecommendationUrgency.MissingInformation,
                Explanation = explanation
            });
        }

        if (recommendations.Count == 0 && safety == SpendingSafety.Safe)
        {
            recommendations.Add(new Recommendation
            {
                Instruction = $"You may safely spend up to {safeToSpend.ToDisplayString()} this week.",
                Urgency = RecommendationUrgency.MaySpend,
                Amount = safeToSpend,
                Explanation = explanation
            });
        }

        return recommendations
            .OrderBy(item => item.Urgency)
            .ToList();
    }

    #endregion
}
