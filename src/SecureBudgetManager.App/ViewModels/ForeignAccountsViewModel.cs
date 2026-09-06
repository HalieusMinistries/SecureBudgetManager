using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureBudgetManager.App.Services;
using SecureBudgetManager.Core.International;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Tax;

namespace SecureBudgetManager.App.ViewModels;

public sealed record ForeignAccountRow(
    Guid Id,
    string Institution,
    string Country,
    string Nickname,
    string Currency,
    string HighBalance,
    string Review);

public sealed record ReminderRow(string Form, string Message, string Source);

/// <summary>Foreign-account records and possible information-return reminders. No banking credentials.</summary>
public sealed partial class ForeignAccountsViewModel : PageViewModel
{
    private readonly IBudgetSession _session;

    public ForeignAccountsViewModel(IBudgetSession session)
        : base(
            "Foreign accounts",
            "Reporting review",
            "Record foreign financial accounts without usernames, passwords, PINs or full card " +
            "numbers. Reminders say “Possible reporting requirement” unless every legally necessary " +
            "fact is known. This is not a substitute for a qualified tax professional.")
    {
        _session = session;
        _session.Changed += OnSessionChanged;
        Refresh();
    }

    public IReadOnlyList<ChoiceOption<FilingStatus>> FilingOptions { get; } =
    [
        new(FilingStatus.Single, "Single"),
        new(FilingStatus.MarriedFilingJointly, "Married filing jointly"),
        new(FilingStatus.MarriedFilingSeparately, "Married filing separately"),
        new(FilingStatus.HeadOfHousehold, "Head of household")
    ];

    [ObservableProperty] private string institution = string.Empty;
    [ObservableProperty] private string country = "South Africa";
    [ObservableProperty] private string nickname = string.Empty;
    [ObservableProperty] private string currency = "ZAR";
    [ObservableProperty] private string maximumBalance = string.Empty;
    [ObservableProperty] private string yearEndBalance = string.Empty;
    [ObservableProperty] private string reportingRate = string.Empty;
    [ObservableProperty] private bool hasFinancialInterest = true;
    [ObservableProperty] private bool hasSignatureAuthority;
    [ObservableProperty] private int reminderYear = 2026;
    [ObservableProperty] private FilingStatus filingStatus = FilingStatus.MarriedFilingJointly;
    [ObservableProperty] private bool livesInUnitedStates = true;
    [ObservableProperty] private string documentTitle = string.Empty;
    [ObservableProperty] private string documentKind = "Transfer receipt";
    [ObservableProperty] private string? statusMessage;
    [ObservableProperty] private string? errorMessage;

    public IReadOnlyList<ForeignAccountRow> Accounts { get; private set; } = [];
    public IReadOnlyList<ReminderRow> Reminders { get; private set; } = [];
    public IReadOnlyList<string> Documents { get; private set; } = [];

    [RelayCommand]
    private async Task SaveAccountAsync(CancellationToken cancellationToken)
    {
        if (!_session.IsOpen)
        {
            ErrorMessage = "Open the household database first.";
            return;
        }

        if (string.IsNullOrWhiteSpace(Institution) || string.IsNullOrWhiteSpace(Nickname))
        {
            ErrorMessage = "An account needs an institution and a nickname, not a password or PIN.";
            return;
        }

        if (Nickname.Contains("password", StringComparison.OrdinalIgnoreCase)
            || Nickname.Contains("pin", StringComparison.OrdinalIgnoreCase))
        {
            ErrorMessage = "Do not store banking credentials or PINs.";
            return;
        }

        Money? high = null;
        Money? yearEnd = null;
        decimal? rate = null;
        if (!string.IsNullOrWhiteSpace(MaximumBalance))
        {
            if (!AmountParsing.TryParseMoney(MaximumBalance, out var parsed))
            {
                ErrorMessage = "Enter a valid high balance or leave it blank.";
                return;
            }

            high = parsed;
        }

        if (!string.IsNullOrWhiteSpace(YearEndBalance))
        {
            if (!AmountParsing.TryParseMoney(YearEndBalance, out var parsed))
            {
                ErrorMessage = "Enter a valid year-end balance or leave it blank.";
                return;
            }

            yearEnd = parsed;
        }

        if (!string.IsNullOrWhiteSpace(ReportingRate))
        {
            if (!AmountParsing.TryParseDecimal(ReportingRate, out var parsed) || parsed <= 0m)
            {
                ErrorMessage = "Enter a valid reporting exchange rate or leave it blank.";
                return;
            }

            rate = parsed;
        }

        var account = new ForeignAccount
        {
            Id = Guid.NewGuid(),
            Institution = Institution.Trim(),
            Country = Country.Trim(),
            Nickname = Nickname.Trim(),
            Currency = Currency.Trim().ToUpperInvariant(),
            MaximumCalendarYearBalance = high,
            YearEndBalance = yearEnd,
            ReportingExchangeRate = rate,
            UsdEquivalent = high is { } amount && rate is { } fx
                ? new Money(amount.Amount * fx)
                : high,
            HasFinancialInterest = HasFinancialInterest,
            HasSignatureAuthority = HasSignatureAuthority
        };

        if (!_session.TryReplace(
                _session.Document with { ForeignAccounts = _session.Document.ForeignAccounts.Append(account).ToList() },
                out var error))
        {
            ErrorMessage = error;
            return;
        }

        ErrorMessage = null;
        StatusMessage = await _session.SaveAsync(cancellationToken)
            ? "Foreign account saved. No banking credential was stored."
            : _session.LastError ?? "The account could not be saved.";
    }

    [RelayCommand]
    private async Task SaveDocumentAsync(CancellationToken cancellationToken)
    {
        if (!_session.IsOpen)
        {
            ErrorMessage = "Open the household database first.";
            return;
        }

        if (string.IsNullOrWhiteSpace(DocumentTitle))
        {
            ErrorMessage = "A supporting document needs a title.";
            return;
        }

        var documentRef = new SupportingDocumentRef
        {
            Id = Guid.NewGuid(),
            Title = DocumentTitle.Trim(),
            Kind = string.IsNullOrWhiteSpace(DocumentKind) ? "Other" : DocumentKind.Trim()
        };

        if (!_session.TryReplace(
                _session.Document with
                {
                    SupportingDocuments = _session.Document.SupportingDocuments.Append(documentRef).ToList()
                },
                out var error))
        {
            ErrorMessage = error;
            return;
        }

        ErrorMessage = null;
        StatusMessage = await _session.SaveAsync(cancellationToken)
            ? "Encrypted document reference saved."
            : _session.LastError ?? "The document reference could not be saved.";
    }

    private void OnSessionChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        if (!_session.IsOpen)
        {
            Accounts = [];
            Reminders = [];
            Documents = [];
            Institution = string.Empty;
            Nickname = string.Empty;
            MaximumBalance = string.Empty;
            YearEndBalance = string.Empty;
            ReportingRate = string.Empty;
            DocumentTitle = string.Empty;
            StatusMessage = null;
            ErrorMessage = null;
            Notify();
            return;
        }

        var document = _session.Document;
        Accounts = document.ForeignAccounts
            .Select(account => new ForeignAccountRow(
                account.Id,
                account.Institution,
                account.Country,
                account.Nickname,
                account.Currency,
                (account.MaximumCalendarYearBalance ?? account.UsdEquivalent)?.ToDisplayString() ?? "Not recorded",
                account.ReviewStatus.ToString()))
            .ToList();

        Reminders = ForeignReportingLibrary
            .Evaluate(ReminderYear, FilingStatus, LivesInUnitedStates, document.ForeignAccounts, document.InternationalTransfers)
            .Select(item => new ReminderRow(item.Form, item.Message, item.Source))
            .ToList();

        Documents = document.SupportingDocuments
            .Select(item => $"{item.Kind}: {item.Title}")
            .ToList();
        Notify();
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(Accounts));
        OnPropertyChanged(nameof(Reminders));
        OnPropertyChanged(nameof(Documents));
    }
}
