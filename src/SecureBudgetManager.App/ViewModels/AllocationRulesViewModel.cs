using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureBudgetManager.App.Services;
using SecureBudgetManager.Core.Guidance;
using SecureBudgetManager.Core.Models;

namespace SecureBudgetManager.App.ViewModels;

public sealed record HierarchyRuleRow(string Category, string Tier, string Protected);

/// <summary>
/// The agreed rules that turn a paycheque into allocations. The resulting numbers are derived;
/// these inputs are what must be persisted so a past recommendation can be reproduced.
/// </summary>
public sealed partial class AllocationRulesViewModel : PageViewModel
{
    private readonly IBudgetSession _session;
    private readonly IUserDialog _dialog;

    public AllocationRulesViewModel(IBudgetSession session, IUserDialog dialog)
        : base(
            "Allocation rules",
            "Agreements",
            "How this household turns a paycheque into protected needs, reservations and personal " +
            "allowances. Conservative income protects essentials unless you explicitly choose otherwise.")
    {
        _session = session;
        _dialog = dialog;
        _session.Changed += OnSessionChanged;
        Refresh();
    }

    public IReadOnlyList<ChoiceOption<IncomeBasis>> BasisOptions { get; } =
        Enum.GetValues<IncomeBasis>()
            .Select(basis => new ChoiceOption<IncomeBasis>(basis, basis.ToDisplayName()))
            .ToList();

    public IReadOnlyList<ChoiceOption<DiscretionaryMethod>> MethodOptions { get; } =
    [
        new(DiscretionaryMethod.SamePercentageOfOwnRemaining, "Same percentage of each person's remaining income"),
        new(DiscretionaryMethod.ProportionalToUsableNetIncome, "Proportional to usable net income"),
        new(DiscretionaryMethod.FixedAmountEach, "Fixed dollar amount for each eligible earner"),
        new(DiscretionaryMethod.CustomPercentages, "Custom personal percentages"),
        new(DiscretionaryMethod.AllRemainingPersonalMoney, "All remaining personal money"),
        new(DiscretionaryMethod.None, "No discretionary allocation this period")
    ];

    public IReadOnlyList<ChoiceOption<NeedTier>> TierOptions { get; } =
        NeedTierExtensions.All
            .Select(tier => new ChoiceOption<NeedTier>(tier, tier.ToDisplayName()))
            .ToList();

    [ObservableProperty] private IncomeBasis basis = IncomeBasis.Conservative;
    [ObservableProperty] private bool optimisticMayFundEssentials;
    [ObservableProperty] private string safetyBuffer = "0";
    [ObservableProperty] private DiscretionaryMethod discretionaryMethod = DiscretionaryMethod.SamePercentageOfOwnRemaining;
    [ObservableProperty] private string discretionaryPercent = "10";
    [ObservableProperty] private string fixedDiscretionary = "0";
    [ObservableProperty] private string sharedEntertainment = "0";
    [ObservableProperty] private string flexibleSavings = "0";
    [ObservableProperty] private string notes = string.Empty;
    [ObservableProperty] private string categoryName = string.Empty;
    [ObservableProperty] private NeedTier categoryTier = NeedTier.Stability;
    [ObservableProperty] private bool categoryAlwaysProtected;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private string? statusMessage;
    [ObservableProperty] private string defaultFundingText = string.Empty;
    [ObservableProperty] private string chosenFundingText = string.Empty;
    [ObservableProperty] private string defaultReductionText = string.Empty;
    [ObservableProperty] private string chosenReductionText = string.Empty;
    [ObservableProperty] private string orderEffectText = string.Empty;
    [ObservableProperty] private FundingBucket selectedFundingBucket = FundingBucket.FamilyAndBelonging;
    [ObservableProperty] private ReducibleBucket selectedReductionBucket = ReducibleBucket.UnallocatedSurplus;

    public IReadOnlyList<HierarchyRuleRow> HierarchyRules { get; private set; } = [];

    public IReadOnlyList<ChoiceOption<FundingBucket>> FundingBucketOptions { get; } =
        Enum.GetValues<FundingBucket>()
            .Select(bucket => new ChoiceOption<FundingBucket>(bucket, bucket.ToDisplayName()))
            .ToList();

    public IReadOnlyList<ChoiceOption<ReducibleBucket>> ReductionBucketOptions { get; } =
        Enum.GetValues<ReducibleBucket>()
            .Select(bucket => new ChoiceOption<ReducibleBucket>(bucket, bucket.ToDisplayName()))
            .ToList();

    private List<FundingBucket> _fundingOrder = [.. AllocationRules.DefaultFundingOrder];
    private List<ReducibleBucket> _reductionOrder = [.. AllocationRules.DefaultReductionOrder];

    [RelayCommand]
    private async Task SaveRulesAsync(CancellationToken cancellationToken)
    {
        if (!_session.IsOpen)
        {
            ErrorMessage = "Open the household database first.";
            return;
        }

        if (!AmountParsing.TryParseMoney(SafetyBuffer, out var buffer)
            || !AmountParsing.TryParseMoney(FixedDiscretionary, out var fixedAmount)
            || !AmountParsing.TryParseMoney(SharedEntertainment, out var entertainment)
            || !AmountParsing.TryParseMoney(FlexibleSavings, out var flexible)
            || buffer.IsNegative
            || fixedAmount.IsNegative || entertainment.IsNegative || flexible.IsNegative)
        {
            ErrorMessage = "Enter a safety buffer and non-negative amounts.";
            return;
        }

        var percentConfigured = AmountParsing.TryParseDecimal(DiscretionaryPercent, out var percent)
            && DiscretionaryPercent.Trim().Length > 0;
        if (percentConfigured && percent is < 0m or > 100m)
        {
            ErrorMessage = "The discretionary percentage must be between 0 and 100.";
            return;
        }

        if (DiscretionaryMethod == DiscretionaryMethod.SamePercentageOfOwnRemaining && !percentConfigured)
        {
            percent = 0m;
        }
        else if (!percentConfigured)
        {
            percent = 0m;
        }

        if (OptimisticMayFundEssentials
            && !_dialog.Confirm(
                "Optimistic income",
                "Essentials would then be funded from the optimistic figure. A quieter week would leave those obligations short. Continue?"))
        {
            return;
        }

        var rules = _session.Document.Rules with
        {
            Basis = Basis,
            OptimisticIncomeMayFundEssentials = OptimisticMayFundEssentials,
            SafetyBuffer = buffer,
            DiscretionaryMethod = DiscretionaryMethod,
            DiscretionaryPercent = percent,
            DiscretionaryPercentConfigured = percentConfigured,
            FixedDiscretionaryAmount = fixedAmount,
            SharedEntertainmentPerPayday = entertainment,
            FlexibleSavingsPerPayday = flexible,
            FundingOrder = _fundingOrder.ToList(),
            ReductionOrder = _reductionOrder.ToList(),
            Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim()
        };

        if (!_session.TryReplace(_session.Document with { Rules = rules }, out var error))
        {
            ErrorMessage = error;
            return;
        }

        ErrorMessage = null;
        StatusMessage = await _session.SaveAsync(cancellationToken)
            ? "Allocation rules saved."
            : _session.LastError ?? "The rules could not be saved.";
    }

    [RelayCommand]
    private async Task SaveHierarchyRuleAsync(CancellationToken cancellationToken)
    {
        if (!_session.IsOpen)
        {
            ErrorMessage = "Open the household database first.";
            return;
        }

        if (string.IsNullOrWhiteSpace(CategoryName))
        {
            ErrorMessage = "Name the spending category this rule applies to.";
            return;
        }

        var hierarchy = _session.Document.Hierarchy.With(new NeedCategoryRule
        {
            CategoryName = CategoryName.Trim(),
            Tier = CategoryTier,
            IsAlwaysProtected = CategoryAlwaysProtected
        });

        if (!_session.TryReplace(_session.Document with { Hierarchy = hierarchy }, out var error))
        {
            ErrorMessage = error;
            return;
        }

        ErrorMessage = null;
        CategoryName = string.Empty;
        StatusMessage = await _session.SaveAsync(cancellationToken)
            ? "Hierarchy rule saved."
            : _session.LastError ?? "The hierarchy rule could not be saved.";
    }

    [RelayCommand]
    private void MoveFundingEarlier()
    {
        Move(_fundingOrder, SelectedFundingBucket, -1);
        RefreshOrderDisplay();
    }

    [RelayCommand]
    private void MoveFundingLater()
    {
        Move(_fundingOrder, SelectedFundingBucket, 1);
        RefreshOrderDisplay();
    }

    [RelayCommand]
    private void RestoreDefaultFunding()
    {
        _fundingOrder = [.. AllocationRules.DefaultFundingOrder];
        RefreshOrderDisplay();
    }

    [RelayCommand]
    private void MoveReductionEarlier()
    {
        Move(_reductionOrder, SelectedReductionBucket, -1);
        RefreshOrderDisplay();
    }

    [RelayCommand]
    private void MoveReductionLater()
    {
        Move(_reductionOrder, SelectedReductionBucket, 1);
        RefreshOrderDisplay();
    }

    [RelayCommand]
    private void RestoreDefaultReduction()
    {
        _reductionOrder = [.. AllocationRules.DefaultReductionOrder];
        RefreshOrderDisplay();
    }

    private static void Move<T>(List<T> order, T item, int delta)
    {
        var index = order.IndexOf(item);
        var target = index + delta;

        if (index < 0 || target < 0 || target >= order.Count)
        {
            return;
        }

        (order[index], order[target]) = (order[target], order[index]);
    }

    [RelayCommand]
    private async Task RestoreDefaultHierarchyAsync(CancellationToken cancellationToken)
    {
        if (!_session.IsOpen)
        {
            return;
        }

        if (!_dialog.Confirm("Restore default hierarchy", "Replace the household's category mapping with the built-in Survival-to-Discretionary defaults?"))
        {
            return;
        }

        if (!_session.TryReplace(_session.Document with { Hierarchy = NeedsHierarchy.Default }, out var error))
        {
            ErrorMessage = error;
            return;
        }

        ErrorMessage = null;
        StatusMessage = await _session.SaveAsync(cancellationToken)
            ? "Default needs hierarchy restored."
            : _session.LastError ?? "The hierarchy could not be restored.";
    }

    private void OnSessionChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        if (!_session.IsOpen)
        {
            HierarchyRules = [];
            Basis = IncomeBasis.Conservative;
            OptimisticMayFundEssentials = false;
            SafetyBuffer = string.Empty;
            DiscretionaryPercent = string.Empty;
            FixedDiscretionary = string.Empty;
            SharedEntertainment = string.Empty;
            FlexibleSavings = string.Empty;
            Notes = string.Empty;
            CategoryName = string.Empty;
            DefaultFundingText = string.Empty;
            ChosenFundingText = string.Empty;
            DefaultReductionText = string.Empty;
            ChosenReductionText = string.Empty;
            OrderEffectText = string.Empty;
            StatusMessage = null;
            ErrorMessage = null;
            OnPropertyChanged(nameof(HierarchyRules));
            return;
        }

        var rules = _session.Document.Rules;
        Basis = rules.Basis;
        OptimisticMayFundEssentials = rules.OptimisticIncomeMayFundEssentials;
        SafetyBuffer = AmountParsing.Format(rules.SafetyBuffer);
        DiscretionaryMethod = rules.DiscretionaryMethod;
        DiscretionaryPercent = rules.DiscretionaryPercentConfigured
            ? AmountParsing.Format(rules.DiscretionaryPercent)
            : string.Empty;
        FixedDiscretionary = AmountParsing.Format(rules.FixedDiscretionaryAmount);
        SharedEntertainment = AmountParsing.Format(rules.SharedEntertainmentPerPayday);
        FlexibleSavings = AmountParsing.Format(rules.FlexibleSavingsPerPayday);
        Notes = rules.Notes ?? string.Empty;
        HierarchyRules = _session.Document.Hierarchy.Rules
            .Select(rule => new HierarchyRuleRow(
                rule.CategoryName,
                rule.Tier.ToDisplayName(),
                rule.IsAlwaysProtected ? "Always protected" : rule.Tier.IsProtectedByDefault() ? "Protected by tier" : "Reducible"))
            .ToList();
        _fundingOrder = rules.FundingOrder.Count == 0
            ? [.. AllocationRules.DefaultFundingOrder]
            : [.. rules.FundingOrder];
        _reductionOrder = rules.ReductionOrder.Count == 0
            ? [.. AllocationRules.DefaultReductionOrder]
            : [.. rules.ReductionOrder];
        RefreshOrderDisplay();
        OnPropertyChanged(nameof(HierarchyRules));
    }

    private void RefreshOrderDisplay()
    {
        DefaultFundingText = Format(AllocationRules.DefaultFundingOrder, bucket => bucket.ToDisplayName());
        ChosenFundingText = Format(_fundingOrder, bucket => bucket.ToDisplayName());
        DefaultReductionText = Format(AllocationRules.DefaultReductionOrder, bucket => bucket.ToDisplayName());
        ChosenReductionText = Format(_reductionOrder, bucket => bucket.ToDisplayName());

        var leftover = new Money(100m);
        var entertainment = new Money(40m);
        var goals = new Money(40m);
        var discretionary = new Money(40m);
        var flexible = new Money(40m);
        var defaultFill = AllocationOrder.AssignByFundingOrder(
            leftover, entertainment, goals, discretionary, flexible, AllocationRules.DefaultFundingOrder);
        var chosenFill = AllocationOrder.AssignByFundingOrder(
            leftover, entertainment, goals, discretionary, flexible, _fundingOrder);
        var defaultCut = AllocationOrder.FillConfiguredOrReduce(
            leftover, entertainment, goals, discretionary, flexible, AllocationRules.DefaultReductionOrder);
        var chosenCut = AllocationOrder.FillConfiguredOrReduce(
            leftover, entertainment, goals, discretionary, flexible, _reductionOrder);

        OrderEffectText =
            "Worked example with $100 leftover after essentials and $40 requested in entertainment, " +
            "goals, discretionary and flexible savings. Protected obligations are never reduced. " +
            $"Default funding fill: entertainment {defaultFill.Entertainment.ToDisplayString()}, " +
            $"goals {defaultFill.Goals.ToDisplayString()}, discretionary {defaultFill.Discretionary.ToDisplayString()}, " +
            $"flexible {defaultFill.FlexibleSavings.ToDisplayString()}, surplus {defaultFill.Surplus.ToDisplayString()}. " +
            $"Chosen funding fill: entertainment {chosenFill.Entertainment.ToDisplayString()}, " +
            $"goals {chosenFill.Goals.ToDisplayString()}, discretionary {chosenFill.Discretionary.ToDisplayString()}, " +
            $"flexible {chosenFill.FlexibleSavings.ToDisplayString()}, surplus {chosenFill.Surplus.ToDisplayString()}. " +
            $"Default reduction of the same shortage: entertainment {defaultCut.Entertainment.ToDisplayString()}, " +
            $"goals {defaultCut.Goals.ToDisplayString()}, discretionary {defaultCut.Discretionary.ToDisplayString()}, " +
            $"flexible {defaultCut.FlexibleSavings.ToDisplayString()}, surplus {defaultCut.Surplus.ToDisplayString()}. " +
            $"Chosen reduction: entertainment {chosenCut.Entertainment.ToDisplayString()}, " +
            $"goals {chosenCut.Goals.ToDisplayString()}, discretionary {chosenCut.Discretionary.ToDisplayString()}, " +
            $"flexible {chosenCut.FlexibleSavings.ToDisplayString()}, surplus {chosenCut.Surplus.ToDisplayString()}. " +
            "A custom order that starves a protected essential is rejected at allocation time; " +
            "essentials stay visible as required / funded / unfunded.";
    }

    private static string Format<T>(IEnumerable<T> items, Func<T, string> name) =>
        string.Join(Environment.NewLine, items.Select((item, index) => $"{index + 1}. {name(item)}"));
}
