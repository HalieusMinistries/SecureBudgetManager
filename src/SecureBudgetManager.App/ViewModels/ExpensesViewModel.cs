using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureBudgetManager.App.Services;
using SecureBudgetManager.Core.Budgeting;
using SecureBudgetManager.Core.Expenses;
using SecureBudgetManager.Core.Household;
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
    string Status);

public sealed partial class ExpensesViewModel : PageViewModel
{
    private readonly IBudgetSession _session;
    private readonly IUserDialog _dialog;
    private Guid? _editingId;
    private string _originalFingerprint = string.Empty;

    public ExpensesViewModel(IBudgetSession session, IUserDialog dialog)
        : base(
            "Expenses",
            "Money out",
            "Bills and other recurring costs stay on this computer. Average monthly figures use the annual total divided by 12, not four weeks.")
    {
        _session = session;
        _dialog = dialog;
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
        Refresh();
    }

    public IReadOnlyList<ChoiceOption<string>> CategoryOptions { get; }

    public IReadOnlyList<ChoiceOption<Frequency>> FrequencyOptions { get; }

    public IReadOnlyList<ChoiceOption<ExpenseNecessity>> NecessityOptions { get; }

    public IReadOnlyList<ChoiceOption<ExpenseVariability>> VariabilityOptions { get; }

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
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    [ObservableProperty]
    private string? statusMessage;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

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
        StatusMessage = "Expense saved.";
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
        EditorIsActive);

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
        _editingId = null;
        EditorName = string.Empty;
        EditorAmount = string.Empty;
        ErrorMessage = null;
        _originalFingerprint = string.Empty;
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(HasEditorChanges));
    }

    private async Task<bool> CommitAsync(BudgetDocument document, CancellationToken cancellationToken)
    {
        ErrorMessage = null;

        if (!_session.TryReplace(document, out var error))
        {
            ErrorMessage = error;
            return false;
        }

        if (await _session.SaveAsync(cancellationToken))
        {
            return true;
        }

        ErrorMessage = _session.LastError ?? "The household data could not be saved.";
        return false;
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
                expense.IsPaused ? "Inactive" : "Active");
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
}
