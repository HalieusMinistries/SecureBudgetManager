using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureBudgetManager.App.Interaction;
using SecureBudgetManager.App.Services;
using SecureBudgetManager.Core.Debt;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.App.ViewModels;

public sealed record DebtListItem(Guid Id, string Name, string Balance, string Apr, string Minimum);

public sealed partial class DebtViewModel : PageViewModel, IEditablePage
{
    private readonly IBudgetSession _session;
    private readonly IUserDialog _dialog;
    private readonly TimeProvider _clock;

    public DebtViewModel(IBudgetSession session, IUserDialog dialog, TimeProvider clock)
        : base(
            "Debt",
            "Payoff",
            "Snowball and avalanche use the same monthly budget. Avalanche usually costs less interest; snowball clears the smallest balance first.")
    {
        _session = session;
        _dialog = dialog;
        _clock = clock;
        _session.Changed += OnSessionChanged;
        Refresh();
    }

    public IReadOnlyList<ChoiceOption<DebtKind>> KindOptions { get; } =
    [
        new(DebtKind.CreditCard, "Credit card"),
        new(DebtKind.CarLoan, "Car loan"),
        new(DebtKind.PersonalLoan, "Personal loan"),
        new(DebtKind.StudentLoan, "Student loan"),
        new(DebtKind.MedicalDebt, "Medical"),
        new(DebtKind.Mortgage, "Mortgage"),
        new(DebtKind.Other, "Other")
    ];

    public IReadOnlyList<DebtListItem> Debts { get; private set; } = [];

    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private DebtKind kind = DebtKind.CreditCard;
    [ObservableProperty] private string balance = string.Empty;
    [ObservableProperty] private string apr = string.Empty;
    [ObservableProperty] private string minimum = string.Empty;
    [ObservableProperty] private string promotionalApr = string.Empty;
    [ObservableProperty] private DateTime? promotionalEnds;
    [ObservableProperty] private string extraBudget = "0";
    [ObservableProperty] private string comparisonText = string.Empty;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private string? statusMessage;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditorTitle))]
    private bool isEditorOpen;
    private Guid? _editingId;
    private string _originalFingerprint = string.Empty;

    public string EditorTitle => _editingId is null ? "Add a loan or card" : "Edit a loan or card";

    public string EditorSaveLabel => "Save debt";

    public string? EditorEffectPreview =>
        string.IsNullOrWhiteSpace(ComparisonText) ? null : ComparisonText;

    public bool HasEditorChanges => IsEditorOpen && DebtFingerprint() != _originalFingerprint;

    public bool HasEditorError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public string? EditorError => ErrorMessage;

    public ICommand SaveEditorCommand => AddDebtCommand;

    public ICommand CancelEditorCommand => CancelDebtEditorCommand;

    public bool TryLeaveEditor()
    {
        if (!IsEditorOpen)
        {
            return true;
        }

        if (!HasEditorChanges || _dialog.Confirm("Unsaved changes", "Close without saving this debt?"))
        {
            DismissEditor();
            return true;
        }

        return false;
    }

    public void DismissEditor()
    {
        IsEditorOpen = false;
        _editingId = null;
        ErrorMessage = null;
    }

    private string DebtFingerprint() =>
        $"{Name}|{Kind}|{Balance}|{Apr}|{Minimum}|{PromotionalApr}|{PromotionalEnds}";

    [RelayCommand]
    private void CancelDebtEditor() => DismissEditor();

    [RelayCommand]
    private void BeginAdd()
    {
        if (!TryLeaveEditor())
        {
            return;
        }

        _editingId = null;
        Name = string.Empty;
        Balance = string.Empty;
        Apr = string.Empty;
        Minimum = string.Empty;
        PromotionalApr = string.Empty;
        PromotionalEnds = null;
        Kind = DebtKind.CreditCard;
        _originalFingerprint = DebtFingerprint();
        ErrorMessage = null;
        IsEditorOpen = true;
        OnPropertyChanged(nameof(EditorTitle));
    }

    [RelayCommand]
    private void BeginEdit(DebtListItem? item)
    {
        if (item is null || !_session.IsOpen || !TryLeaveEditor())
        {
            return;
        }

        var debt = _session.Document.Debts.FirstOrDefault(entry => entry.Id == item.Id);
        if (debt is null)
        {
            return;
        }

        _editingId = debt.Id;
        Name = debt.Name;
        Kind = debt.Kind;
        Balance = AmountParsing.Format(debt.Balance);
        Apr = AmountParsing.Format(debt.AnnualPercentageRate);
        Minimum = AmountParsing.Format(debt.MinimumPayment);
        PromotionalApr = debt.PromotionalRate is { } promo ? AmountParsing.Format(promo) : string.Empty;
        PromotionalEnds = debt.PromotionalRateEnds?.ToDateTime(TimeOnly.MinValue);
        _originalFingerprint = DebtFingerprint();
        ErrorMessage = null;
        IsEditorOpen = true;
        OnPropertyChanged(nameof(EditorTitle));
    }

    [RelayCommand]
    private async Task AddDebtAsync(CancellationToken cancellationToken)
    {
        if (!AmountParsing.TryParseMoney(Balance, out var amount) || amount.IsNegative
            || !AmountParsing.TryParseDecimal(Apr, out var rate) || rate < 0
            || !AmountParsing.TryParseMoney(Minimum, out var payment) || payment.IsNegative
            || string.IsNullOrWhiteSpace(Name))
        {
            ErrorMessage = "Enter a name, balance, APR and minimum payment.";
            return;
        }

        decimal? promo = null;
        if (!string.IsNullOrWhiteSpace(PromotionalApr))
        {
            if (!AmountParsing.TryParseDecimal(PromotionalApr, out var parsed) || parsed < 0)
            {
                ErrorMessage = "Enter a valid promotional APR, or leave it blank.";
                return;
            }

            promo = parsed;
        }

        if (promo is not null && PromotionalEnds is null)
        {
            ErrorMessage = "A promotional rate needs an expiry date.";
            return;
        }

        var document = _session.Document;
        var debts = document.Debts.ToList();
        var account = new DebtAccount
        {
            Id = _editingId ?? Guid.NewGuid(),
            Name = Name.Trim(),
            Kind = Kind,
            Balance = amount,
            AnnualPercentageRate = rate,
            MinimumPayment = payment,
            PromotionalRate = promo,
            PromotionalRateEnds = PromotionalEnds is { } ends ? DateOnly.FromDateTime(ends) : null
        };
        var index = debts.FindIndex(entry => entry.Id == account.Id);
        if (index >= 0)
        {
            debts[index] = account;
        }
        else
        {
            debts.Add(account);
        }

        if (!await CommitAsync(document with { Debts = debts }, cancellationToken))
        {
            return;
        }

        Name = string.Empty;
        Balance = string.Empty;
        Apr = string.Empty;
        Minimum = string.Empty;
        PromotionalApr = string.Empty;
        PromotionalEnds = null;
        StatusMessage = "Debt saved.";
        DismissEditor();
        RefreshComparison();
    }

    [RelayCommand]
    private async Task RemoveDebtAsync(DebtListItem? item, CancellationToken cancellationToken)
    {
        if (item is null)
        {
            return;
        }

        if (!_dialog.Confirm("Remove debt", $"Remove the debt named {item.Name}?"))
        {
            return;
        }

        var remaining = _session.Document.Debts.Where(debt => debt.Id != item.Id).ToList();
        if (await CommitAsync(_session.Document with { Debts = remaining }, cancellationToken))
        {
            StatusMessage = "Debt removed.";
            RefreshComparison();
        }
    }

    [RelayCommand]
    private void Compare()
    {
        RefreshComparison();
    }

    private void RefreshComparison()
    {
        if (!_session.IsOpen || _session.Document.Debts.Count == 0)
        {
            ComparisonText = string.Empty;
            return;
        }

        AmountParsing.TryParseMoney(ExtraBudget, out var extra);
        var minimums = Money.Sum(_session.Document.Debts.Select(debt => debt.MinimumPayment));
        var budget = (minimums + extra).Round();
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        var comparison = PayoffPlanner.Compare(_session.Document.Debts, budget, today);
        ComparisonText =
            $"Monthly budget {budget.ToDisplayString()}. Snowball: {comparison.Snowball.MonthsToClear} months, {comparison.Snowball.TotalInterest.ToDisplayString()} interest. " +
            $"Avalanche: {comparison.Avalanche.MonthsToClear} months, {comparison.Avalanche.TotalInterest.ToDisplayString()} interest. " +
            $"Avalanche saves {comparison.InterestSavedByAvalanche.ToDisplayString()} and {comparison.MonthsSavedByAvalanche} months. {comparison.Recommendation}";
    }

    private async Task<bool> CommitAsync(SecureBudgetManager.Core.Storage.BudgetDocument document, CancellationToken cancellationToken)
    {
        var result = await EditorSaveCoordinator.TryCommitAsync(
            _session,
            document,
            cancellationToken,
            "Debt saved");
        ErrorMessage = result.IsSuccess ? null : result.Message;
        return result.IsSuccess;
    }

    private void OnSessionChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        if (!_session.IsOpen)
        {
            Debts = [];
            Name = string.Empty;
            Balance = string.Empty;
            Apr = string.Empty;
            Minimum = string.Empty;
            ComparisonText = string.Empty;
            StatusMessage = null;
            ErrorMessage = null;
            OnPropertyChanged(nameof(Debts));
            return;
        }

        Debts = _session.Document.Debts.Select(debt => new DebtListItem(
            debt.Id,
            debt.Name,
            debt.Balance.ToDisplayString(),
            $"{debt.AnnualPercentageRate:0.##}%",
            debt.MinimumPayment.ToDisplayString())).ToList();
        OnPropertyChanged(nameof(Debts));
        RefreshComparison();
    }
}
