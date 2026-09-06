using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureBudgetManager.App.Services;
using SecureBudgetManager.Core.Budgeting;
using SecureBudgetManager.Core.CashFlow;
using SecureBudgetManager.Core.Household;
using SecureBudgetManager.Core.Income;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Storage;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.App.ViewModels;

public sealed record CashFlowDayRow(
    string Date,
    string Opening,
    string Closing,
    string Events,
    bool IsPayday,
    bool IsBillDay,
    bool IsOverdraft);

public sealed partial class CashFlowViewModel : PageViewModel
{
    private readonly IBudgetSession _session;
    private readonly TimeProvider _clock;

    public CashFlowViewModel(IBudgetSession session, TimeProvider clock)
        : base(
            "Cash Flow",
            "Calendar",
            "Daily balances are projected on this device from local income, bills and the starting balance you enter. Conservative uses 35-hour weeks, normal 37.5, optimistic 40.")
    {
        _session = session;
        _clock = clock;
        _session.Changed += OnSessionChanged;
        Refresh();
    }

    public IReadOnlyList<ChoiceOption<IncomeEstimate>> AssumptionOptions { get; } =
    [
        new(IncomeEstimate.Conservative, "Conservative (35-hour weeks)"),
        new(IncomeEstimate.Normal, "Normal (37.5-hour weeks)"),
        new(IncomeEstimate.Optimistic, "Optimistic (40-hour weeks)")
    ];

    [ObservableProperty]
    private IncomeEstimate assumption = IncomeEstimate.Conservative;

    [ObservableProperty]
    private string startingBalanceText = "0.00";

    [ObservableProperty]
    private string accountName = "Primary checking";

    [ObservableProperty]
    private string lowestBalanceText = "—";

    [ObservableProperty]
    private string nextPaydayText = "—";

    [ObservableProperty]
    private string billsBeforePaydayText = "—";

    [ObservableProperty]
    private string overdraftDatesText = "No projected overdraft in this window.";

    [ObservableProperty]
    private string fivePaychequeText = "—";

    [ObservableProperty]
    private string mixHint = string.Empty;

    [ObservableProperty]
    private string? statusMessage;

    [ObservableProperty]
    private string? errorMessage;

    public IReadOnlyList<CashFlowDayRow> Days { get; private set; } = [];

    public bool HasDays => Days.Count > 0;

    public bool ShowEmptyCalendar => Days.Count == 0;

    partial void OnAssumptionChanged(IncomeEstimate value) => Refresh();

    [RelayCommand]
    private async Task SaveStartingBalanceAsync(CancellationToken cancellationToken)
    {
        if (!_session.IsOpen)
        {
            ErrorMessage = "Open the household database first.";
            return;
        }

        if (!AmountParsing.TryParseMoney(StartingBalanceText, out var balance))
        {
            ErrorMessage = "Enter a valid starting balance.";
            return;
        }

        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        var document = _session.Document;
        var accounts = document.Accounts.ToList();
        var existing = accounts.FirstOrDefault(account => account.IsPrimary) ?? accounts.FirstOrDefault();
        if (existing is null)
        {
            accounts.Add(new BankAccount
            {
                Id = Guid.NewGuid(),
                Name = string.IsNullOrWhiteSpace(AccountName) ? "Primary checking" : AccountName.Trim(),
                CurrentBalance = balance,
                IsPrimary = true,
                UpdatedOn = today
            });
        }
        else
        {
            var index = accounts.FindIndex(account => account.Id == existing.Id);
            accounts[index] = existing with
            {
                Name = string.IsNullOrWhiteSpace(AccountName) ? existing.Name : AccountName.Trim(),
                CurrentBalance = balance,
                IsPrimary = true,
                UpdatedOn = today
            };
        }

        if (!_session.TryReplace(document with { Accounts = accounts }, out var error))
        {
            ErrorMessage = error;
            return;
        }

        ErrorMessage = null;
        StatusMessage = await _session.SaveAsync(cancellationToken)
            ? "Starting balance saved to the local database."
            : _session.LastError ?? "The balance could not be saved.";
    }

    private void OnSessionChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        if (!_session.IsOpen)
        {
            Days = [];
            StartingBalanceText = string.Empty;
            AccountName = string.Empty;
            LowestBalanceText = string.Empty;
            NextPaydayText = string.Empty;
            BillsBeforePaydayText = string.Empty;
            OverdraftDatesText = string.Empty;
            FivePaychequeText = string.Empty;
            MixHint = string.Empty;
            StatusMessage = null;
            ErrorMessage = null;
            OnPropertyChanged(nameof(Days));
            OnPropertyChanged(nameof(HasDays));
            OnPropertyChanged(nameof(ShowEmptyCalendar));
            return;
        }

        var document = _session.Document;
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        var primary = document.PrimaryAccount;
        var starting = primary?.CurrentBalance ?? Money.Zero;
        StartingBalanceText = AmountParsing.Format(starting);
        AccountName = primary?.Name ?? "Primary checking";

        var takeHome = TakeHomeCalculator.From(document, Assumption);
        var projection = CashFlowProjector.Project(new CashFlowInputs
        {
            From = today,
            To = today.AddDays(90),
            StartingBalance = starting,
            IncomeSources = document.IncomeSources,
            NetPayPerPeriod = takeHome.NetPayPerPeriod,
            Expenses = document.Expenses,
            Assumption = Assumption
        });

        Days = projection.Days.Select(day => new CashFlowDayRow(
            day.Date.ToString("yyyy-MM-dd ddd"),
            day.OpeningBalance.ToDisplayString(),
            day.ClosingBalance.ToDisplayString(),
            string.Join("; ", day.Events.Select(Describe)),
            day.Events.Any(item => item.Direction == CashFlowDirection.Deposit),
            day.Events.Any(item => item.Direction == CashFlowDirection.Withdrawal),
            day.IsBelowZero)).ToList();

        LowestBalanceText = projection.LowestDay is { } lowest
            ? $"{lowest.ClosingBalance.ToDisplayString()} on {lowest.Date:yyyy-MM-dd}"
            : starting.ToDisplayString();

        var nextPayday = projection.Days
            .SelectMany(day => day.Events.Select(item => (day.Date, item)))
            .Where(pair => pair.item.Direction == CashFlowDirection.Deposit)
            .Select(pair => pair.Date)
            .Cast<DateOnly?>()
            .FirstOrDefault();

        NextPaydayText = nextPayday is { } payday
            ? payday.ToString("yyyy-MM-dd")
            : "No payday in the next 90 days.";

        if (nextPayday is { } next)
        {
            var bills = projection.Days
                .Where(day => day.Date < next)
                .SelectMany(day => day.Events)
                .Where(item => item.Direction == CashFlowDirection.Withdrawal)
                .ToList();
            BillsBeforePaydayText = bills.Count == 0
                ? "No bills before the next payday."
                : string.Join(", ", bills.Select(item => $"{item.Date:MMM d} {item.Description} {item.Amount.ToDisplayString()}"));
        }
        else
        {
            BillsBeforePaydayText = "Add income with a pay date to see bills before payday.";
        }

        OverdraftDatesText = projection.GoesNegative
            ? "Potential overdraft: " + string.Join(", ", projection.DaysBelowZero.Take(8).Select(day => day.Date.ToString("yyyy-MM-dd")))
            : "No projected overdraft in this 90-day window.";

        var fivePay = projection.Days
            .SelectMany(day => day.Events.Where(item => item.Direction == CashFlowDirection.Deposit).Select(_ => day.Date))
            .GroupBy(date => (date.Year, date.Month))
            .Where(group => group.Count() >= 5)
            .Select(group => $"{group.Key.Year}-{group.Key.Month:00} ({group.Count()} paydays)")
            .ToList();
        FivePaychequeText = fivePay.Count == 0
            ? "No five-paycheque month in this window."
            : "Five-paycheque months: " + string.Join(", ", fivePay);

        var weeklyIncome = document.IncomeSources.Any(source => source.IsActive && source.PayFrequency == Frequency.Weekly);
        var fortnightlyBills = document.Expenses.Any(expense => !expense.IsPaused && expense.Frequency == Frequency.Fortnightly);
        MixHint = weeklyIncome && fortnightlyBills
            ? "Fortnightly bills are being projected against weekly income. Watch the week that has a bill but no second payday."
            : TaxYearLibraryNote(document);

        OnPropertyChanged(nameof(Days));
        OnPropertyChanged(nameof(HasDays));
        OnPropertyChanged(nameof(ShowEmptyCalendar));
    }

    private static string TaxYearLibraryNote(BudgetDocument document) =>
        document.IncomeSources.Any(source => source.IsActive)
            ? "Figures use estimated net pay when payroll details exist; otherwise they fall back to gross."
            : "Add income and bills to populate the calendar.";

    private static string Describe(CashFlowEvent item) =>
        item.Direction == CashFlowDirection.Deposit
            ? $"+ {item.Description} {item.Amount.ToDisplayString()}"
            : $"- {item.Description} {item.Amount.ToDisplayString()}";
}
