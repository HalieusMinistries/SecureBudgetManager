using SecureBudgetManager.Core.Debt;
using SecureBudgetManager.Core.Expenses;
using SecureBudgetManager.Core.Household;
using SecureBudgetManager.Core.Income;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Savings;
using SecureBudgetManager.Core.Storage;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.Core.Guidance;

public enum SuggestionDecision
{
    Accept = 0,
    Adjust = 1,
    Exclude = 2,
    Defer = 3,
    MarkUnknown = 4
}

public enum SuggestionOwnerKind
{
    Household = 0,
    Person = 1
}

public sealed record SuggestedBudgetLine
{
    public required Guid Id { get; init; }

    public Guid? ExistingExpenseId { get; init; }

    public required string Category { get; init; }

    public required NeedTier Tier { get; init; }

    public required SuggestionAmountKind AmountKind { get; init; }

    public required Money Amount { get; init; }

    public required Frequency Frequency { get; init; }

    public required SuggestionOwnerKind OwnerKind { get; init; }

    public Guid? OwnerMemberId { get; init; }

    public required string OwnerName { get; init; }

    public Money Low { get; init; } = Money.Zero;

    public Money Typical { get; init; } = Money.Zero;

    public Money Comfortable { get; init; } = Money.Zero;

    public string GuidanceSource { get; init; } = "Household records";

    public DateOnly? EffectiveDate { get; init; }

    public GuidanceConfidence Confidence { get; init; } = GuidanceConfidence.Medium;

    public required string Why { get; init; }

    public required string SafeToSpendEffect { get; init; }

    public required string UpcomingBillEffect { get; init; }

    public HouseholdCostCoverage Coverage { get; init; } = HouseholdCostCoverage.HouseholdPays;

    public string? CoverageExplanation { get; init; }

    public bool IsExistingActual { get; init; }

    public bool IsSafeRecommendation { get; init; }

    public SuggestionDecision Decision { get; init; } = SuggestionDecision.Accept;

    public Money WeeklyEquivalent => FrequencyConverter.ToWeekly(Amount, Frequency).Round();

    public Money MonthlyEquivalent => FrequencyConverter.ToMonthly(Amount, Frequency).Round();

    public decimal PercentOfRelevantNet { get; init; }

    public SuggestedBudgetLine WithDecision(SuggestionDecision decision, Money? amount = null) =>
        this with
        {
            Decision = decision,
            Amount = amount ?? Amount,
            AmountKind = decision == SuggestionDecision.MarkUnknown ? SuggestionAmountKind.Unknown : AmountKind
        };
}

public sealed record SuggestedBudgetSummary
{
    public required string PersonName { get; init; }

    public required Money EstimatedIncome { get; init; }

    public required Money Commitments { get; init; }

    public Money Remaining => (EstimatedIncome - Commitments).Round();
}

public sealed record SuggestedBudgetProposal
{
    public required IncomeEstimate Scenario { get; init; }

    public required DateOnly EffectiveDate { get; init; }

    public required IReadOnlyList<SuggestedBudgetLine> Lines { get; init; }

    public required IReadOnlyList<SuggestedBudgetSummary> People { get; init; }

    public required Money SharedObligations { get; init; }

    public required Money TotalEssentials { get; init; }

    public required Money RequiredSavings { get; init; }

    public required Money FlexibleSavings { get; init; }

    public required Money Discretionary { get; init; }

    public required Money SafetyMargin { get; init; }

    public required Money ConservativeSafeToSpend { get; init; }

    public required Money EssentialShortfall { get; init; }

    public required IReadOnlyList<string> Unfunded { get; init; }

    public required string ConfidenceNote { get; init; }

    public bool HasEssentialShortfall => !EssentialShortfall.IsZero;
}

/// <summary>
/// Builds a starting-budget proposal from confirmed records and local guidance. It never overwrites
/// the document until the household accepts specific lines.
/// </summary>
public static class SuggestedBudgetPlanner
{
    public static SuggestedBudgetProposal Propose(
        BudgetDocument document,
        DateOnly today,
        IncomeEstimate scenario)
    {
        ArgumentNullException.ThrowIfNull(document);

        var hierarchy = document.Hierarchy.Rules.Count == 0 ? NeedsHierarchy.Default : document.Hierarchy;
        var earners = EarnerIncomeCalculator.ForHousehold(
            document,
            scenario.ToBasis(),
            Frequency.Weekly,
            today);
        var weeklyNetByMember = earners.ToDictionary(item => item.MemberId, item => item.UsableNetIncome);
        var householdWeeklyNet = Money.Sum(earners.Select(item => item.UsableNetIncome)).Round();
        var lines = new List<SuggestedBudgetLine>();

        foreach (var expense in document.Expenses.Where(item => !item.IsArchived))
        {
            lines.Add(FromExistingExpense(document, expense, hierarchy, weeklyNetByMember, householdWeeklyNet, today));
        }

        foreach (var debt in document.Debts)
        {
            if (lines.Any(line =>
                    line.ExistingExpenseId is null
                    && line.Category.Equals("Debt payments", StringComparison.OrdinalIgnoreCase)
                    && line.Why.Contains(debt.Name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (document.Expenses.Any(expense =>
                    expense.Name.Contains(debt.Name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            lines.Add(FromDebt(debt, hierarchy, householdWeeklyNet, today));
        }

        foreach (var template in FlexibleTemplates())
        {
            if (lines.Any(line => line.Category.Equals(template.Category, StringComparison.OrdinalIgnoreCase)
                                  && line.OwnerKind == SuggestionOwnerKind.Household))
            {
                continue;
            }

            lines.Add(FromGuidance(document, template, hierarchy, householdWeeklyNet, today));
        }

        foreach (var member in document.Members.Where(item => item.IsDiscretionaryEligible && !item.IsArchived))
        {
            var net = weeklyNetByMember.TryGetValue(member.Id, out var value) ? value : Money.Zero;
            lines.Add(PersonalDiscretionary(member, net, today));
        }

        lines = lines
            .OrderBy(line => (int)line.Tier)
            .ThenBy(line => line.Category, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Summarise(document, today, scenario, lines, earners, householdWeeklyNet);
    }

    public static BudgetDocument Apply(BudgetDocument document, IEnumerable<SuggestedBudgetLine> lines, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(lines);

        var expenses = document.Expenses.ToList();
        foreach (var line in lines)
        {
            if (line.Decision is SuggestionDecision.Exclude or SuggestionDecision.Defer)
            {
                continue;
            }

            if (line.ExistingExpenseId is { } existingId)
            {
                var index = expenses.FindIndex(item => item.Id == existingId);
                if (index < 0)
                {
                    continue;
                }

                var current = expenses[index];
                if (current.AmountKind is SuggestionAmountKind.ActualFixed
                    || (!current.OriginatedAsSuggestion && current.ExpectedAmount.IsPositiveEnough()))
                {
                    expenses[index] = ApplyCoverageOnly(current, line, today);
                    continue;
                }

                expenses[index] = ApplyToExpense(current, line, today);
                continue;
            }

            if (line.Decision == SuggestionDecision.MarkUnknown)
            {
                continue;
            }

            if (line.Decision != SuggestionDecision.Adjust
                && (line.AmountKind == SuggestionAmountKind.Unknown
                    || line.Amount.IsZero && line.Coverage == HouseholdCostCoverage.HouseholdPays))
            {
                continue;
            }

            if (expenses.Any(item => item.Name.Equals(line.Category, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            expenses.Add(CreateExpense(line, today));
        }

        return document with { Expenses = expenses };
    }

    private static SuggestedBudgetLine FromExistingExpense(
        BudgetDocument document,
        ExpenseItem expense,
        NeedsHierarchy hierarchy,
        IReadOnlyDictionary<Guid, Money> weeklyNet,
        Money householdWeeklyNet,
        DateOnly today)
    {
        var coverage = ExpenseCoverage.Effective(expense);
        var owner = OwnerOf(document, expense);
        var relevantNet = owner.MemberId is { } id && weeklyNet.TryGetValue(id, out var personal)
            ? personal
            : householdWeeklyNet;
        var amount = coverage.CreatesHouseholdOutflow() ? expense.ExpectedAmount : Money.Zero;
        var kind = coverage switch
        {
            HouseholdCostCoverage.IncludedInAnotherPayment => SuggestionAmountKind.IncludedElsewhere,
            HouseholdCostCoverage.EmployerCovered => SuggestionAmountKind.EmployerCovered,
            HouseholdCostCoverage.NotApplicable => SuggestionAmountKind.NotApplicable,
            HouseholdCostCoverage.Reimbursed => SuggestionAmountKind.IncludedElsewhere,
            HouseholdCostCoverage.Inactive => SuggestionAmountKind.NotApplicable,
            _ when expense.Variability == ExpenseVariability.Fixed => SuggestionAmountKind.ActualFixed,
            _ => SuggestionAmountKind.EstimatedFixed
        };

        return new SuggestedBudgetLine
        {
            Id = Guid.NewGuid(),
            ExistingExpenseId = expense.Id,
            Category = expense.Name,
            Tier = hierarchy.TierFor(expense),
            AmountKind = kind,
            Amount = amount,
            Frequency = expense.Frequency,
            OwnerKind = owner.Kind,
            OwnerMemberId = owner.MemberId,
            OwnerName = owner.Name,
            Low = amount,
            Typical = amount,
            Comfortable = amount,
            GuidanceSource = expense.SuggestionSource ?? "Confirmed household bill",
            EffectiveDate = expense.SuggestionEffectiveDate ?? today,
            Confidence = coverage.CreatesHouseholdOutflow()
                ? GuidanceConfidence.High
                : GuidanceConfidence.High,
            Why = coverage.CreatesHouseholdOutflow()
                ? $"Uses the recorded {expense.Frequency.ToDisplayName().ToLowerInvariant()} amount rather than a salary percentage."
                : ExpenseCoverage.Explanation(expense),
            SafeToSpendEffect = coverage.CreatesHouseholdOutflow()
                ? "Reduces safe-to-spend by the reserved amount when due."
                : "Does not reduce safe-to-spend.",
            UpcomingBillEffect = coverage.CreatesHouseholdOutflow()
                ? "Counts as an upcoming bill when a due date is known."
                : "Hidden from ordinary outstanding-bill lists.",
            Coverage = coverage,
            CoverageExplanation = ExpenseCoverage.Explanation(expense),
            IsExistingActual = true,
            IsSafeRecommendation = true,
            Decision = SuggestionDecision.Accept,
            PercentOfRelevantNet = Percent(FrequencyConverter.ToWeekly(amount, expense.Frequency), relevantNet)
        };
    }

    private static SuggestedBudgetLine FromDebt(
        DebtAccount debt,
        NeedsHierarchy hierarchy,
        Money householdWeeklyNet,
        DateOnly today)
    {
        var weekly = FrequencyConverter.ToWeekly(debt.MinimumPayment, Frequency.Monthly);
        return new SuggestedBudgetLine
        {
            Id = Guid.NewGuid(),
            Category = $"{debt.Name} minimum",
            Tier = NeedTier.Safety,
            AmountKind = SuggestionAmountKind.ActualFixed,
            Amount = debt.MinimumPayment,
            Frequency = Frequency.Monthly,
            OwnerKind = SuggestionOwnerKind.Household,
            OwnerName = "Household",
            Low = debt.MinimumPayment,
            Typical = debt.MinimumPayment,
            Comfortable = debt.MinimumPayment,
            GuidanceSource = "Recorded debt minimum",
            EffectiveDate = today,
            Confidence = GuidanceConfidence.High,
            Why = "Minimum debt payments use the recorded amount, not a salary percentage.",
            SafeToSpendEffect = "Reduces safe-to-spend until the minimum is reserved or paid.",
            UpcomingBillEffect = "Counts as a required upcoming payment.",
            IsExistingActual = true,
            IsSafeRecommendation = true,
            Decision = SuggestionDecision.Accept,
            PercentOfRelevantNet = Percent(weekly, householdWeeklyNet)
        };
    }

    private static SuggestedBudgetLine FromGuidance(
        BudgetDocument document,
        FlexibleTemplate template,
        NeedsHierarchy hierarchy,
        Money householdWeeklyNet,
        DateOnly today)
    {
        var lookup = CostGuidanceLibrary.Find(
            document.CostGuidance,
            document.Locality,
            template.GuidanceCategory,
            document.Composition,
            today);
        var typical = lookup.Record is { HasAmounts: true } record ? record.Typical : template.FallbackWeekly;
        var low = lookup.Record is { HasAmounts: true } lowRecord ? lowRecord.Low : Money.Zero;
        var comfortable = lookup.Record is { HasAmounts: true } highRecord
            ? highRecord.Comfortable
            : typical;
        var confidence = lookup.HasGuidance && !lookup.RequiresReview
            ? GuidanceConfidence.Medium
            : GuidanceConfidence.Low;
        var why = lookup.HasGuidance
            ? lookup.Explanation
            : $"{template.Why} {lookup.Explanation}";

        return new SuggestedBudgetLine
        {
            Id = Guid.NewGuid(),
            Category = template.Category,
            Tier = template.Tier,
            AmountKind = lookup.HasGuidance ? SuggestionAmountKind.GuidanceBased : SuggestionAmountKind.Unknown,
            Amount = typical,
            Frequency = Frequency.Weekly,
            OwnerKind = SuggestionOwnerKind.Household,
            OwnerName = "Household",
            Low = low,
            Typical = typical,
            Comfortable = comfortable,
            GuidanceSource = lookup.Record?.DescribeSource() ?? "No local guidance recorded",
            EffectiveDate = lookup.Record?.EffectiveDate ?? today,
            Confidence = confidence,
            Why = why,
            SafeToSpendEffect = template.Tier.IsProtectedByDefault()
                ? "Protects essentials before discretionary spending."
                : "Only reduces safe-to-spend if accepted.",
            UpcomingBillEffect = "Flexible category — not presented as a confirmed bill.",
            IsExistingActual = false,
            IsSafeRecommendation = template.Tier.IsProtectedByDefault() && typical.IsPositiveEnough(),
            Decision = typical.IsPositiveEnough() && template.Tier.IsProtectedByDefault()
                ? SuggestionDecision.Accept
                : SuggestionDecision.Defer,
            PercentOfRelevantNet = Percent(typical, householdWeeklyNet)
        };
    }

    private static SuggestedBudgetLine PersonalDiscretionary(
        HouseholdMember member,
        Money weeklyNet,
        DateOnly today)
    {
        var amount = (weeklyNet * 0.08m).Round();
        return new SuggestedBudgetLine
        {
            Id = Guid.NewGuid(),
            Category = $"{member.Name} personal spending",
            Tier = NeedTier.DiscretionaryFreedom,
            AmountKind = weeklyNet.IsZero ? SuggestionAmountKind.Unknown : SuggestionAmountKind.IncomeBased,
            Amount = amount,
            Frequency = Frequency.Weekly,
            OwnerKind = SuggestionOwnerKind.Person,
            OwnerMemberId = member.Id,
            OwnerName = member.Name,
            Low = (weeklyNet * 0.04m).Round(),
            Typical = amount,
            Comfortable = (weeklyNet * 0.12m).Round(),
            GuidanceSource = "Income-based personal allowance",
            EffectiveDate = today,
            Confidence = weeklyNet.IsZero ? GuidanceConfidence.Low : GuidanceConfidence.Medium,
            Why = "Personal discretionary money stays with this person and is never charged to someone else.",
            SafeToSpendEffect = "Uses only this person's remaining money after their assigned obligations.",
            UpcomingBillEffect = "Does not create a household bill.",
            IsExistingActual = false,
            IsSafeRecommendation = false,
            Decision = SuggestionDecision.Defer,
            PercentOfRelevantNet = weeklyNet.IsZero ? 0m : 8m
        };
    }

    private static SuggestedBudgetProposal Summarise(
        BudgetDocument document,
        DateOnly today,
        IncomeEstimate scenario,
        IReadOnlyList<SuggestedBudgetLine> lines,
        IReadOnlyList<EarnerIncome> earners,
        Money householdWeeklyNet)
    {
        var accepted = lines
            .Where(line => line.Decision is SuggestionDecision.Accept or SuggestionDecision.Adjust)
            .Where(line => line.Coverage.CreatesHouseholdOutflow())
            .ToList();
        var essentials = Money.Sum(accepted
            .Where(line => line.Tier <= NeedTier.Safety)
            .Select(line => line.WeeklyEquivalent)).Round();
        var requiredSavings = Money.Sum(accepted
            .Where(line => line.Tier == NeedTier.Stability)
            .Select(line => line.WeeklyEquivalent)).Round();
        var flexible = Money.Sum(accepted
            .Where(line => line.Tier is NeedTier.FamilyAndBelonging or NeedTier.Growth)
            .Select(line => line.WeeklyEquivalent)).Round();
        var discretionary = Money.Sum(accepted
            .Where(line => line.Tier == NeedTier.DiscretionaryFreedom)
            .Select(line => line.WeeklyEquivalent)).Round();
        var shared = Money.Sum(accepted
            .Where(line => line.OwnerKind == SuggestionOwnerKind.Household)
            .Select(line => line.WeeklyEquivalent)).Round();
        var shortfall = Money.Max(Money.Zero, (essentials - householdWeeklyNet).Round());
        var margin = Money.Max(Money.Zero, (householdWeeklyNet - essentials - requiredSavings).Round());
        var safe = scenario == IncomeEstimate.Conservative
            ? Money.Max(Money.Zero, (householdWeeklyNet - essentials - requiredSavings - document.Preferences.MinimumBreathingRoom).Round())
            : Money.Max(Money.Zero, margin - discretionary);
        var unfunded = accepted
            .Where(line => line.Tier.IsProtectedByDefault() && householdWeeklyNet.IsZero)
            .Select(line => line.Category)
            .ToList();
        if (!shortfall.IsZero)
        {
            unfunded = accepted
                .Where(line => line.Tier.IsProtectedByDefault())
                .Select(line => line.Category)
                .ToList();
        }

        var people = earners
            .Select(earner =>
            {
                var commitments = Money.Sum(accepted
                    .Where(line => line.OwnerMemberId == earner.MemberId)
                    .Select(line => line.WeeklyEquivalent)).Round();
                return new SuggestedBudgetSummary
                {
                    PersonName = earner.MemberName,
                    EstimatedIncome = earner.UsableNetIncome,
                    Commitments = commitments
                };
            })
            .ToList();

        var missing = new List<string>();
        if (document.Accounts.Count == 0)
        {
            missing.Add("No account balance is recorded, so available money is unknown.");
        }

        if (document.CostGuidance.Count == 0)
        {
            missing.Add("No local guidance is stored, so flexible amounts have lower confidence.");
        }

        return new SuggestedBudgetProposal
        {
            Scenario = scenario,
            EffectiveDate = today,
            Lines = lines,
            People = people,
            SharedObligations = shared,
            TotalEssentials = essentials,
            RequiredSavings = requiredSavings,
            FlexibleSavings = flexible,
            Discretionary = discretionary,
            SafetyMargin = margin,
            ConservativeSafeToSpend = safe,
            EssentialShortfall = shortfall,
            Unfunded = unfunded,
            ConfidenceNote = missing.Count == 0
                ? "Suggestions use confirmed bills where they exist and local guidance only for flexible categories."
                : string.Join(" ", missing)
        };
    }

    private static ExpenseItem ApplyCoverageOnly(ExpenseItem current, SuggestedBudgetLine line, DateOnly today) =>
        current with
        {
            Coverage = line.Coverage,
            CoverageConfirmed = line.Coverage != HouseholdCostCoverage.HouseholdPays || current.CoverageConfirmed,
            CoveredByExplanation = line.CoverageExplanation ?? current.CoveredByExplanation,
            SuggestionEffectiveDate = current.SuggestionEffectiveDate ?? today
        };

    private static ExpenseItem ApplyToExpense(ExpenseItem current, SuggestedBudgetLine line, DateOnly today) =>
        current with
        {
            ExpectedAmount = line.Decision == SuggestionDecision.MarkUnknown ? current.ExpectedAmount : line.Amount,
            Frequency = line.Frequency,
            Coverage = line.Coverage,
            CoverageConfirmed = true,
            CoveredByExplanation = line.CoverageExplanation,
            OriginatedAsSuggestion = current.OriginatedAsSuggestion || !current.ExpectedAmount.IsPositiveEnough(),
            SuggestionSource = line.GuidanceSource,
            SuggestionEffectiveDate = line.EffectiveDate ?? today,
            AmountKind = line.AmountKind
        };

    private static ExpenseItem CreateExpense(SuggestedBudgetLine line, DateOnly today) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = line.Category,
            Category = ExpenseCategory.BuiltIn.FirstOrDefault(item =>
                           item.Name.Equals(line.Category, StringComparison.OrdinalIgnoreCase))
                       ?? ExpenseCategory.Custom(line.Category),
            ExpectedAmount = line.Amount,
            Frequency = line.Frequency,
            AnchorDueDate = today,
            DueDateUnknown = true,
            Necessity = line.Tier <= NeedTier.Safety ? ExpenseNecessity.Essential : ExpenseNecessity.Optional,
            Variability = line.AmountKind == SuggestionAmountKind.ActualFixed
                ? ExpenseVariability.Fixed
                : ExpenseVariability.Variable,
            Ownership = line.OwnerKind == SuggestionOwnerKind.Person
                ? Ownership.Individual
                : Ownership.Shared,
            Assignment = line.OwnerMemberId is { } owner
                ? BillAssignment.MemberPaysAll
                : BillAssignment.Unassigned,
            Split = line.OwnerMemberId is { } member ? SplitRule.SoleResponsibility(member) : null,
            Coverage = line.Coverage,
            CoverageConfirmed = true,
            CoveredByExplanation = line.CoverageExplanation,
            OriginatedAsSuggestion = true,
            SuggestionSource = line.GuidanceSource,
            SuggestionEffectiveDate = line.EffectiveDate ?? today,
            AmountKind = line.AmountKind
        };

    private static (SuggestionOwnerKind Kind, Guid? MemberId, string Name) OwnerOf(
        BudgetDocument document,
        ExpenseItem expense)
    {
        if (expense.Assignment == BillAssignment.MemberPaysAll
            && expense.Split?.Participants.FirstOrDefault() is { } payer)
        {
            var member = document.Members.FirstOrDefault(item => item.Id == payer);
            return (SuggestionOwnerKind.Person, payer, member?.Name ?? "Person");
        }

        return (SuggestionOwnerKind.Household, null, "Household");
    }

    private static decimal Percent(Money weekly, Money relevantNet)
    {
        if (relevantNet.IsZero)
        {
            return 0m;
        }

        return Math.Round(weekly.Amount / relevantNet.Amount * 100m, 1);
    }

    private static IReadOnlyList<FlexibleTemplate> FlexibleTemplates() =>
    [
        new("Grocery budget", "Groceries", NeedTier.Survival, new Money(120m), "Essential food uses guidance and household composition, not a salary percentage of a known bill."),
        new("Cleaning products", "Cleaning products", NeedTier.Stability, new Money(8m), "Cleaning products are a flexible household stock item."),
        new("Toiletries", "Toiletries", NeedTier.Stability, new Money(10m), "Toiletries are suggested from local guidance when it exists."),
        new("Emergency pantry", "Emergency pantry", NeedTier.Stability, new Money(10m), "A small pantry reserve is a stability item, not a confirmed bill."),
        new("Fuel reserve", "Fuel", NeedTier.Safety, new Money(40m), "Fuel is suggested only when actual usage is incomplete."),
        new("Car maintenance", "Car maintenance", NeedTier.Stability, new Money(15m), "A sinking amount for maintenance uses guidance or a conservative placeholder."),
        new("Tyres", "Tyres", NeedTier.Stability, new Money(8m), "Tyres are a required sinking fund, not a weekly bill."),
        new("Medical reserve", "Healthcare", NeedTier.Stability, new Money(15m), "A medical reserve is suggested until actual costs are known."),
        new("Registration reserve", "Car registration", NeedTier.Stability, new Money(6m), "Registration is reserved ahead of the due date."),
        new("Household repairs", "Household repairs", NeedTier.Stability, new Money(10m), "Repairs are a flexible sinking amount."),
        new("Shared entertainment", "Entertainment", NeedTier.FamilyAndBelonging, new Money(15m), "Shared family activity stays a household suggestion."),
        new("Dining out", "Dining out", NeedTier.DiscretionaryFreedom, new Money(20m), "Dining out is discretionary and never treated as a confirmed bill.")
    ];

    private sealed record FlexibleTemplate(
        string Category,
        string GuidanceCategory,
        NeedTier Tier,
        Money FallbackWeekly,
        string Why);
}

file static class SuggestedBudgetMoneyExtensions
{
    public static bool IsPositiveEnough(this Money amount) => !amount.IsZero && !amount.IsNegative;
}

file static class SuggestedBudgetIncomeExtensions
{
    public static IncomeBasis ToBasis(this IncomeEstimate estimate) => estimate switch
    {
        IncomeEstimate.Conservative => IncomeBasis.Conservative,
        IncomeEstimate.Optimistic => IncomeBasis.Optimistic,
        _ => IncomeBasis.Normal
    };
}
