using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureBudgetManager.App.Interaction;
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
    string Review,
    bool IsSelected = false);

public sealed record ReminderRow(string Form, string Message, string Source);

/// <summary>Foreign-account records and possible information-return reminders. No banking credentials.</summary>
public sealed partial class ForeignAccountsViewModel : PageViewModel, IEditablePage
{
    private readonly IBudgetSession _session;
    private readonly IUserDialog _dialog;
    private readonly TimeProvider _clock;
    private Guid? _editingId;
    private string _originalFingerprint = string.Empty;

    public ForeignAccountsViewModel(IBudgetSession session, IUserDialog dialog, TimeProvider clock)
        : base(
            "Foreign accounts",
            "Reporting review",
            "Record foreign financial accounts without usernames, passwords, PINs or full card " +
            "numbers. Reminders say “Possible reporting requirement” unless every legally necessary " +
            "fact is known. This is not a substitute for a qualified tax professional.")
    {
        _session = session;
        _dialog = dialog;
        _clock = clock;
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

    public IReadOnlyList<ChoiceOption<Guid>> OwnerOptions { get; private set; } =
        [new(Guid.Empty, "Not assigned")];

    [ObservableProperty] private Guid ownerId = Guid.Empty;
    [ObservableProperty] private string institution = string.Empty;
    [ObservableProperty] private string country = "South Africa";
    [ObservableProperty] private string nickname = string.Empty;
    [ObservableProperty] private string currency = "ZAR";
    [ObservableProperty] private string maximumBalance = string.Empty;
    [ObservableProperty] private string yearEndBalance = string.Empty;
    [ObservableProperty] private string reportingRate = string.Empty;
    [ObservableProperty] private string usdEquivalent = string.Empty;
    [ObservableProperty] private bool hasFinancialInterest = true;
    [ObservableProperty] private bool hasSignatureAuthority;
    [ObservableProperty] private bool isArchived;
    [ObservableProperty] private int reminderYear = 2026;
    [ObservableProperty] private FilingStatus filingStatus = FilingStatus.MarriedFilingJointly;
    [ObservableProperty] private bool livesInUnitedStates = true;
    [ObservableProperty] private string documentTitle = string.Empty;
    [ObservableProperty] private string documentKind = "Transfer receipt";
    [ObservableProperty] private string? statusMessage;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditorTitle))]
    private bool isEditorOpen;
    [ObservableProperty] private double listScrollOffset;
    [ObservableProperty] private Guid? selectedRecordId;

    public IReadOnlyList<ForeignAccountRow> Accounts { get; private set; } = [];
    public IReadOnlyList<ReminderRow> Reminders { get; private set; } = [];
    public IReadOnlyList<string> Documents { get; private set; } = [];

    public bool HasAccounts => Accounts.Count > 0;

    public string EditorTitle => _editingId is null ? "Add foreign account" : "Edit foreign account";

    public string EditorSaveLabel => "Save foreign account";

    public string? EditorEffectPreview =>
        "Recorded balances are for reporting review and are not added to United States available money. " +
        "Enter only a rate, balance or USD equivalent you already have. This programme does not invent them.";

    public bool HasEditorChanges => IsEditorOpen && AccountFingerprint() != _originalFingerprint;

    public bool HasEditorError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public string? EditorError => ErrorMessage;

    public ICommand SaveEditorCommand => SaveAccountCommand;

    public ICommand CancelEditorCommand => CancelAccountEditorCommand;

    public bool TryLeaveEditor()
    {
        if (!IsEditorOpen)
        {
            return true;
        }

        if (!HasEditorChanges || _dialog.Confirm("Unsaved changes", "Close without saving this foreign account?"))
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

    private string AccountFingerprint() =>
        $"{OwnerId}|{Institution}|{Country}|{Nickname}|{Currency}|{MaximumBalance}|{YearEndBalance}|{ReportingRate}|{UsdEquivalent}|{HasFinancialInterest}|{HasSignatureAuthority}|{IsArchived}";

    [RelayCommand]
    private void CancelAccountEditor() => DismissEditor();

    [RelayCommand]
    private void SelectAccount(ForeignAccountRow? row)
    {
        if (row is null)
        {
            return;
        }

        SelectedRecordId = row.Id;
        RematchSelection();
    }

    [RelayCommand]
    private void BeginAdd()
    {
        if (!TryLeaveEditor())
        {
            return;
        }

        _editingId = null;
        OwnerId = Guid.Empty;
        Institution = string.Empty;
        Country = "South Africa";
        Nickname = string.Empty;
        Currency = "ZAR";
        MaximumBalance = string.Empty;
        YearEndBalance = string.Empty;
        ReportingRate = string.Empty;
        UsdEquivalent = string.Empty;
        HasFinancialInterest = true;
        HasSignatureAuthority = false;
        IsArchived = false;
        _originalFingerprint = AccountFingerprint();
        ErrorMessage = null;
        IsEditorOpen = true;
        OnPropertyChanged(nameof(EditorTitle));
    }

    [RelayCommand]
    private void BeginEdit(ForeignAccountRow? row)
    {
        if (row is null || !_session.IsOpen || !TryLeaveEditor())
        {
            return;
        }

        var account = _session.Document.ForeignAccounts.FirstOrDefault(item => item.Id == row.Id);
        if (account is null)
        {
            return;
        }

        SelectedRecordId = account.Id;
        _editingId = account.Id;
        OwnerId = account.OwnerMemberId ?? Guid.Empty;
        Institution = account.Institution;
        Country = account.Country;
        Nickname = account.Nickname;
        Currency = account.Currency;
        MaximumBalance = account.MaximumCalendarYearBalance is { } high
            ? AmountParsing.Format(high)
            : string.Empty;
        YearEndBalance = account.YearEndBalance is { } yearEnd
            ? AmountParsing.Format(yearEnd)
            : string.Empty;
        ReportingRate = account.ReportingExchangeRate is { } rate
            ? AmountParsing.Format(rate)
            : string.Empty;
        UsdEquivalent = account.UsdEquivalent is { } usd
            ? AmountParsing.Format(usd)
            : string.Empty;
        HasFinancialInterest = account.HasFinancialInterest;
        HasSignatureAuthority = account.HasSignatureAuthority;
        IsArchived = account.ClosedOn is not null;
        _originalFingerprint = AccountFingerprint();
        ErrorMessage = null;
        IsEditorOpen = true;
        OnPropertyChanged(nameof(EditorTitle));
        RematchSelection();
    }

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

        if (!TryOptionalMoney(MaximumBalance, "Enter a valid high balance or leave it blank.", out var high)
            || !TryOptionalMoney(YearEndBalance, "Enter a valid recorded balance or leave it blank.", out var yearEnd)
            || !TryOptionalMoney(UsdEquivalent, "Enter a valid USD equivalent or leave it blank.", out var usd))
        {
            return;
        }

        decimal? rate = null;
        if (!string.IsNullOrWhiteSpace(ReportingRate))
        {
            if (!AmountParsing.TryParseDecimal(ReportingRate, out var parsed) || parsed <= 0m)
            {
                ErrorMessage = "Enter a valid reporting exchange rate you already have, or leave it blank.";
                return;
            }

            rate = parsed;
        }

        var existing = _editingId is { } editing
            ? _session.Document.ForeignAccounts.FirstOrDefault(item => item.Id == editing)
            : null;
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        var account = new ForeignAccount
        {
            Id = existing?.Id ?? Guid.NewGuid(),
            OwnerMemberId = OwnerId == Guid.Empty ? null : OwnerId,
            Institution = Institution.Trim(),
            Country = string.IsNullOrWhiteSpace(Country) ? "South Africa" : Country.Trim(),
            Nickname = Nickname.Trim(),
            Currency = string.IsNullOrWhiteSpace(Currency) ? "ZAR" : Currency.Trim().ToUpperInvariant(),
            MaximumCalendarYearBalance = high,
            YearEndBalance = yearEnd,
            ReportingExchangeRate = rate,
            UsdEquivalent = usd ?? (high is { } amount && rate is { } fx
                ? new Money(amount.Amount * fx)
                : high),
            HasFinancialInterest = HasFinancialInterest,
            HasSignatureAuthority = HasSignatureAuthority,
            OpenedOn = existing?.OpenedOn,
            ClosedOn = IsArchived
                ? existing?.ClosedOn ?? today
                : null,
            ReviewStatus = existing?.ReviewStatus ?? ClassificationReviewStatus.Unreviewed
        };

        SelectedRecordId = account.Id;
        var accounts = _session.Document.ForeignAccounts
            .Where(item => item.Id != account.Id)
            .Append(account)
            .ToList();

        if (!_session.TryReplace(_session.Document with { ForeignAccounts = accounts }, out var error))
        {
            ErrorMessage = error;
            return;
        }

        var result = await EditorSaveCoordinator.PersistCurrentAsync(
            _session,
            cancellationToken,
            "Foreign account saved",
            document => document.ForeignAccounts.Any(item => item.Id == account.Id));
        ErrorMessage = result.IsSuccess ? null : result.Message;
        StatusMessage = result.IsSuccess ? result.Message : StatusMessage;
        if (result.IsSuccess)
        {
            DismissEditor();
        }
    }

    private bool TryOptionalMoney(string text, string invalidMessage, out Money? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (!AmountParsing.TryParseMoney(text, out var parsed))
        {
            ErrorMessage = invalidMessage;
            return false;
        }

        value = parsed;
        return true;
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
            OwnerOptions = [new(Guid.Empty, "Not assigned")];
            Institution = string.Empty;
            Nickname = string.Empty;
            MaximumBalance = string.Empty;
            YearEndBalance = string.Empty;
            ReportingRate = string.Empty;
            UsdEquivalent = string.Empty;
            DocumentTitle = string.Empty;
            StatusMessage = null;
            ErrorMessage = null;
            SelectedRecordId = null;
            DismissEditor();
            Notify();
            return;
        }

        var document = _session.Document;
        OwnerOptions =
        [
            new(Guid.Empty, "Not assigned"),
            .. document.Members.Select(member => new ChoiceOption<Guid>(member.Id, member.Name))
        ];

        Accounts = document.ForeignAccounts
            .Select(account => new ForeignAccountRow(
                account.Id,
                account.Institution,
                account.Country,
                account.Nickname,
                account.Currency,
                (account.YearEndBalance ?? account.MaximumCalendarYearBalance ?? account.UsdEquivalent)?.ToDisplayString()
                    ?? "Not recorded",
                account.ClosedOn is null ? account.ReviewStatus.ToString() : "Archived",
                account.Id == SelectedRecordId))
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

    private void RematchSelection()
    {
        Accounts = Accounts
            .Select(item => item with { IsSelected = item.Id == SelectedRecordId })
            .ToList();
        OnPropertyChanged(nameof(Accounts));
        OnPropertyChanged(nameof(HasAccounts));
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(Accounts));
        OnPropertyChanged(nameof(Reminders));
        OnPropertyChanged(nameof(Documents));
        OnPropertyChanged(nameof(OwnerOptions));
        OnPropertyChanged(nameof(HasAccounts));
    }
}
