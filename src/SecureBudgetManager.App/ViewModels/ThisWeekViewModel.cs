using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureBudgetManager.App.Services;
using SecureBudgetManager.Core.Guidance;
using SecureBudgetManager.Core.Models;

namespace SecureBudgetManager.App.ViewModels;

/// <summary>One line of the weekly summary, with the working behind it available on demand.</summary>
public sealed record GuidanceLineRow(string Label, string Amount, string Detail);

public sealed record ProtectedRow(string Label, string Amount, string Tier, string ProtectedBy, string Reason);

public sealed record RecommendationRow(string Instruction, string Urgency, string Amount, string Effect);

public sealed record ShortfallRow(string Name, string Amount, string Date, string Tier);

public sealed record PersonRow(
    string Name,
    string Gross,
    string Taxes,
    string Deductions,
    string Benefits,
    string UsableNet,
    string ContributionPercent,
    string SharedShare,
    string IndividualObligations,
    string Savings,
    string Allowance,
    string SpendingUsed,
    string RemainingBalance,
    string Basis,
    string Explanation);

/// <summary>
/// The page a household should be able to open on a Friday and know what to do. It answers the
/// four questions in order: what is here, what must be paid, what must be kept, and what is left.
/// </summary>
public sealed partial class ThisWeekViewModel : PageViewModel
{
    private readonly IBudgetSession _session;
    private readonly TimeProvider _clock;

    public ThisWeekViewModel(IBudgetSession session, TimeProvider clock)
        : base(
            "This week",
            "Guidance",
            "What is available, what has to be paid or set aside, and what may safely be spent " +
            "between now and the next payday. Every figure is calculated on this device from your " +
            "own records.")
    {
        _session = session;
        _clock = clock;
        _session.Changed += OnSessionChanged;
        Refresh();
    }

    [ObservableProperty]
    private string safetyLabel = string.Empty;

    [ObservableProperty]
    private string safetyExplanation = string.Empty;

    [ObservableProperty]
    private string safeToSpendText = string.Empty;

    [ObservableProperty]
    private string mustNotSpendText = string.Empty;

    [ObservableProperty]
    private string validThroughText = string.Empty;

    [ObservableProperty]
    private string incomeBasisText = string.Empty;

    [ObservableProperty]
    private string reconciliationText = string.Empty;

    [ObservableProperty]
    private string shortfallHeadline = string.Empty;

    [ObservableProperty]
    private string essentialRequiredText = string.Empty;

    [ObservableProperty]
    private string essentialFundedText = string.Empty;

    [ObservableProperty]
    private string essentialUnfundedText = string.Empty;

    [ObservableProperty]
    private string essentialsExplanation = string.Empty;

    [ObservableProperty]
    private bool showExplanation;

    [ObservableProperty]
    private string explanationInputs = string.Empty;

    [ObservableProperty]
    private string explanationDates = string.Empty;

    [ObservableProperty]
    private string explanationCalculations = string.Empty;

    [ObservableProperty]
    private string explanationAssumptions = string.Empty;

    [ObservableProperty]
    private string explanationProtected = string.Empty;

    [ObservableProperty]
    private string explanationSources = string.Empty;

    [ObservableProperty]
    private string explanationEffect = string.Empty;

    [ObservableProperty]
    private string testAmount = string.Empty;

    [ObservableProperty]
    private string testResult = string.Empty;

    public IReadOnlyList<GuidanceLineRow> MoneyIn { get; private set; } = [];

    public IReadOnlyList<GuidanceLineRow> Household { get; private set; } = [];

    public IReadOnlyList<ProtectedRow> Protections { get; private set; } = [];

    public IReadOnlyList<RecommendationRow> Recommendations { get; private set; } = [];

    public IReadOnlyList<PersonRow> People { get; private set; } = [];

    public IReadOnlyList<ShortfallRow> Shortfalls { get; private set; } = [];

    public IReadOnlyList<string> Warnings { get; private set; } = [];

    public IReadOnlyList<string> MissingInformation { get; private set; } = [];

    public bool HasShortfall => Shortfalls.Count > 0 || !string.IsNullOrEmpty(ShortfallHeadline);

    public bool HasWarnings => Warnings.Count > 0;

    public bool HasMissingInformation => MissingInformation.Count > 0;

    [RelayCommand]
    private void ToggleExplanation() => ShowExplanation = !ShowExplanation;

    [RelayCommand]
    private void Recalculate() => Refresh();

    [RelayCommand]
    private void TestSpending()
    {
        if (!_session.IsOpen)
        {
            TestResult = "Open the household database first.";
            return;
        }

        if (!AmountParsing.TryParseMoney(TestAmount, out var amount))
        {
            TestResult = "Enter an amount to check.";
            return;
        }

        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        var allocation = PaychequeAllocator.Allocate(_session.Document, today);
        var (safety, explanation) = PaychequeAllocator.Classify(allocation, amount);

        TestResult = $"{safety.ToDisplayName()}. {explanation}";
    }

    private void OnSessionChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        if (!_session.IsOpen)
        {
            Clear();
            return;
        }

        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        var allocation = PaychequeAllocator.Allocate(_session.Document, today);

        SafetyLabel = allocation.Safety.ToDisplayName();
        SafetyExplanation = allocation.Explanation.FinancialEffect;
        SafeToSpendText = allocation.SafeToSpend.ToDisplayString();
        MustNotSpendText = allocation.MustNotSpend.ToDisplayString();
        ValidThroughText = $"Valid through {allocation.ValidThrough:yyyy-MM-dd}";
        IncomeBasisText = $"Income basis: {allocation.Basis.ToDisplayName()}";

        ReconciliationText = allocation.Ledger.ReconcilesExactly
            ? $"{allocation.Ledger.TotalIn.ToDisplayString()} in, " +
              $"{allocation.Ledger.TotalAllocated.ToDisplayString()} allocated. Every dollar is accounted for."
            : $"Allocations differ from money in by {allocation.Ledger.Difference.ToDisplayString()}. " +
              "This is a calculation fault and should be reported.";

        MoneyIn =
        [
            Line("Money available now", allocation.AvailableNow, "Balances as recorded on your accounts."),
            Line("Income received today", allocation.IncomeReceived, "Deposits dated today."),
            Line(
                "Income expected before the next payday",
                allocation.IncomeExpectedBeforeNextPayday,
                allocation.NextPayday is { } next
                    ? $"Deposits between tomorrow and {next.AddDays(-1):yyyy-MM-dd}."
                    : "No further payday was found in the projection."),
            Line(
                "Income that may be relied on",
                allocation.ReliableIncome,
                "Essential obligations are protected with this figure."),
            Line(
                "Additional income above the baseline",
                allocation.AdditionalIncomeAboveBaseline,
                "Directed by your additional-income rules rather than assumed.")
        ];

        Household =
        [
            Line("Bills to pay this week", allocation.BillsDueBeforeNextPayday, "Due before the next payday."),
            Line("Reserve this week for later bills", allocation.ReservationsForLaterBills, "Spread over the paydays that remain."),
            Line("Essential groceries", allocation.EssentialGroceries, "From the current grocery plan, by category."),
            Line("Optional groceries", allocation.OptionalGroceries, "Grocery categories marked optional."),
            Line("Essential transport", allocation.EssentialTransport, "Getting to work."),
            Line("Other essential spending", allocation.OtherEssentialSpending, "Variable essential costs."),
            Line("Minimum debt payments", allocation.MinimumDebtPayments, "Protected before discretionary spending."),
            Line("Required sinking funds", allocation.RequiredSinkingFunds, "Dated savings targets."),
            Line("Protected savings already held", allocation.ProtectedSavings, "In the account but spoken for."),
            Line("Committed funds already held", allocation.CommittedFunds, "Already reserved against a bill or debt."),
            Line("Flexible savings", allocation.FlexibleSavings, "Reducible without harming a protected need."),
            Line("Shared household entertainment", allocation.SharedEntertainment, "A permission to spend, not an instruction."),
            Line("Lower-priority goals", allocation.LowerPriorityGoals, "Funded after required savings."),
            Line("Household safety margin", allocation.SafetyBuffer, "Kept back for what nobody saw coming."),
            Line("Unallocated surplus", allocation.UnallocatedSurplus, "Not yet assigned to anything."),
            Line("Safe to spend", allocation.SafeToSpend, "Everything above the protected total."),
            Line("Must not be spent", allocation.MustNotSpend, "Held for named obligations and the buffer.")
        ];

        Protections = allocation.Protections
            .Select(item => new ProtectedRow(
                item.Label,
                item.Amount.ToDisplayString(),
                item.Tier.ToDisplayName(),
                item.ProtectedBy,
                item.Reason))
            .ToList();

        Recommendations = allocation.Recommendations
            .Select(item => new RecommendationRow(
                item.Instruction,
                Describe(item.Urgency),
                item.Amount?.ToDisplayString() ?? string.Empty,
                item.Explanation.FinancialEffect))
            .ToList();

        People = allocation.Earners
            .Select(earner => new PersonRow(
                earner.MemberName,
                earner.Income.GrossIncome.ToDisplayString(),
                earner.Income.Taxes.ToDisplayString(),
                earner.Income.PayrollDeductions.ToDisplayString(),
                earner.Income.BenefitDeductions.ToDisplayString(),
                earner.Income.UsableNetIncome.ToDisplayString(),
                $"{earner.ContributionPercent:0.##}%",
                earner.ShareOfSharedObligations.ToDisplayString(),
                earner.Income.IndividualObligations.ToDisplayString(),
                earner.SavingsContribution.ToDisplayString(),
                earner.Income.IsDiscretionaryEligible
                    ? earner.DiscretionaryAllowance.ToDisplayString()
                    : "Not eligible",
                earner.PersonalSpendingUsed.ToDisplayString(),
                earner.Income.IsDiscretionaryEligible
                    ? earner.RemainingPersonalBalance.ToDisplayString()
                    : "—",
                earner.Income.Basis,
                earner.Explanation))
            .ToList();

        Shortfalls = allocation.Shortfall is { } shortfall
            ? shortfall.Affected
                .Select(item => new ShortfallRow(
                    item.Name,
                    item.AmountMissing.ToDisplayString(),
                    item.Date.ToString("yyyy-MM-dd"),
                    item.Tier.ToDisplayName()))
                .ToList()
            : [];

        ShortfallHeadline = allocation.Shortfall?.Explanation ?? string.Empty;
        EssentialRequiredText = allocation.Essentials.Required.ToDisplayString();
        EssentialFundedText = allocation.Essentials.Funded.ToDisplayString();
        EssentialUnfundedText = allocation.Essentials.Unfunded.ToDisplayString();
        EssentialsExplanation = allocation.Essentials.Explanation;
        Warnings = allocation.Warnings;
        MissingInformation = allocation.MissingInformation;

        var explanation = allocation.Explanation;
        ExplanationInputs = string.Join(Environment.NewLine, explanation.InputsUsed);
        ExplanationDates = string.Join(Environment.NewLine, explanation.DatesConsidered);
        ExplanationCalculations = string.Join(Environment.NewLine, explanation.Calculations);
        ExplanationAssumptions = string.Join(Environment.NewLine, explanation.Assumptions);
        ExplanationProtected = explanation.ObligationsProtected.Count == 0
            ? "No dated obligation needs money from this paycheque."
            : string.Join(Environment.NewLine, explanation.ObligationsProtected);
        ExplanationSources = string.Join(Environment.NewLine, explanation.GuidanceSources);
        ExplanationEffect = explanation.FinancialEffect;

        NotifyLists();
    }

    private void Clear()
    {
        MoneyIn = [];
        Household = [];
        Protections = [];
        Recommendations = [];
        People = [];
        Shortfalls = [];
        Warnings = [];
        MissingInformation = [];

        SafetyLabel = string.Empty;
        SafetyExplanation = string.Empty;
        SafeToSpendText = string.Empty;
        MustNotSpendText = string.Empty;
        ValidThroughText = string.Empty;
        IncomeBasisText = string.Empty;
        ReconciliationText = string.Empty;
        ShortfallHeadline = string.Empty;
        EssentialRequiredText = string.Empty;
        EssentialFundedText = string.Empty;
        EssentialUnfundedText = string.Empty;
        EssentialsExplanation = string.Empty;
        ExplanationInputs = string.Empty;
        ExplanationDates = string.Empty;
        ExplanationCalculations = string.Empty;
        ExplanationAssumptions = string.Empty;
        ExplanationProtected = string.Empty;
        ExplanationSources = string.Empty;
        ExplanationEffect = string.Empty;
        TestAmount = string.Empty;
        TestResult = string.Empty;
        ShowExplanation = false;

        NotifyLists();
    }

    private void NotifyLists()
    {
        OnPropertyChanged(nameof(MoneyIn));
        OnPropertyChanged(nameof(Household));
        OnPropertyChanged(nameof(Protections));
        OnPropertyChanged(nameof(Recommendations));
        OnPropertyChanged(nameof(People));
        OnPropertyChanged(nameof(Shortfalls));
        OnPropertyChanged(nameof(Warnings));
        OnPropertyChanged(nameof(MissingInformation));
        OnPropertyChanged(nameof(HasShortfall));
        OnPropertyChanged(nameof(HasWarnings));
        OnPropertyChanged(nameof(HasMissingInformation));
    }

    private static GuidanceLineRow Line(string label, Money amount, string detail) =>
        new(label, amount.ToDisplayString(), detail);

    private static string Describe(RecommendationUrgency urgency) => urgency switch
    {
        RecommendationUrgency.ActNow => "Pay now",
        RecommendationUrgency.SetAside => "Set aside",
        RecommendationUrgency.MaySpend => "May spend",
        RecommendationUrgency.Warning => "Warning",
        _ => "Missing information"
    };
}
