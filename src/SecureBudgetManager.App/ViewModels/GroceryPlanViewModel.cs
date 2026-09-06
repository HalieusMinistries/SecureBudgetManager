using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureBudgetManager.App.Interaction;
using SecureBudgetManager.App.Services;
using SecureBudgetManager.Core.Guidance;
using SecureBudgetManager.Core.Models;

namespace SecureBudgetManager.App.ViewModels;

public sealed record GroceryCategoryRow(
    Guid Id,
    string Name,
    string Essential,
    string Suggested,
    string Ranges,
    string WeeklyLimit,
    string Spent,
    string Remaining,
    string CarriedForward,
    string Rollover,
    string Source,
    string EffectiveDate,
    string Confidence,
    string ReviewState,
    bool IsSelected = false);

public sealed record AssistanceCategoryChoice(Guid Id, string Name, bool IsSupplied);

/// <summary>
/// Groceries by category rather than one figure, because "we spend too much on food" is not
/// something a household can act on until it can see which part of the shop it is.
/// </summary>
public sealed partial class GroceryPlanViewModel : PageViewModel, IEditablePage
{
    private readonly IBudgetSession _session;
    private readonly IUserDialog _dialog;
    private readonly TimeProvider _clock;
    private string _originalFingerprint = string.Empty;
    private int _saveDepth;

    public GroceryPlanViewModel(IBudgetSession session, IUserDialog dialog, TimeProvider clock)
        : base(
            "Grocery plan",
            "Food",
            "A weekly plan by grocery category, with local guidance where it exists and an honest " +
            "\"insufficient local pricing information\" where it does not. Dining out is kept " +
            "separate from essential food.")
    {
        _session = session;
        _dialog = dialog;
        _clock = clock;
        _session.Changed += OnSessionChanged;
        Refresh();
    }

    public IReadOnlyList<ChoiceOption<GroceryPlanKind>> PlanKinds { get; } =
    [
        new(GroceryPlanKind.Current, GroceryPlanKind.Current.ToDisplayName()),
        new(GroceryPlanKind.FallbackWithoutAssistance, GroceryPlanKind.FallbackWithoutAssistance.ToDisplayName()),
        new(GroceryPlanKind.EmergencyMinimum, GroceryPlanKind.EmergencyMinimum.ToDisplayName())
    ];

    public IReadOnlyList<ChoiceOption<RolloverRule>> RolloverRules { get; } =
    [
        new(RolloverRule.ReturnToHousehold, "Return unspent money to the household"),
        new(RolloverRule.CarryForward, "Carry unspent money forward")
    ];

    public IReadOnlyList<ChoiceOption<string>> AssistanceStatuses { get; } =
    [
        new("not-expected", "Not expected"),
        new("expected", "Expected — not assumed permanent"),
        new("suspended", "Temporarily suspended")
    ];

    [ObservableProperty]
    private GroceryPlanKind selectedKind = GroceryPlanKind.Current;

    [ObservableProperty]
    private string planSummary = string.Empty;

    [ObservableProperty]
    private string assistanceSummary = string.Empty;

    [ObservableProperty]
    private string fallbackSummary = string.Empty;

    [ObservableProperty]
    private string emergencySummary = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditorTitle))]
    [NotifyPropertyChangedFor(nameof(EditorSaveLabel))]
    [NotifyPropertyChangedFor(nameof(IsCategoryEditor))]
    private bool isEditorOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditorTitle))]
    [NotifyPropertyChangedFor(nameof(EditorSaveLabel))]
    [NotifyPropertyChangedFor(nameof(IsCategoryEditor))]
    private bool isAssistanceEditor;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditorTitle))]
    private GroceryCategoryRow? selectedCategory;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSelected))]
    private bool isAssistanceSelected;

    public bool IsSelected => IsAssistanceSelected;

    [ObservableProperty]
    private double listScrollOffset;

    [ObservableProperty]
    private Guid? selectedRecordId;

    [ObservableProperty]
    private string categoryLimit = string.Empty;

    [ObservableProperty]
    private RolloverRule categoryRollover = RolloverRule.ReturnToHousehold;

    [ObservableProperty]
    private bool categoryIsEssential = true;

    [ObservableProperty]
    private bool categorySuppliedByAssistance;

    [ObservableProperty]
    private string newCategoryName = string.Empty;

    [ObservableProperty]
    private bool assistanceExpected;

    [ObservableProperty]
    private bool assistanceSuspended;

    [ObservableProperty]
    private string selectedAssistanceStatus = "not-expected";

    [ObservableProperty]
    private string assistanceSource = string.Empty;

    [ObservableProperty]
    private string assistanceValue = string.Empty;

    [ObservableProperty]
    private DateTime? assistanceEffective;

    [ObservableProperty]
    private DateTime? assistanceReview;

    [ObservableProperty]
    private string assistanceNotes = string.Empty;

    [ObservableProperty]
    private string cashStillRequired = string.Empty;

    [ObservableProperty]
    private string? statusMessage;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private string? focusField;

    public IReadOnlyDictionary<string, string> FieldErrors { get; private set; } = EditorSaveResult.NoFieldErrors;

    public IReadOnlyList<GroceryCategoryRow> Categories { get; private set; } = [];

    public IReadOnlyList<AssistanceCategoryChoice> AssistanceCategories { get; private set; } = [];

    public IReadOnlyList<string> Warnings { get; private set; } = [];

    public bool HasWarnings => Warnings.Count > 0;

    public bool HasPlan { get; private set; }

    public bool HasAssistanceRecorded { get; private set; }

    public bool CanAddAssistance => HasPlan && !HasAssistanceRecorded;

    public bool CanEditAssistance => HasPlan && HasAssistanceRecorded;

    public bool IsCategoryEditor => IsEditorOpen && !IsAssistanceEditor;

    public string AssistanceFrequencyText =>
        "Weekly (assistance value is stored as a weekly amount)";

    public string AssistanceNotPermanentNote =>
        "Assistance is not assumed to be permanent. The fallback plan remains the cash picture if it ends.";

    public string EditorTitle => IsAssistanceEditor
        ? HasAssistanceRecorded ? "Edit assistance" : "Add assistance"
        : SelectedCategory is { } category
            ? category.Name
            : "Grocery category";

    public string EditorSaveLabel => IsAssistanceEditor ? "Save assistance" : "Save category";

    public string? EditorEffectPreview =>
        IsAssistanceEditor
            ? string.IsNullOrWhiteSpace(CashStillRequired) ? AssistanceNotPermanentNote : CashStillRequired
            : "A weekly grocery limit is the most you intend to spend in this part of the shop.";

    public bool HasEditorChanges => IsEditorOpen && Fingerprint() != _originalFingerprint;

    public bool HasEditorError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public string? EditorError => ErrorMessage;

    public ICommand SaveEditorCommand => IsAssistanceEditor ? SaveAssistanceCommand : SaveCategoryCommand;

    public ICommand CancelEditorCommand => CancelEditorAliasCommand;

    public bool TryLeaveEditor()
    {
        if (!IsEditorOpen)
        {
            return true;
        }

        if (!HasEditorChanges
            || _dialog.Confirm("Unsaved changes", "Close without saving this grocery category or assistance?"))
        {
            DismissEditor();
            return true;
        }

        return false;
    }

    public void DismissEditor()
    {
        IsEditorOpen = false;
        IsAssistanceEditor = false;
        ErrorMessage = null;
    }

    private string Fingerprint() =>
        IsAssistanceEditor
            ? $"{SelectedAssistanceStatus}|{AssistanceSource}|{AssistanceValue}|{AssistanceEffective}|{AssistanceReview}|{AssistanceNotes}|{string.Join(',', AssistanceCategories.Where(item => item.IsSupplied).Select(item => item.Id))}"
            : $"{SelectedCategory?.Id}|{CategoryLimit}|{CategoryRollover}|{CategoryIsEssential}|{CategorySuppliedByAssistance}";

    [RelayCommand]
    private void CancelEditorAlias() => DismissEditor();

    [RelayCommand]
    private void SelectCategory(GroceryCategoryRow? row)
    {
        if (row is null)
        {
            return;
        }

        SelectedRecordId = row.Id;
        IsAssistanceSelected = false;
        SelectedCategory = Categories.FirstOrDefault(item => item.Id == row.Id) ?? row;
        RematchSelection();
    }

    [RelayCommand]
    private void SelectAssistance()
    {
        if (!HasPlan)
        {
            return;
        }

        SelectedRecordId = null;
        IsAssistanceSelected = true;
        RematchSelection();
    }

    [RelayCommand]
    private void OpenCategory(GroceryCategoryRow? row)
    {
        if (row is null || !TryLeaveEditor())
        {
            return;
        }

        SelectedRecordId = row.Id;
        IsAssistanceSelected = false;
        IsAssistanceEditor = false;
        LoadCategory(row);
        ErrorMessage = null;
        _originalFingerprint = Fingerprint();
        IsEditorOpen = true;
        OnPropertyChanged(nameof(EditorTitle));
        RematchSelection();
    }

    [RelayCommand]
    private void BeginAddAssistance()
    {
        if (!HasPlan || !TryLeaveEditor())
        {
            return;
        }

        IsAssistanceSelected = true;
        SelectedRecordId = null;
        IsAssistanceEditor = true;
        SelectedAssistanceStatus = "not-expected";
        AssistanceExpected = false;
        AssistanceSuspended = false;
        AssistanceSource = string.Empty;
        AssistanceValue = string.Empty;
        AssistanceEffective = null;
        AssistanceReview = null;
        AssistanceNotes = string.Empty;
        AssistanceCategories = Categories
            .Select(category => new AssistanceCategoryChoice(category.Id, category.Name, false))
            .ToList();
        RefreshAssistanceContext();
        ErrorMessage = null;
        _originalFingerprint = Fingerprint();
        IsEditorOpen = true;
        OnPropertyChanged(nameof(AssistanceCategories));
        OnPropertyChanged(nameof(EditorTitle));
        OnPropertyChanged(nameof(EditorEffectPreview));
        RematchSelection();
    }

    [RelayCommand]
    private void BeginEditAssistance()
    {
        if (!HasPlan || !_session.IsOpen || !TryLeaveEditor())
        {
            return;
        }

        var plan = _session.Document.GroceryPlanOf(SelectedKind);
        if (plan is null)
        {
            return;
        }

        IsAssistanceSelected = true;
        SelectedRecordId = null;
        IsAssistanceEditor = true;
        LoadAssistance(plan);
        RefreshAssistanceContext();
        ErrorMessage = null;
        _originalFingerprint = Fingerprint();
        IsEditorOpen = true;
        OnPropertyChanged(nameof(EditorTitle));
        OnPropertyChanged(nameof(EditorEffectPreview));
        RematchSelection();
    }

    [RelayCommand]
    private void OpenAssistance()
    {
        if (HasAssistanceRecorded)
        {
            BeginEditAssistance();
            return;
        }

        BeginAddAssistance();
    }

    [RelayCommand]
    private void ToggleAssistanceCategory(Guid id)
    {
        AssistanceCategories = AssistanceCategories
            .Select(item => item.Id == id ? item with { IsSupplied = !item.IsSupplied } : item)
            .ToList();
        OnPropertyChanged(nameof(AssistanceCategories));
    }

    private void LoadCategory(GroceryCategoryRow row)
    {
        SelectedCategory = Categories.FirstOrDefault(item => item.Id == row.Id) ?? row;
        if (!_session.IsOpen)
        {
            return;
        }

        var plan = _session.Document.GroceryPlanOf(SelectedKind);
        var category = plan?.Categories.FirstOrDefault(item => item.Id == row.Id);
        if (category is null)
        {
            return;
        }

        CategoryLimit = AmountParsing.Format(category.WeeklyLimit);
        CategoryRollover = category.Rollover;
        CategoryIsEssential = category.IsEssential;
        CategorySuppliedByAssistance = category.SuppliedByAssistance;
    }

    private void LoadAssistance(GroceryPlan plan)
    {
        var assistance = plan.Assistance;
        SelectedAssistanceStatus = assistance.IsSuspended
            ? "suspended"
            : assistance.IsExpected
                ? "expected"
                : "not-expected";
        AssistanceExpected = assistance.IsExpected;
        AssistanceSuspended = assistance.IsSuspended;
        AssistanceSource = assistance.SourceName ?? string.Empty;
        AssistanceValue = assistance.EstimatedWeeklyValue.IsZero
            ? string.Empty
            : AmountParsing.Format(assistance.EstimatedWeeklyValue);
        AssistanceEffective = assistance.EffectiveDate?.ToDateTime(TimeOnly.MinValue);
        AssistanceReview = assistance.ReviewDate?.ToDateTime(TimeOnly.MinValue);
        AssistanceNotes = assistance.Notes ?? string.Empty;
        AssistanceCategories = plan.Categories
            .Select(category => new AssistanceCategoryChoice(
                category.Id,
                category.Name,
                category.SuppliedByAssistance || assistance.Supplies(category.Name)))
            .ToList();
        OnPropertyChanged(nameof(AssistanceCategories));
    }

    private void RefreshAssistanceContext()
    {
        if (!_session.IsOpen)
        {
            CashStillRequired = string.Empty;
            OnPropertyChanged(nameof(EditorEffectPreview));
            return;
        }

        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        var document = _session.Document;
        var selected = document.GroceryPlanOf(SelectedKind);
        var requirement = selected is null ? null : GroceryPlanner.Require(selected, today);

        CashStillRequired = requirement is null
            ? "Create this plan to see cash still required after assistance."
            : $"{requirement.TotalCash.ToDisplayString()} cash still required this week after assistance that actually applies.";

        OnPropertyChanged(nameof(EditorEffectPreview));
    }

    partial void OnSelectedKindChanged(GroceryPlanKind value)
    {
        DismissEditor();
        Refresh();
    }

    partial void OnSelectedAssistanceStatusChanged(string value)
    {
        AssistanceExpected = value is "expected" or "suspended";
        AssistanceSuspended = value == "suspended";
    }

    [RelayCommand]
    private async Task CreatePlanAsync(CancellationToken cancellationToken)
    {
        if (!_session.IsOpen)
        {
            ErrorMessage = "Open the household database first.";
            return;
        }

        var document = _session.Document;

        if (document.GroceryPlanOf(SelectedKind) is not null)
        {
            ErrorMessage = $"A {SelectedKind.ToDisplayName().ToLowerInvariant()} already exists.";
            return;
        }

        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);

        var plan = GroceryPlanner.CreateDefault(
            SelectedKind,
            document.CostGuidance,
            document.Locality,
            document.Composition,
            today);

        var plans = document.GroceryPlans.ToList();
        plans.Add(plan);

        if (!_session.TryReplace(document with { GroceryPlans = plans }, out var error))
        {
            ErrorMessage = error;
            return;
        }

        ErrorMessage = null;
        StatusMessage = await _session.SaveAsync(cancellationToken)
            ? $"{plan.Name} created with {plan.Categories.Count} categories."
            : _session.LastError ?? "The plan could not be saved.";
    }

    [RelayCommand]
    private async Task SaveCategoryAsync(CancellationToken cancellationToken)
    {
        if (!TryBeginSave())
        {
            return;
        }

        try
        {
            if (!_session.IsOpen)
            {
                ErrorMessage = "Open the household database first.";
                return;
            }

            if (SelectedCategory is not { } selected)
            {
                ErrorMessage = "Choose a grocery category first.";
                return;
            }

            if (!AmountParsing.TryParseMoney(CategoryLimit, out var limit) || limit.IsNegative)
            {
                ApplySaveResult(EditorSaveResult.Validation(
                    "Enter a weekly limit of zero or more.",
                    "CategoryLimit",
                    "Enter a weekly limit of zero or more."));
                return;
            }

            SelectedRecordId = selected.Id;
            if (!TryUpdatePlan(
                    plan => plan with
                    {
                        Categories = plan.Categories
                            .Select(category => category.Id == selected.Id
                                ? category with
                                {
                                    WeeklyLimit = limit,
                                    Rollover = CategoryRollover,
                                    IsEssential = CategoryIsEssential,
                                    SuppliedByAssistance = CategorySuppliedByAssistance
                                }
                                : category)
                            .ToList()
                    },
                    out var error))
            {
                ErrorMessage = error;
                return;
            }

            var result = await EditorSaveCoordinator.PersistCurrentAsync(
                _session,
                cancellationToken,
                $"{selected.Name} saved",
                document => document.GroceryPlanOf(SelectedKind)?.Categories
                    .FirstOrDefault(category => category.Id == selected.Id) is { } saved
                    && saved.WeeklyLimit == limit);
            ApplySaveResult(result);
            if (result.IsSuccess)
            {
                DismissEditor();
            }
        }
        catch (Exception exception)
        {
            ApplySaveResult(EditorSaveResult.NotSaved(UserFacingError.From(exception)));
        }
        finally
        {
            EndSave();
        }
    }

    [RelayCommand]
    private async Task AddCategoryAsync(CancellationToken cancellationToken)
    {
        if (!_session.IsOpen)
        {
            ErrorMessage = "Open the household database first.";
            return;
        }

        if (string.IsNullOrWhiteSpace(NewCategoryName))
        {
            ErrorMessage = "Enter a name for the new grocery category.";
            return;
        }

        var name = NewCategoryName.Trim();

        if (!TryUpdatePlan(
                plan =>
                {
                    var categories = plan.Categories.ToList();

                    categories.Add(new GroceryCategoryPlan
                    {
                        Id = Guid.NewGuid(),
                        Name = name,
                        IsEssential = CategoryIsEssential,
                        GuidanceSource = CostGuidanceLookup.NoLocalInformation
                    });

                    return plan with { Categories = categories };
                },
                out var error))
        {
            ErrorMessage = error;
            return;
        }

        ErrorMessage = null;
        NewCategoryName = string.Empty;

        StatusMessage = await _session.SaveAsync(cancellationToken)
            ? $"{name} added."
            : _session.LastError ?? "The category could not be added.";
    }

    [RelayCommand]
    private async Task RemoveCategoryAsync(GroceryCategoryRow? row, CancellationToken cancellationToken)
    {
        if (row is not null)
        {
            SelectedCategory = row;
        }

        if (!_session.IsOpen || SelectedCategory is not { } selected)
        {
            return;
        }

        if (!_dialog.Confirm("Remove category", $"Remove the grocery category {selected.Name}?"))
        {
            return;
        }

        if (!TryUpdatePlan(
                plan => plan with
                {
                    Categories = plan.Categories.Where(category => category.Id != selected.Id).ToList()
                },
                out var error))
        {
            ErrorMessage = error;
            return;
        }

        ErrorMessage = null;
        SelectedCategory = null;

        StatusMessage = await _session.SaveAsync(cancellationToken)
            ? $"{selected.Name} removed."
            : _session.LastError ?? "The category could not be removed.";
    }

    [RelayCommand]
    private async Task SaveAssistanceAsync(CancellationToken cancellationToken)
    {
        if (!TryBeginSave())
        {
            return;
        }

        try
        {
            if (!_session.IsOpen)
            {
                ErrorMessage = "Open the household database first.";
                return;
            }

            Money value = Money.Zero;
            if (!string.IsNullOrWhiteSpace(AssistanceValue))
            {
                if (!AmountParsing.TryParseMoney(AssistanceValue, out value) || value.IsNegative)
                {
                    ApplySaveResult(EditorSaveResult.Validation(
                        "Enter the estimated weekly value if it is known, or leave it blank.",
                        "AssistanceValue",
                        "Leave this blank when the value is unknown."));
                    return;
                }
            }

            var suppliedNames = AssistanceCategories
                .Where(item => item.IsSupplied)
                .Select(item => item.Name)
                .ToList();

            if (!TryUpdatePlan(
                    plan => plan with
                    {
                        Categories = plan.Categories
                            .Select(category => category with
                            {
                                SuppliedByAssistance = suppliedNames.Contains(category.Name, StringComparer.OrdinalIgnoreCase)
                            })
                            .ToList(),
                        Assistance = plan.Assistance with
                        {
                            IsExpected = SelectedAssistanceStatus is "expected" or "suspended",
                            IsSuspended = SelectedAssistanceStatus == "suspended",
                            SourceName = string.IsNullOrWhiteSpace(AssistanceSource) ? null : AssistanceSource.Trim(),
                            EstimatedWeeklyValue = value,
                            EffectiveDate = AssistanceEffective is { } effective
                                ? DateOnly.FromDateTime(effective)
                                : null,
                            ReviewDate = AssistanceReview is { } review ? DateOnly.FromDateTime(review) : null,
                            Notes = string.IsNullOrWhiteSpace(AssistanceNotes) ? null : AssistanceNotes.Trim(),
                            CategoriesSupplied = suppliedNames
                        }
                    },
                    out var error))
            {
                ErrorMessage = error;
                return;
            }

            var source = string.IsNullOrWhiteSpace(AssistanceSource) ? null : AssistanceSource.Trim();
            var result = await EditorSaveCoordinator.PersistCurrentAsync(
                _session,
                cancellationToken,
                "Assistance saved",
                document =>
                {
                    var saved = document.GroceryPlanOf(SelectedKind)?.Assistance;
                    return saved is not null
                           && saved.IsExpected == (SelectedAssistanceStatus is "expected" or "suspended")
                           && string.Equals(saved.SourceName, source, StringComparison.Ordinal)
                           && saved.EstimatedWeeklyValue == value;
                });
            ApplySaveResult(result);
            if (result.IsSuccess)
            {
                IsAssistanceSelected = true;
                DismissEditor();
            }
        }
        catch (Exception exception)
        {
            ApplySaveResult(EditorSaveResult.NotSaved(UserFacingError.From(exception)));
        }
        finally
        {
            EndSave();
        }
    }

    private bool TryUpdatePlan(Func<GroceryPlan, GroceryPlan> update, out string? error)
    {
        var document = _session.Document;
        var existing = document.GroceryPlanOf(SelectedKind);

        if (existing is null)
        {
            error = $"Create the {SelectedKind.ToDisplayName().ToLowerInvariant()} first.";
            return false;
        }

        var plans = document.GroceryPlans
            .Select(plan => plan.Kind == SelectedKind ? update(plan) : plan)
            .ToList();

        return _session.TryReplace(document with { GroceryPlans = plans }, out error);
    }

    private bool TryBeginSave()
    {
        if (System.Threading.Interlocked.Exchange(ref _saveDepth, 1) != 0)
        {
            return false;
        }

        return true;
    }

    private void EndSave() => System.Threading.Interlocked.Exchange(ref _saveDepth, 0);

    private void ApplySaveResult(EditorSaveResult result)
    {
        FieldErrors = result.FieldErrors;
        FocusField = result.FocusField;
        OnPropertyChanged(nameof(FieldErrors));
        if (result.IsSuccess)
        {
            ErrorMessage = null;
            StatusMessage = result.Message;
            return;
        }

        ErrorMessage = result.Message;
        if (result.Status != EditorSaveStatus.ValidationFailed)
        {
            StatusMessage = null;
        }
    }

    private static bool IsAssistanceRecorded(FoodAssistance assistance) =>
        assistance.IsExpected
        || assistance.IsSuspended
        || !string.IsNullOrWhiteSpace(assistance.SourceName)
        || !assistance.EstimatedWeeklyValue.IsZero
        || assistance.EffectiveDate is not null
        || assistance.ReviewDate is not null
        || !string.IsNullOrWhiteSpace(assistance.Notes)
        || assistance.CategoriesSupplied.Count > 0;

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        try
        {
            Refresh();
        }
        catch (Exception exception)
        {
            ErrorMessage = UserFacingError.From(exception);
        }
    }

    private void Refresh()
    {
        if (!_session.IsOpen)
        {
            Categories = [];
            AssistanceCategories = [];
            Warnings = [];
            PlanSummary = string.Empty;
            AssistanceSummary = string.Empty;
            FallbackSummary = string.Empty;
            EmergencySummary = string.Empty;
            CashStillRequired = string.Empty;
            CategoryLimit = string.Empty;
            NewCategoryName = string.Empty;
            AssistanceSource = string.Empty;
            AssistanceValue = string.Empty;
            AssistanceNotes = string.Empty;
            AssistanceEffective = null;
            AssistanceReview = null;
            AssistanceExpected = false;
            AssistanceSuspended = false;
            SelectedAssistanceStatus = "not-expected";
            SelectedCategory = null;
            SelectedRecordId = null;
            IsAssistanceSelected = false;
            HasPlan = false;
            HasAssistanceRecorded = false;
            StatusMessage = null;
            ErrorMessage = null;
            DismissEditor();
            Notify();
            return;
        }

        var document = _session.Document;
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        var plan = document.GroceryPlanOf(SelectedKind);
        HasPlan = plan is not null;
        HasAssistanceRecorded = plan is not null && IsAssistanceRecorded(plan.Assistance);

        if (plan is null)
        {
            Categories = [];
            AssistanceCategories = [];
            Warnings = [];
            PlanSummary = $"No {SelectedKind.ToDisplayName().ToLowerInvariant()} has been created yet.";
            AssistanceSummary = string.Empty;
            FallbackSummary = string.Empty;
            EmergencySummary = "No emergency minimum plan has been created.";
            CashStillRequired = string.Empty;
            if (IsEditorOpen)
            {
                DismissEditor();
            }

            Notify();
            return;
        }

        var requirement = GroceryPlanner.Require(plan, today);
        var spending = SpendingByCategory(today);

        Categories = plan.Categories
            .Select(category =>
            {
                var spent = spending.TryGetValue(category.Name, out var amount) ? amount : Money.Zero;

                return new GroceryCategoryRow(
                    category.Id,
                    category.Name,
                    category.IsEssential ? "Essential" : "Optional",
                    category.SuggestedWeekly.ToDisplayString(),
                    $"{category.LowRange.ToDisplayString()} low · " +
                    $"{category.TypicalRange.ToDisplayString()} typical · " +
                    $"{category.ComfortableRange.ToDisplayString()} comfortable",
                    category.WeeklyLimit.ToDisplayString(),
                    spent.ToDisplayString(),
                    category.RemainingAfter(spent).ToDisplayString(),
                    category.CarriedForward.ToDisplayString(),
                    category.Rollover == RolloverRule.CarryForward ? "Carries forward" : "Returns to household",
                    category.GuidanceSource,
                    category.GuidanceEffectiveDate?.ToString("yyyy-MM-dd") ?? "No effective date",
                    category.Confidence.ToString(),
                    category.ReviewRequired ? "Review required" : "Current",
                    category.Id == SelectedRecordId);
            })
            .ToList();

        SelectedCategory = SelectedRecordId is { } id
            ? Categories.FirstOrDefault(item => item.Id == id)
            : SelectedCategory is { } selected
                ? Categories.FirstOrDefault(item => item.Id == selected.Id)
                : null;

        PlanSummary =
            $"{requirement.TotalCash.ToDisplayString()} of cash a week " +
            $"({requirement.EssentialCash.ToDisplayString()} essential, " +
            $"{requirement.OptionalCash.ToDisplayString()} optional) across " +
            $"{requirement.CategoriesRequiringCash.Count} categories.";

        AssistanceSummary = plan.Assistance.IsExpected
            ? $"{requirement.CoveredByAssistance.ToDisplayString()} a week is expected in goods" +
              (plan.Assistance.SourceName is { } source ? $" from {source}" : string.Empty) +
              (plan.Assistance.IsSuspended
                  ? ". Assistance is currently suspended, so the full cash requirement applies."
                  : $", covering {requirement.CategoriesSuppliedByAssistance.Count} categories. " +
                    "This is not counted as income.")
            : "No food assistance is expected.";

        var fallback = document.GroceryPlanOf(GroceryPlanKind.FallbackWithoutAssistance);
        var current = document.GroceryPlanOf(GroceryPlanKind.Current);
        var emergency = document.GroceryPlanOf(GroceryPlanKind.EmergencyMinimum);

        FallbackSummary = current is null
            ? "Create a current plan to see what happens if assistance ends."
            : GroceryPlanner.IfAssistanceEnds(current, fallback, today).Explanation;

        EmergencySummary = emergency is null
            ? "No emergency minimum plan has been created."
            : $"{GroceryPlanner.Require(emergency, today).TotalCash.ToDisplayString()} cash a week on the emergency plan.";

        Warnings = requirement.Warnings;
        CashStillRequired =
            $"{requirement.TotalCash.ToDisplayString()} cash still required this week after assistance that actually applies.";

        if (!IsEditorOpen)
        {
            AssistanceExpected = plan.Assistance.IsExpected;
            AssistanceSuspended = plan.Assistance.IsSuspended;
            SelectedAssistanceStatus = plan.Assistance.IsSuspended
                ? "suspended"
                : plan.Assistance.IsExpected
                    ? "expected"
                    : "not-expected";
            AssistanceSource = plan.Assistance.SourceName ?? string.Empty;
            AssistanceValue = plan.Assistance.EstimatedWeeklyValue.IsZero
                ? string.Empty
                : AmountParsing.Format(plan.Assistance.EstimatedWeeklyValue);
            AssistanceEffective = plan.Assistance.EffectiveDate?.ToDateTime(TimeOnly.MinValue);
            AssistanceReview = plan.Assistance.ReviewDate?.ToDateTime(TimeOnly.MinValue);
            AssistanceNotes = plan.Assistance.Notes ?? string.Empty;
        }

        Notify();
    }

    /// <summary>
    /// Spending in the last seven days, matched to plan categories by name. Refunds net off, and a
    /// category that is net positive after refunds is shown as nothing spent rather than negative.
    /// </summary>
    private Dictionary<string, Money> SpendingByCategory(DateOnly today)
    {
        var weekStart = today.AddDays(-6);

        return _session.Document.Transactions
            .Where(transaction => transaction.Date >= weekStart && transaction.Date <= today)
            .GroupBy(transaction => transaction.Category.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => Money.Max(Money.Zero, Money.Sum(group.Select(item => item.SignedAmount)).Round()),
                StringComparer.OrdinalIgnoreCase);
    }

    private void RematchSelection()
    {
        Categories = Categories
            .Select(item => item with { IsSelected = !IsAssistanceSelected && item.Id == SelectedRecordId })
            .ToList();
        SelectedCategory = Categories.FirstOrDefault(item => item.Id == SelectedRecordId) ?? SelectedCategory;
        OnPropertyChanged(nameof(Categories));
        OnPropertyChanged(nameof(IsAssistanceSelected));
        OnPropertyChanged(nameof(IsSelected));
        OnPropertyChanged(nameof(EditorTitle));
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(Categories));
        OnPropertyChanged(nameof(AssistanceCategories));
        OnPropertyChanged(nameof(Warnings));
        OnPropertyChanged(nameof(HasWarnings));
        OnPropertyChanged(nameof(HasPlan));
        OnPropertyChanged(nameof(HasAssistanceRecorded));
        OnPropertyChanged(nameof(CanAddAssistance));
        OnPropertyChanged(nameof(CanEditAssistance));
        OnPropertyChanged(nameof(EditorTitle));
        OnPropertyChanged(nameof(IsCategoryEditor));
        OnPropertyChanged(nameof(EditorEffectPreview));
    }
}
