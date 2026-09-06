using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    string ReviewState);

/// <summary>
/// Groceries by category rather than one figure, because "we spend too much on food" is not
/// something a household can act on until it can see which part of the shop it is.
/// </summary>
public sealed partial class GroceryPlanViewModel : PageViewModel
{
    private readonly IBudgetSession _session;
    private readonly IUserDialog _dialog;
    private readonly TimeProvider _clock;

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

    [ObservableProperty]
    private GroceryPlanKind selectedKind = GroceryPlanKind.Current;

    [ObservableProperty]
    private string planSummary = string.Empty;

    [ObservableProperty]
    private string assistanceSummary = string.Empty;

    [ObservableProperty]
    private string fallbackSummary = string.Empty;

    [ObservableProperty]
    private GroceryCategoryRow? selectedCategory;

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
    private string assistanceSource = string.Empty;

    [ObservableProperty]
    private string assistanceValue = string.Empty;

    [ObservableProperty]
    private DateTime? assistanceEffective;

    [ObservableProperty]
    private DateTime? assistanceReview;

    [ObservableProperty]
    private string? statusMessage;

    [ObservableProperty]
    private string? errorMessage;

    public IReadOnlyList<GroceryCategoryRow> Categories { get; private set; } = [];

    public IReadOnlyList<string> Warnings { get; private set; } = [];

    public bool HasWarnings => Warnings.Count > 0;

    partial void OnSelectedKindChanged(GroceryPlanKind value) => Refresh();

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
            ErrorMessage = "Enter a weekly limit of zero or more.";
            return;
        }

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

        ErrorMessage = null;
        StatusMessage = await _session.SaveAsync(cancellationToken)
            ? $"{selected.Name} set to {limit.ToDisplayString()} a week."
            : _session.LastError ?? "The category could not be saved.";
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
    private async Task RemoveCategoryAsync(CancellationToken cancellationToken)
    {
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
        if (!_session.IsOpen)
        {
            ErrorMessage = "Open the household database first.";
            return;
        }

        if (!AmountParsing.TryParseMoney(
                string.IsNullOrWhiteSpace(AssistanceValue) ? "0" : AssistanceValue,
                out var value)
            || value.IsNegative)
        {
            ErrorMessage = "Enter the estimated weekly value of the assistance.";
            return;
        }

        if (!TryUpdatePlan(
                plan => plan with
                {
                    Assistance = plan.Assistance with
                    {
                        IsExpected = AssistanceExpected,
                        IsSuspended = AssistanceSuspended,
                        SourceName = string.IsNullOrWhiteSpace(AssistanceSource) ? null : AssistanceSource.Trim(),
                        EstimatedWeeklyValue = value,
                        EffectiveDate = AssistanceEffective is { } effective
                            ? DateOnly.FromDateTime(effective)
                            : null,
                        ReviewDate = AssistanceReview is { } review ? DateOnly.FromDateTime(review) : null,
                        CategoriesSupplied = plan.Categories
                            .Where(category => category.SuppliedByAssistance)
                            .Select(category => category.Name)
                            .ToList()
                    }
                },
                out var error))
        {
            ErrorMessage = error;
            return;
        }

        ErrorMessage = null;
        StatusMessage = await _session.SaveAsync(cancellationToken)
            ? "Food assistance saved. The fallback plan still shows the cash requirement without it."
            : _session.LastError ?? "The assistance details could not be saved.";
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

    partial void OnSelectedCategoryChanged(GroceryCategoryRow? value)
    {
        if (value is null || !_session.IsOpen)
        {
            return;
        }

        var plan = _session.Document.GroceryPlanOf(SelectedKind);
        var category = plan?.Categories.FirstOrDefault(item => item.Id == value.Id);

        if (category is null)
        {
            return;
        }

        CategoryLimit = AmountParsing.Format(category.WeeklyLimit);
        CategoryRollover = category.Rollover;
        CategoryIsEssential = category.IsEssential;
        CategorySuppliedByAssistance = category.SuppliedByAssistance;
    }

    private void OnSessionChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        if (!_session.IsOpen)
        {
            Categories = [];
            Warnings = [];
            PlanSummary = string.Empty;
            AssistanceSummary = string.Empty;
            FallbackSummary = string.Empty;
            CategoryLimit = string.Empty;
            NewCategoryName = string.Empty;
            AssistanceSource = string.Empty;
            AssistanceValue = string.Empty;
            AssistanceEffective = null;
            AssistanceReview = null;
            AssistanceExpected = false;
            AssistanceSuspended = false;
            SelectedCategory = null;
            StatusMessage = null;
            ErrorMessage = null;
            Notify();
            return;
        }

        var document = _session.Document;
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        var plan = document.GroceryPlanOf(SelectedKind);

        if (plan is null)
        {
            Categories = [];
            Warnings = [];
            PlanSummary = $"No {SelectedKind.ToDisplayName().ToLowerInvariant()} has been created yet.";
            AssistanceSummary = string.Empty;
            FallbackSummary = string.Empty;
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
                    category.ReviewRequired ? "Review required" : "Current");
            })
            .ToList();

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

        FallbackSummary = current is null
            ? "Create a current plan to see what happens if assistance ends."
            : GroceryPlanner.IfAssistanceEnds(current, fallback, today).Explanation;

        Warnings = requirement.Warnings;

        AssistanceExpected = plan.Assistance.IsExpected;
        AssistanceSuspended = plan.Assistance.IsSuspended;
        AssistanceSource = plan.Assistance.SourceName ?? string.Empty;
        AssistanceValue = AmountParsing.Format(plan.Assistance.EstimatedWeeklyValue);
        AssistanceEffective = plan.Assistance.EffectiveDate?.ToDateTime(TimeOnly.MinValue);
        AssistanceReview = plan.Assistance.ReviewDate?.ToDateTime(TimeOnly.MinValue);

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

    private void Notify()
    {
        OnPropertyChanged(nameof(Categories));
        OnPropertyChanged(nameof(Warnings));
        OnPropertyChanged(nameof(HasWarnings));
    }
}
