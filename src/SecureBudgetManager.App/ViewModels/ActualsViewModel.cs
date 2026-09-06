using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureBudgetManager.App.Services;
using SecureBudgetManager.Core.Budgeting;
using SecureBudgetManager.Core.Expenses;
using SecureBudgetManager.Core.Household;
using SecureBudgetManager.Core.Income;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.App.ViewModels;

public sealed record TransactionListItem(
    Guid Id,
    string Date,
    string Description,
    string Amount,
    string Kind,
    string Category);

public sealed record ComparisonRow(string Name, string Expected, string Actual, string Difference, string Note);

public sealed record PayslipListItem(Guid Id, string Date, string Source, string Gross, string Net, string Mileage);

public sealed partial class ActualsViewModel : PageViewModel
{
    private readonly IBudgetSession _session;
    private readonly IUserDialog _dialog;
    private readonly TimeProvider _clock;

    public ActualsViewModel(IBudgetSession session, IUserDialog dialog, TimeProvider clock)
        : base(
            "Actuals",
            "Reconciliation",
            "Record what actually arrived and what actually left. Mileage reimbursements stay separate from earnings.")
    {
        _session = session;
        _dialog = dialog;
        _clock = clock;
        _session.Changed += OnSessionChanged;
        CategoryOptions = ExpenseCategory.BuiltIn
            .Select(category => new ChoiceOption<string>(category.Name, category.Name))
            .ToList();
        Refresh();
    }

    public IReadOnlyList<ChoiceOption<string>> CategoryOptions { get; }

    public IReadOnlyList<ChoiceOption<Guid?>> ExpenseOptions { get; private set; } = [];

    public IReadOnlyList<ChoiceOption<Guid>> IncomeOptions { get; private set; } = [];

    public IReadOnlyList<TransactionListItem> Transactions { get; private set; } = [];

    public IReadOnlyList<ComparisonRow> Comparisons { get; private set; } = [];

    public IReadOnlyList<PayslipListItem> Payslips { get; private set; } = [];

    [ObservableProperty] private string description = string.Empty;
    [ObservableProperty] private string amount = string.Empty;
    [ObservableProperty] private DateTime? date = DateTime.Today;
    [ObservableProperty] private string categoryName = ExpenseCategory.Other.Name;
    [ObservableProperty] private Guid? linkedExpenseId;
    [ObservableProperty] private bool isRefund;
    [ObservableProperty] private bool isSplitLine;
    [ObservableProperty] private string notes = string.Empty;
    [ObservableProperty] private Guid selectedIncomeId;
    [ObservableProperty] private DateTime? payslipDate = DateTime.Today;
    [ObservableProperty] private string payslipGross = string.Empty;
    [ObservableProperty] private string payslipFederal = "0";
    [ObservableProperty] private string payslipState = "0";
    [ObservableProperty] private string payslipSocialSecurity = "0";
    [ObservableProperty] private string payslipMedicare = "0";
    [ObservableProperty] private string payslipPreTax = "0";
    [ObservableProperty] private string payslipPostTax = "0";
    [ObservableProperty] private string payslipReimbursement = "0";
    [ObservableProperty] private string payslipHours = "0";
    [ObservableProperty] private string comparisonNote = string.Empty;
    [ObservableProperty] private string carryForwardText = string.Empty;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private string? statusMessage;

    [RelayCommand]
    private async Task AddTransactionAsync(CancellationToken cancellationToken)
    {
        if (!AmountParsing.TryParseMoney(Amount, out var money) || money.IsNegative
            || string.IsNullOrWhiteSpace(Description) || Date is null)
        {
            ErrorMessage = "Enter a description, date and amount.";
            return;
        }

        var category = ExpenseCategory.BuiltIn.FirstOrDefault(item => item.Name == CategoryName)
                       ?? ExpenseCategory.Custom(CategoryName);
        var document = _session.Document;
        var transactions = document.Transactions.ToList();
        transactions.Add(new ExpenseTransaction
        {
            Id = Guid.NewGuid(),
            ExpenseItemId = LinkedExpenseId,
            Date = DateOnly.FromDateTime(Date.Value),
            Description = Description.Trim(),
            Amount = money,
            Category = category,
            IsRefund = IsRefund,
            SplitParentId = IsSplitLine ? Guid.NewGuid() : null,
            Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim()
        });

        if (!await CommitAsync(document with { Transactions = transactions }, cancellationToken))
        {
            return;
        }

        Description = string.Empty;
        Amount = string.Empty;
        Notes = string.Empty;
        IsRefund = false;
        IsSplitLine = false;
        StatusMessage = "Transaction saved.";
    }

    [RelayCommand]
    private async Task RemoveTransactionAsync(TransactionListItem? item, CancellationToken cancellationToken)
    {
        if (item is null)
        {
            return;
        }

        if (!_dialog.Confirm("Remove transaction", "Remove this recorded transaction?"))
        {
            return;
        }

        var remaining = _session.Document.Transactions.Where(entry => entry.Id != item.Id).ToList();
        if (await CommitAsync(_session.Document with { Transactions = remaining }, cancellationToken))
        {
            StatusMessage = "Transaction removed.";
        }
    }

    [RelayCommand]
    private async Task ConfirmTransactionAsync(TransactionListItem? item, CancellationToken cancellationToken)
    {
        if (item is null)
        {
            return;
        }

        var transactions = _session.Document.Transactions.ToList();
        var index = transactions.FindIndex(entry => entry.Id == item.Id);
        if (index < 0)
        {
            return;
        }

        transactions[index] = transactions[index] with { IsConfirmed = true };
        if (await CommitAsync(_session.Document with { Transactions = transactions }, cancellationToken))
        {
            StatusMessage = "Marked as reconciled.";
        }
    }

    [RelayCommand]
    private async Task AddPayslipAsync(CancellationToken cancellationToken)
    {
        if (SelectedIncomeId == Guid.Empty || PayslipDate is null
            || !AmountParsing.TryParseMoney(PayslipGross, out var gross)
            || !AmountParsing.TryParseMoney(PayslipFederal, out var federal)
            || !AmountParsing.TryParseMoney(PayslipState, out var state)
            || !AmountParsing.TryParseMoney(PayslipSocialSecurity, out var social)
            || !AmountParsing.TryParseMoney(PayslipMedicare, out var medicare)
            || !AmountParsing.TryParseMoney(PayslipPreTax, out var preTax)
            || !AmountParsing.TryParseMoney(PayslipPostTax, out var postTax)
            || !AmountParsing.TryParseMoney(PayslipReimbursement, out var reimbursement)
            || !AmountParsing.TryParseDecimal(PayslipHours, out var hours)
            || gross.IsNegative || hours < 0m)
        {
            ErrorMessage = "Enter a valid payslip against an income source.";
            return;
        }

        var source = _session.Document.IncomeSources.FirstOrDefault(item => item.Id == SelectedIncomeId);
        if (source is null)
        {
            ErrorMessage = "Choose the income source this payslip belongs to.";
            return;
        }

        var payslip = new Payslip
        {
            Id = Guid.NewGuid(),
            IncomeSourceId = SelectedIncomeId,
            PayDate = DateOnly.FromDateTime(PayslipDate.Value),
            GrossPay = gross,
            FederalWithholding = federal,
            StateWithholding = state,
            SocialSecurity = social,
            Medicare = medicare,
            PreTaxDeductions = preTax,
            PostTaxDeductions = postTax,
            NonTaxableReimbursements = reimbursement,
            HoursWorked = hours
        };

        var document = _session.Document;
        var slips = document.Payslips.ToList();
        slips.Add(payslip);
        if (!await CommitAsync(document with { Payslips = slips }, cancellationToken))
        {
            return;
        }

        var takeHome = TakeHomeCalculator.From(document, IncomeEstimate.Normal);
        takeHome.NetPayPerPeriod.TryGetValue(SelectedIncomeId, out var expected);
        var comparison = IncomeReconciler.Compare(expected, payslip);
        ComparisonNote = comparison.Explanation;
        StatusMessage = "Payslip saved. Mileage and other reimbursements stay outside taxable earnings.";
    }

    [RelayCommand]
    private async Task CarryBalanceForwardAsync(CancellationToken cancellationToken)
    {
        if (!AmountParsing.TryParseMoney(CarryForwardText, out var balance))
        {
            ErrorMessage = "Enter the closing balance to carry into the next period.";
            return;
        }

        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        var document = _session.Document;
        var accounts = document.Accounts.ToList();
        var primary = accounts.FirstOrDefault(account => account.IsPrimary) ?? accounts.FirstOrDefault();
        if (primary is null)
        {
            accounts.Add(new BankAccount
            {
                Id = Guid.NewGuid(),
                Name = "Primary checking",
                CurrentBalance = balance,
                IsPrimary = true,
                UpdatedOn = today
            });
        }
        else
        {
            var index = accounts.FindIndex(account => account.Id == primary.Id);
            accounts[index] = primary with { CurrentBalance = balance, UpdatedOn = today };
        }

        if (await CommitAsync(document with { Accounts = accounts }, cancellationToken))
        {
            StatusMessage = "Closing balance carried into the starting balance for the next period.";
        }
    }

    private async Task<bool> CommitAsync(SecureBudgetManager.Core.Storage.BudgetDocument document, CancellationToken cancellationToken)
    {
        if (!_session.TryReplace(document, out var error))
        {
            ErrorMessage = error;
            return false;
        }

        if (!await _session.SaveAsync(cancellationToken))
        {
            ErrorMessage = _session.LastError;
            return false;
        }

        ErrorMessage = null;
        return true;
    }

    private void OnSessionChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        if (!_session.IsOpen)
        {
            Transactions = [];
            Comparisons = [];
            Payslips = [];
            ExpenseOptions = [];
            IncomeOptions = [];
            Description = string.Empty;
            Amount = string.Empty;
            ComparisonNote = string.Empty;
            CarryForwardText = string.Empty;
            StatusMessage = null;
            ErrorMessage = null;
            OnPropertyChanged(nameof(Transactions));
            OnPropertyChanged(nameof(Comparisons));
            OnPropertyChanged(nameof(Payslips));
            OnPropertyChanged(nameof(ExpenseOptions));
            OnPropertyChanged(nameof(IncomeOptions));
            return;
        }

        var document = _session.Document;
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        var from = new DateOnly(today.Year, today.Month, 1);
        var to = from.AddMonths(1).AddDays(-1);

        ExpenseOptions =
        [
            new ChoiceOption<Guid?>(null, "Unlinked"),
            .. document.Expenses.Select(item => new ChoiceOption<Guid?>(item.Id, item.Name))
        ];
        IncomeOptions = document.IncomeSources
            .Select(source => new ChoiceOption<Guid>(source.Id, source.Name))
            .ToList();
        if (IncomeOptions.All(option => option.Value != SelectedIncomeId))
        {
            SelectedIncomeId = IncomeOptions.FirstOrDefault()?.Value ?? Guid.Empty;
        }

        Transactions = document.Transactions
            .OrderByDescending(item => item.Date)
            .Select(item => new TransactionListItem(
                item.Id,
                item.Date.ToString("yyyy-MM-dd"),
                item.Description,
                item.SignedAmount.ToDisplayString(),
                item.IsRefund ? "Refund / reimbursement" : item.SplitParentId is null ? "Spend" : "Split line",
                item.Category.Name))
            .ToList();

        Comparisons = document.Expenses.Select(item =>
        {
            var row = ExpenseReconciler.Compare(item, document.Transactions, from, to);
            return new ComparisonRow(
                item.Name,
                row.Expected.ToDisplayString(),
                row.Actual.ToDisplayString(),
                row.Difference.ToDisplayString(),
                row.Explanation);
        }).ToList();

        var takeHome = TakeHomeCalculator.From(document, IncomeEstimate.Normal);
        Payslips = document.Payslips
            .OrderByDescending(item => item.PayDate)
            .Select(item =>
            {
                var source = document.IncomeSources.FirstOrDefault(entry => entry.Id == item.IncomeSourceId);
                var mileage = source is MileageReimbursement
                    ? item.NetPay.ToDisplayString()
                    : item.NonTaxableReimbursements.ToDisplayString();
                return new PayslipListItem(
                    item.Id,
                    item.PayDate.ToString("yyyy-MM-dd"),
                    source?.Name ?? "Unknown",
                    item.GrossPay.ToDisplayString(),
                    item.NetPay.ToDisplayString(),
                    mileage);
            }).ToList();

        CarryForwardText = AmountParsing.Format(document.PrimaryAccount?.CurrentBalance ?? Money.Zero);
        if (document.Payslips.Count > 0 && takeHome.HasPayrollDetails)
        {
            var latest = document.Payslips.MaxBy(item => item.PayDate)!;
            takeHome.NetPayPerPeriod.TryGetValue(latest.IncomeSourceId, out var expected);
            ComparisonNote = IncomeReconciler.Compare(expected, latest).Explanation;
        }

        OnPropertyChanged(nameof(Transactions));
        OnPropertyChanged(nameof(Comparisons));
        OnPropertyChanged(nameof(Payslips));
        OnPropertyChanged(nameof(ExpenseOptions));
        OnPropertyChanged(nameof(IncomeOptions));
    }
}
