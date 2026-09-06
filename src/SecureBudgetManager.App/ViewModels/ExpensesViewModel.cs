using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureBudgetManager.App.Interaction;
using SecureBudgetManager.App.Services;
using SecureBudgetManager.Core.Budgeting;
using SecureBudgetManager.Core.Expenses;
using SecureBudgetManager.Core.Guidance;
using SecureBudgetManager.Core.Household;
using SecureBudgetManager.Core.Income;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Storage;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.App.ViewModels;

public sealed record ExpenseListItem(
    Guid Id,
    string Name,
    string Owner,
    string Category,
    string Necessity,
    string Frequency,
    string Weekly,
    string Monthly,
    string Annual,
    string Status,
    string Coverage,
    string HouseholdResponsibility);

public sealed class SuggestionReviewItem : ObservableObject
{
    public SuggestionReviewItem(SuggestedBudgetLine line)
    {
        Line = line;
        AdjustedAmount = AmountParsing.Format(line.Amount);
        Decision = line.Decision;
    }

    public SuggestedBudgetLine Line { get; private set; }

    public string Category => Line.Category;

    public string AmountKind => Line.AmountKind.ToDisplayName();

    public string SuggestedAmount => Line.Amount.ToDisplayString();

    public string Frequency => Line.Frequency.ToDisplayName();

    public string Weekly => Line.WeeklyEquivalent.ToDisplayString();

    public string Monthly => Line.MonthlyEquivalent.ToDisplayString();

    public string Owner => Line.OwnerName;

    public string PercentOfNet => $"{Line.PercentOfRelevantNet:0.#}%";

    public string Range =>
        $"{Line.Low.ToDisplayString()} low · {Line.Typical.ToDisplayString()} typical · {Line.Comfortable.ToDisplayString()} comfortable";

    public string Source => Line.GuidanceSource;

    public string EffectiveDate => Line.EffectiveDate?.ToString("yyyy-MM-dd") ?? "Unknown";

    public string Confidence => Line.Confidence.ToString();

    public string Why => Line.Why;

    public string SafeToSpendEffect => Line.SafeToSpendEffect;

    public string UpcomingBillEffect => Line.UpcomingBillEffect;

    public string Tier => Line.Tier.ToDisplayName();

    public bool IsExistingActual => Line.IsExistingActual;

    public IReadOnlyList<ChoiceOption<SuggestionDecision>> Decisions { get; } =
    [
        new(SuggestionDecision.Accept, "Accept"),
        new(SuggestionDecision.Adjust, "Adjust"),
        new(SuggestionDecision.Exclude, "Exclude"),
        new(SuggestionDecision.Defer, "Defer"),
        new(SuggestionDecision.MarkUnknown, "Mark unknown")
    ];

    public string AdjustedAmount
    {
        get => _adjustedAmount;
        set => SetProperty(ref _adjustedAmount, value);
    }

    public SuggestionDecision Decision
    {
        get => _decision;
        set => SetProperty(ref _decision, value);
    }

    private string _adjustedAmount = string.Empty;
    private SuggestionDecision _decision;

    public SuggestedBudgetLine ToLine()
    {
        var amount = Line.Amount;
        if (Decision == SuggestionDecision.Adjust
            && AmountParsing.TryParseMoney(AdjustedAmount, out var parsed)
            && !parsed.IsNegative)
        {
            amount = parsed;
        }

        return Line.WithDecision(Decision, amount);
    }
}

public sealed partial class ExpensesViewModel : PageViewModel, IEditablePage
{
    private readonly IBudgetSession _session;
    private readonly IUserDialog _dialog;
    private readonly TimeProvider _clock;
    private Guid? _editingId;
    private string _originalFingerprint = string.Empty;

    public ExpensesViewModel(IBudgetSession session, IUserDialog dialog, TimeProvider? clock = null)
        : base(
            "Expenses",
            "Money out",
            "Bills and other recurring costs stay on this computer. Average monthly figures use the annual total divided by 12, not four weeks.")
    {
        _session = session;
        _dialog = dialog;
        _clock = clock ?? TimeProvider.System;
        _session.Changed += OnSessionChanged;
        CategoryOptions = ExpenseCategory.BuiltIn
            .Select(category => new ChoiceOption<string>(category.Name, category.Name))
            .ToList();
        FrequencyOptions = FrequencyChoices.Expense;
        NecessityOptions =
        [
            new ChoiceOption<ExpenseNecessity>(ExpenseNecessity.Essential, "Essential"),
            new ChoiceOption<ExpenseNecessity>(ExpenseNecessity.Optional, "Optional")
        ];
        VariabilityOptions =
        [
            new ChoiceOption<ExpenseVariability>(ExpenseVariability.Fixed, "Fixed"),
            new ChoiceOption<ExpenseVariability>(ExpenseVariability.Variable, "Variable")
        ];
        CoverageOptions =
        [
            new ChoiceOption<HouseholdCostCoverage>(HouseholdCostCoverage.HouseholdPays, "Household pays"),
            new ChoiceOption<HouseholdCostCoverage>(HouseholdCostCoverage.IncludedInAnotherPayment, "Included in another payment"),
            new ChoiceOption<HouseholdCostCoverage>(HouseholdCostCoverage.EmployerCovered, "Employer-covered"),
            new ChoiceOption<HouseholdCostCoverage>(HouseholdCostCoverage.Reimbursed, "Reimbursed"),
            new ChoiceOption<HouseholdCostCoverage>(HouseholdCostCoverage.NotApplicable, "Not applicable"),
            new ChoiceOption<HouseholdCostCoverage>(HouseholdCostCoverage.Inactive, "Inactive")
        ];
        ScenarioOptions =
        [
            new ChoiceOption<IncomeEstimate>(IncomeEstimate.Conservative, "Conservative"),
            new ChoiceOption<IncomeEstimate>(IncomeEstimate.Normal, "Normal"),
            new ChoiceOption<IncomeEstimate>(IncomeEstimate.Optimistic, "Optimistic")
        ];
        Refresh();
    }

    public IReadOnlyList<ChoiceOption<string>> CategoryOptions { get; }

    public IReadOnlyList<ChoiceOption<Frequency>> FrequencyOptions { get; }

    public IReadOnlyList<ChoiceOption<ExpenseNecessity>> NecessityOptions { get; }

    public IReadOnlyList<ChoiceOption<ExpenseVariability>> VariabilityOptions { get; }

    public IReadOnlyList<ChoiceOption<HouseholdCostCoverage>> CoverageOptions { get; }

    public IReadOnlyList<ChoiceOption<IncomeEstimate>> ScenarioOptions { get; }

    public IReadOnlyList<ChoiceOption<Guid?>> OwnerOptions { get; private set; } = [];

    public IReadOnlyList<ExpenseListItem> Items { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasItems))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private int itemCount;

    public bool HasItems => ItemCount > 0;

    public bool ShowEmptyState => _session.IsOpen && ItemCount == 0 && !IsEditorOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    [NotifyCanExecuteChangedFor(nameof(SaveEntryCommand))]
    private bool isEditorOpen;

    [ObservableProperty]
    private string editorTitle = "Add expense";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditorChanges))]
    private string editorName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditorChanges))]
    private Guid? editorOwnerId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditorChanges))]
    private string editorCategory = ExpenseCategory.Other.Name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditorChanges))]
    private ExpenseNecessity editorNecessity = ExpenseNecessity.Essential;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditorChanges))]
    [NotifyPropertyChangedFor(nameof(EquivalentSummary))]
    private string editorAmount = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditorChanges))]
    [NotifyPropertyChangedFor(nameof(EquivalentSummary))]
    private Frequency editorFrequency = Frequency.Monthly;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditorChanges))]
    private DateTime? editorDueDate = DateTime.Today;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditorChanges))]
    private DateTime? editorEndDate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditorChanges))]
    [NotifyPropertyChangedFor(nameof(IsVariable))]
    private ExpenseVariability editorVariability = ExpenseVariability.Fixed;

    public bool IsVariable => EditorVariability == ExpenseVariability.Variable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditorChanges))]
    private bool editorIsActive = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditorChanges))]
    [NotifyPropertyChangedFor(nameof(CoverageExplanation))]
    private HouseholdCostCoverage editorCoverage = HouseholdCostCoverage.HouseholdPays;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditorChanges))]
    private string editorCoverageExplanation = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditorTitle))]
    [NotifyPropertyChangedFor(nameof(EditorSaveLabel))]
    [NotifyPropertyChangedFor(nameof(IsExpenseEditor))]
    private bool isSuggestionEditor;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SuggestionSummary))]
    private IncomeEstimate selectedScenario = IncomeEstimate.Conservative;

    public bool IsExpenseEditor => IsEditorOpen && !IsSuggestionEditor;

    public string CoverageExplanation =>
        EditorCoverage switch
        {
            HouseholdCostCoverage.IncludedInAnotherPayment =>
                string.IsNullOrWhiteSpace(EditorCoverageExplanation) ? "Included in rent" : EditorCoverageExplanation,
            HouseholdCostCoverage.EmployerCovered =>
                string.IsNullOrWhiteSpace(EditorCoverageExplanation) ? "Employer-covered" : EditorCoverageExplanation,
            _ => EditorCoverageExplanation
        };

    public IReadOnlyList<SuggestionReviewItem> SuggestionLines { get; private set; } = [];

    public SuggestedBudgetProposal? Proposal { get; private set; }

    public string SuggestionSummary => Proposal is null
        ? "Open the suggested starting budget to review amounts before anything is saved."
        : $"{Proposal.Scenario} scenario. Essentials {Proposal.TotalEssentials.ToDisplayString()} a week. " +
          $"Safety margin {Proposal.SafetyMargin.ToDisplayString()}. " +
          (Proposal.HasEssentialShortfall
              ? $"Conservative income cannot fund essentials by {Proposal.EssentialShortfall.ToDisplayString()}."
              : $"Conservative-style safe-to-spend {Proposal.ConservativeSafeToSpend.ToDisplayString()}.") +
          " " + Proposal.ConfidenceNote;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    [ObservableProperty]
    private string? statusMessage;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public string EditorSaveLabel => IsSuggestionEditor ? "Save suggested budget" : "Save expense";

    public string? EditorEffectPreview => EquivalentSummary;

    public bool HasEditorError => HasError;

    public string? EditorError => ErrorMessage;

    public ICommand SaveEditorCommand => IsSuggestionEditor ? SaveSuggestedBudgetCommand : SaveEntryCommand;

    ICommand IEditablePage.CancelEditorCommand => CancelEditorCommand;

    public bool TryLeaveEditor() => ConfirmDiscardEditor();

    public void DismissEditor() => CloseEditor();

    public bool HasEditorChanges => IsEditorOpen && Fingerprint() != _originalFingerprint;

    public string EquivalentSummary
    {
        get
        {
            if (!AmountParsing.TryParseMoney(EditorAmount, out var amount) || amount.IsNegative)
            {
                return "Enter a valid amount to see weekly, average-monthly and annual equivalents.";
            }

            var annual = FrequencyConverter.ToAnnual(amount, EditorFrequency).Round();
            var weekly = BudgetOverviewCalculator.ToPeriod(annual, DisplayPeriod.Weekly);
            var monthly = BudgetOverviewCalculator.ToPeriod(annual, DisplayPeriod.AverageMonthly);
            return $"Expected: {weekly.ToDisplayString()} weekly · {monthly.ToDisplayString()} average monthly · {annual.ToDisplayString()} a year.";
        }
    }

    [RelayCommand]
    private void BeginAdd()
    {
        if (!ConfirmDiscardEditor())
        {
            return;
        }

        OpenEditor(null, "Add expense", null);
    }

    [RelayCommand]
    private void BeginEdit(ExpenseListItem? item)
    {
        if (item is null || !ConfirmDiscardEditor())
        {
            return;
        }

        var expense = _session.Document.Expenses.FirstOrDefault(entry => entry.Id == item.Id);
        if (expense is null)
        {
            return;
        }

        OpenEditor(expense.Id, "Edit expense", expense);
    }

    [RelayCommand]
    private void Duplicate(ExpenseListItem? item)
    {
        if (item is null || !ConfirmDiscardEditor())
        {
            return;
        }

        var expense = _session.Document.Expenses.FirstOrDefault(entry => entry.Id == item.Id);
        if (expense is null)
        {
            return;
        }

        OpenEditor(null, "Duplicate expense", expense with { Id = Guid.NewGuid(), Name = expense.Name + " (copy)" });
    }

    [RelayCommand(CanExecute = nameof(CanSaveEntry))]
    private async Task SaveEntryAsync(CancellationToken cancellationToken)
    {
        if (!TryBuildExpense(_editingId ?? Guid.NewGuid(), out var expense, out var error))
        {
            ErrorMessage = error;
            return;
        }

        var document = _session.Document;
        var expenses = document.Expenses.ToList();

        if (_editingId is { } id)
        {
            var index = expenses.FindIndex(entry => entry.Id == id);
            if (index < 0)
            {
                ErrorMessage = "That expense is no longer available.";
                return;
            }

            expenses[index] = expense with { Id = id };
        }
        else
        {
            expenses.Add(expense);
        }

        if (!await CommitAsync(document with { Expenses = expenses }, cancellationToken))
        {
            return;
        }

        CloseEditor();
        StatusMessage = "Expense saved";
    }

    private bool CanSaveEntry() => _session.IsOpen && !_session.IsSaving && IsEditorOpen;

    [RelayCommand]
    private void CancelEditor()
    {
        if (!ConfirmDiscardEditor(forcePrompt: HasEditorChanges))
        {
            return;
        }

        CloseEditor();
    }

    [RelayCommand]
    private async Task RemoveAsync(ExpenseListItem? item, CancellationToken cancellationToken)
    {
        if (item is null || !_session.IsOpen)
        {
            return;
        }

        if (!_dialog.Confirm("Remove expense", $"Remove the expense named {item.Name}?"))
        {
            return;
        }

        var document = _session.Document;
        var remaining = document.Expenses.Where(expense => expense.Id != item.Id).ToList();
        var transactions = document.Transactions
            .Select(transaction => transaction.ExpenseItemId == item.Id
                ? transaction with { ExpenseItemId = null }
                : transaction)
            .ToList();

        if (!await CommitAsync(document with { Expenses = remaining, Transactions = transactions }, cancellationToken))
        {
            return;
        }

        if (_editingId == item.Id)
        {
            CloseEditor();
        }

        StatusMessage = "Expense removed.";
    }

    public string? ValidateEditor()
    {
        TryBuildExpense(_editingId ?? Guid.NewGuid(), out _, out var error);
        return error;
    }

    private bool TryBuildExpense(Guid id, out ExpenseItem expense, out string? error)
    {
        expense = null!;
        error = null;

        if (string.IsNullOrWhiteSpace(EditorName))
        {
            error = "An expense needs a name.";
            return false;
        }

        if (!AmountParsing.TryParseMoney(EditorAmount, out var amount) || amount.IsNegative)
        {
            error = "Enter a valid amount.";
            return false;
        }

        if (EditorDueDate is null)
        {
            error = "A due date is required. This anchors the recurrence calendar.";
            return false;
        }

        var due = DateOnly.FromDateTime(EditorDueDate.Value);
        DateOnly? endsOn = EditorEndDate is null ? null : DateOnly.FromDateTime(EditorEndDate.Value);

        var category = ExpenseCategory.BuiltIn.FirstOrDefault(item => item.Name == EditorCategory)
            ?? ExpenseCategory.Custom(EditorCategory);

        var ownership = EditorOwnerId is null ? Ownership.Shared : Ownership.Individual;
        var assignment = EditorOwnerId is null ? BillAssignment.Unassigned : BillAssignment.MemberPaysAll;
        SplitRule? split = EditorOwnerId is { } owner
            ? SplitRule.SoleResponsibility(owner)
            : null;

        try
        {
            var existing = _session.Document.Expenses.FirstOrDefault(item => item.Id == id);

            expense = new ExpenseItem
            {
                Id = id,
                Name = EditorName.Trim(),
                Category = category,
                ExpectedAmount = amount,
                Frequency = EditorFrequency,
                AnchorDueDate = due,
                Necessity = EditorNecessity,
                Variability = EditorVariability,
                Ownership = ownership,
                Assignment = existing is { } current
                             && current.Ownership == ownership
                             && current.Assignment is not BillAssignment.Unassigned
                    ? current.Assignment
                    : assignment,
                Split = existing is { Split: not null } && ownership == existing.Ownership
                    ? existing.Split
                    : split,
                AutopayAnchorDate = existing?.AutopayAnchorDate,
                IsPaused = !EditorIsActive,
                DueDateUnknown = existing?.DueDateUnknown ?? false,
                EndsOn = endsOn,
                Notes = existing?.Notes,
                Coverage = EditorCoverage,
                CoverageConfirmed = true,
                CoveredByExplanation = string.IsNullOrWhiteSpace(EditorCoverageExplanation)
                    ? DefaultCoverageExplanation(EditorCoverage)
                    : EditorCoverageExplanation.Trim(),
                CoveredByExpenseId = existing?.CoveredByExpenseId,
                OriginatedAsSuggestion = existing?.OriginatedAsSuggestion ?? false,
                SuggestionSource = existing?.SuggestionSource,
                SuggestionEffectiveDate = existing?.SuggestionEffectiveDate,
                AmountKind = existing?.AmountKind ?? SuggestionAmountKind.UserDefined,
                AnnualIncreasePercent = existing?.AnnualIncreasePercent ?? 0m
            };

            expense.Validate();
            return true;
        }
        catch (ArgumentException exception)
        {
            error = exception.Message;
            return false;
        }
    }

    private Guid[] Adults() => _session.Document.Members
        .Where(member => !member.IsDependant)
        .Select(member => member.Id)
        .ToArray();

    private void OpenEditor(Guid? id, string title, ExpenseItem? expense)
    {
        ErrorMessage = null;
        IsSuggestionEditor = false;
        _editingId = id;
        EditorTitle = title;

        if (expense is null)
        {
            EditorName = string.Empty;
            EditorOwnerId = null;
            EditorCategory = ExpenseCategory.Other.Name;
            EditorNecessity = ExpenseNecessity.Essential;
            EditorAmount = string.Empty;
            EditorFrequency = Frequency.Monthly;
            EditorDueDate = DateTime.Today;
            EditorEndDate = null;
            EditorVariability = ExpenseVariability.Fixed;
            EditorIsActive = true;
            EditorCoverage = HouseholdCostCoverage.HouseholdPays;
            EditorCoverageExplanation = string.Empty;
        }
        else
        {
            EditorName = expense.Name;
            EditorOwnerId = expense.Ownership == Ownership.Individual
                ? expense.Split?.Participants.FirstOrDefault()
                : null;
            EditorCategory = expense.Category.Name;
            EditorNecessity = expense.Necessity;
            EditorAmount = AmountParsing.Format(expense.ExpectedAmount);
            EditorFrequency = expense.Frequency;
            EditorDueDate = expense.AnchorDueDate.ToDateTime(TimeOnly.MinValue);
            EditorEndDate = expense.EndsOn?.ToDateTime(TimeOnly.MinValue);
            EditorVariability = expense.Variability;
            EditorIsActive = !expense.IsPaused;
            EditorCoverage = ExpenseCoverage.Effective(expense);
            EditorCoverageExplanation = ExpenseCoverage.Explanation(expense);
        }

        _originalFingerprint = Fingerprint();
        IsEditorOpen = true;
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(HasEditorChanges));
        OnPropertyChanged(nameof(EquivalentSummary));
    }

    private string Fingerprint() => string.Join("|",
        EditorName,
        EditorOwnerId,
        EditorCategory,
        EditorNecessity,
        EditorAmount,
        EditorFrequency,
        EditorDueDate,
        EditorEndDate,
        EditorVariability,
        EditorIsActive,
        EditorCoverage,
        EditorCoverageExplanation);

    private bool ConfirmDiscardEditor(bool forcePrompt = false)
    {
        if (!IsEditorOpen)
        {
            return true;
        }

        if (!HasEditorChanges && !forcePrompt)
        {
            CloseEditor();
            return true;
        }

        if (!_dialog.Confirm("Unsaved changes", "Discard the expense entry you are editing?"))
        {
            return false;
        }

        CloseEditor();
        return true;
    }

    private void CloseEditor()
    {
        IsEditorOpen = false;
        IsSuggestionEditor = false;
        _editingId = null;
        EditorName = string.Empty;
        EditorAmount = string.Empty;
        ErrorMessage = null;
        SuggestionLines = [];
        Proposal = null;
        _originalFingerprint = string.Empty;
        OnPropertyChanged(nameof(SuggestionLines));
        OnPropertyChanged(nameof(Proposal));
        OnPropertyChanged(nameof(SuggestionSummary));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(HasEditorChanges));
    }

    private async Task<bool> CommitAsync(BudgetDocument document, CancellationToken cancellationToken)
    {
        var result = await EditorSaveCoordinator.TryCommitAsync(
            _session,
            document,
            cancellationToken,
            IsSuggestionEditor ? "Suggested budget saved" : "Expense saved");
        ErrorMessage = result.IsSuccess ? null : result.Message;
        return result.IsSuccess;
    }

    private void OnSessionChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        if (!_session.IsOpen)
        {
            Items = [];
            ItemCount = 0;
            OwnerOptions = [];
            CloseEditor();
            StatusMessage = null;
            ErrorMessage = null;
            OnPropertyChanged(nameof(Items));
            OnPropertyChanged(nameof(OwnerOptions));
            return;
        }

        var document = _session.Document;
        var owners = new List<ChoiceOption<Guid?>> { new(null, "Shared household") };
        owners.AddRange(document.Members.Select(member => new ChoiceOption<Guid?>(member.Id, member.Name)));
        OwnerOptions = owners;

        Items = document.Expenses.Select(expense =>
        {
            var annual = expense.AnnualCost.Round();
            return new ExpenseListItem(
                expense.Id,
                expense.Name,
                OwnerLabel(document, expense),
                expense.Category.Name,
                expense.Necessity == ExpenseNecessity.Essential ? "Essential" : "Optional",
                expense.Frequency.ToDisplayName(),
                BudgetOverviewCalculator.ToPeriod(annual, DisplayPeriod.Weekly).ToDisplayString(),
                BudgetOverviewCalculator.ToPeriod(annual, DisplayPeriod.AverageMonthly).ToDisplayString(),
                annual.ToDisplayString(),
                expense.IsPaused
                    ? "Inactive"
                    : ExpenseCoverage.CreatesHouseholdOutflow(expense)
                        ? "Active"
                        : ExpenseCoverage.Explanation(expense),
                ExpenseCoverage.Effective(expense).ToDisplayName(),
                ExpenseCoverage.HouseholdResponsibility(expense).ToDisplayString());
        }).ToList();

        ItemCount = Items.Count;
        OnPropertyChanged(nameof(Items));
        OnPropertyChanged(nameof(OwnerOptions));
        SaveEntryCommand.NotifyCanExecuteChanged();
    }

    private static string OwnerLabel(BudgetDocument document, ExpenseItem expense)
    {
        if (expense.Ownership == Ownership.Shared)
        {
            return "Shared";
        }

        var owner = expense.Split?.Participants.FirstOrDefault();
        return owner is { } id ? document.MemberName(id) : "Individual";
    }

    [RelayCommand]
    public void BeginSuggestedBudget()
    {
        if (!ConfirmDiscardEditor())
        {
            return;
        }

        if (!_session.IsOpen)
        {
            ErrorMessage = "Open the household database first.";
            return;
        }

        RebuildProposal();
        IsSuggestionEditor = true;
        EditorTitle = "Suggested starting budget";
        ErrorMessage = null;
        _originalFingerprint = Fingerprint();
        IsEditorOpen = true;
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(HasEditorChanges));
        OnPropertyChanged(nameof(EditorSaveLabel));
        OnPropertyChanged(nameof(IsExpenseEditor));
    }

    [RelayCommand]
    private void RecalculateSuggestions() => RebuildProposal();

    [RelayCommand]
    private void AcceptAllSafe()
    {
        foreach (var item in SuggestionLines.Where(line => line.Line.IsSafeRecommendation))
        {
            item.Decision = SuggestionDecision.Accept;
        }

        OnPropertyChanged(nameof(SuggestionLines));
        OnPropertyChanged(nameof(HasEditorChanges));
    }

    [RelayCommand]
    private void RestoreSuggested(SuggestionReviewItem? item)
    {
        if (item is null)
        {
            return;
        }

        item.AdjustedAmount = AmountParsing.Format(item.Line.Amount);
        item.Decision = item.Line.IsSafeRecommendation ? SuggestionDecision.Accept : SuggestionDecision.Defer;
        OnPropertyChanged(nameof(SuggestionLines));
    }

    [RelayCommand]
    private async Task SaveSuggestedBudgetAsync(CancellationToken cancellationToken)
    {
        if (!_session.IsOpen || Proposal is null)
        {
            ErrorMessage = "Open the suggested starting budget first.";
            return;
        }

        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        var applied = SuggestedBudgetPlanner.Apply(
            _session.Document,
            SuggestionLines.Select(item => item.ToLine()),
            today);
        if (!await CommitAsync(applied, cancellationToken))
        {
            return;
        }

        CloseEditor();
        StatusMessage = "Suggested budget saved";
    }

    partial void OnSelectedScenarioChanged(IncomeEstimate value)
    {
        if (IsSuggestionEditor)
        {
            RebuildProposal();
        }
    }

    private void RebuildProposal()
    {
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        Proposal = SuggestedBudgetPlanner.Propose(_session.Document, today, SelectedScenario);
        SuggestionLines = Proposal.Lines.Select(line => new SuggestionReviewItem(line)).ToList();
        OnPropertyChanged(nameof(Proposal));
        OnPropertyChanged(nameof(SuggestionLines));
        OnPropertyChanged(nameof(SuggestionSummary));
    }

    private static string DefaultCoverageExplanation(HouseholdCostCoverage coverage) => coverage switch
    {
        HouseholdCostCoverage.IncludedInAnotherPayment => "Included in rent",
        HouseholdCostCoverage.EmployerCovered => "Employer-covered",
        HouseholdCostCoverage.Reimbursed => "Reimbursed",
        HouseholdCostCoverage.NotApplicable => "Not applicable",
        HouseholdCostCoverage.Inactive => "Inactive",
        _ => string.Empty
    };
}
