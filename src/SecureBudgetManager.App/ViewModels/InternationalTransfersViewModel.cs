using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureBudgetManager.App.Interaction;
using SecureBudgetManager.App.Services;
using SecureBudgetManager.Core.Guidance;
using SecureBudgetManager.Core.International;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.App.ViewModels;

public sealed record InternationalTransferRow(
    Guid Id,
    string Parties,
    string Amounts,
    string Purpose,
    string Classification,
    string Review,
    bool IsSelected = false);

public sealed record CommitmentRow(
    Guid Id,
    string Name,
    string Reserve,
    string Tier,
    bool IsSelected = false);

/// <summary>United States–South Africa movements. A transfer is not automatically income or a gift.</summary>
public sealed partial class InternationalTransfersViewModel : PageViewModel, IEditablePage
{
    private readonly IBudgetSession _session;
    private readonly IUserDialog _dialog;
    private readonly TimeProvider _clock;
    private Guid? _editingTransferId;
    private Guid? _editingCommitmentId;
    private string _originalFingerprint = string.Empty;

    public InternationalTransfersViewModel(IBudgetSession session, IUserDialog dialog, TimeProvider clock)
        : base(
            "International transfers",
            "Remittances",
            "Money moving between the United States and South Africa. Purpose and ownership " +
            "must be recorded. Same-owner movements are not new income. Completed transfers keep " +
            "their original rate.")
    {
        _session = session;
        _dialog = dialog;
        _clock = clock;
        _session.Changed += OnSessionChanged;
        Refresh();
    }

    public IReadOnlyList<ChoiceOption<TransferPurpose>> PurposeOptions { get; } =
        Enum.GetValues<TransferPurpose>().Select(item => new ChoiceOption<TransferPurpose>(item, item.ToString())).ToList();

    public IReadOnlyList<ChoiceOption<TransferRelationship>> RelationshipOptions { get; } =
        Enum.GetValues<TransferRelationship>().Select(item => new ChoiceOption<TransferRelationship>(item, item.ToString())).ToList();

    public IReadOnlyList<ChoiceOption<TransferTaxClassification>> ClassificationOptions { get; } =
        Enum.GetValues<TransferTaxClassification>().Select(item => new ChoiceOption<TransferTaxClassification>(item, item.ToString())).ToList();

    public IReadOnlyList<ChoiceOption<Frequency>> Frequencies { get; } =
    [
        new(Frequency.Weekly, "Weekly"),
        new(Frequency.Fortnightly, "Fortnightly"),
        new(Frequency.Monthly, "Monthly"),
        new(Frequency.Quarterly, "Quarterly"),
        new(Frequency.Annual, "Annual"),
        new(Frequency.OneOff, "One-time")
    ];

    public IReadOnlyList<ChoiceOption<NeedTier?>> TierOptions { get; } =
        new ChoiceOption<NeedTier?>[] { new(null, "Not classified automatically") }
            .Concat(NeedTierExtensions.All.Select(tier => new ChoiceOption<NeedTier?>(tier, tier.ToDisplayName())))
            .ToList();

    public IReadOnlyList<ChoiceOption<Guid>> OwnerOptions { get; private set; } =
        [new(Guid.Empty, "Not assigned")];

    [ObservableProperty] private string sender = string.Empty;
    [ObservableProperty] private string recipient = string.Empty;
    [ObservableProperty] private Guid sendingOwnerId = Guid.Empty;
    [ObservableProperty] private Guid receivingOwnerId = Guid.Empty;
    [ObservableProperty] private string sendingCountry = "United States";
    [ObservableProperty] private string receivingCountry = "South Africa";
    [ObservableProperty] private string sourceCurrency = "USD";
    [ObservableProperty] private string destinationCurrency = "ZAR";
    [ObservableProperty] private string amountSent = string.Empty;
    [ObservableProperty] private string exchangeRate = string.Empty;
    [ObservableProperty] private string amountReceived = string.Empty;
    [ObservableProperty] private string provider = string.Empty;
    [ObservableProperty] private string providerFee = "0";
    [ObservableProperty] private string sendingBankFee = "0";
    [ObservableProperty] private string intermediaryBankFee = "0";
    [ObservableProperty] private string recipientFee = "0";
    [ObservableProperty] private TransferPurpose purpose = TransferPurpose.Unknown;
    [ObservableProperty] private TransferRelationship relationship = TransferRelationship.OtherFamilyMember;
    [ObservableProperty] private TransferTaxClassification taxClassification = TransferTaxClassification.UnknownExcludeFromSafeToSpend;
    [ObservableProperty] private DateTime? transferDate;
    [ObservableProperty] private Frequency transferFrequency = Frequency.OneOff;
    [ObservableProperty] private bool isFixedDestinationAmount;
    [ObservableProperty] private string commitmentName = string.Empty;
    [ObservableProperty] private string fixedUsd = string.Empty;
    [ObservableProperty] private string fixedZar = string.Empty;
    [ObservableProperty] private string expectedRate = string.Empty;
    [ObservableProperty] private string expectedFees = "0";
    [ObservableProperty] private string safetyMargin = "0";
    [ObservableProperty] private Frequency commitmentFrequency = Frequency.Monthly;
    [ObservableProperty] private NeedTier? commitmentTier;
    [ObservableProperty] private DateTime? commitmentDue;
    [ObservableProperty] private string? statusMessage;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private string conversionSummary = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditorTitle))]
    [NotifyPropertyChangedFor(nameof(EditorSaveLabel))]
    [NotifyPropertyChangedFor(nameof(IsTransferEditor))]
    private bool isEditorOpen;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditorTitle))]
    [NotifyPropertyChangedFor(nameof(EditorSaveLabel))]
    [NotifyPropertyChangedFor(nameof(IsTransferEditor))]
    private bool isCommitmentEditor;
    [ObservableProperty] private double listScrollOffset;
    [ObservableProperty] private Guid? selectedRecordId;

    public IReadOnlyList<InternationalTransferRow> Transfers { get; private set; } = [];
    public IReadOnlyList<CommitmentRow> Commitments { get; private set; } = [];
    public IReadOnlyList<string> ReviewFlags { get; private set; } = [];

    public bool HasTransfers => Transfers.Count > 0;

    public bool HasCommitments => Commitments.Count > 0;

    public bool IsTransferEditor => IsEditorOpen && !IsCommitmentEditor;

    public string EditorTitle => IsCommitmentEditor
        ? _editingCommitmentId is null ? "Add support commitment" : "Edit support commitment"
        : _editingTransferId is null ? "Add transfer" : "Edit transfer";

    public string EditorSaveLabel => IsCommitmentEditor ? "Save support commitment" : "Save transfer";

    public string? EditorEffectPreview =>
        IsCommitmentEditor
            ? "Enter only the USD or ZAR target and expected rate the household already has. This programme does not invent a rate or fee."
            : "Enter the provider's completed amounts and rate. Historical transfers keep the rate they received. Same-owner movements are not new income.";

    public bool HasEditorChanges => IsEditorOpen && Fingerprint() != _originalFingerprint;

    public bool HasEditorError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public string? EditorError => ErrorMessage;

    public string DirectionHint =>
        IsOutgoingFromUnitedStates()
            ? "Outgoing from the United States."
            : IsIncomingToUnitedStates()
                ? "Incoming to the United States."
                : "Direction follows the sending and receiving countries you enter.";

    public ICommand SaveEditorCommand => IsCommitmentEditor ? SaveCommitmentCommand : SaveTransferCommand;

    public ICommand CancelEditorCommand => CancelTransferEditorCommand;

    public bool TryLeaveEditor()
    {
        if (!IsEditorOpen)
        {
            return true;
        }

        if (!HasEditorChanges || _dialog.Confirm("Unsaved changes", "Close without saving this transfer or commitment?"))
        {
            DismissEditor();
            return true;
        }

        return false;
    }

    public void DismissEditor()
    {
        IsEditorOpen = false;
        IsCommitmentEditor = false;
        _editingTransferId = null;
        _editingCommitmentId = null;
        ErrorMessage = null;
    }

    private string Fingerprint() =>
        IsCommitmentEditor
            ? $"{CommitmentName}|{FixedUsd}|{FixedZar}|{ExpectedRate}|{ExpectedFees}|{SafetyMargin}|{CommitmentFrequency}|{CommitmentTier}|{CommitmentDue}"
            : $"{Sender}|{Recipient}|{SendingOwnerId}|{ReceivingOwnerId}|{SendingCountry}|{ReceivingCountry}|{SourceCurrency}|{DestinationCurrency}|{AmountSent}|{ExchangeRate}|{AmountReceived}|{Provider}|{ProviderFee}|{SendingBankFee}|{IntermediaryBankFee}|{RecipientFee}|{Purpose}|{Relationship}|{TaxClassification}|{TransferDate}|{TransferFrequency}|{IsFixedDestinationAmount}";

    [RelayCommand]
    private void CancelTransferEditor() => DismissEditor();

    [RelayCommand]
    private void SelectTransfer(InternationalTransferRow? row)
    {
        if (row is null)
        {
            return;
        }

        SelectedRecordId = row.Id;
        RematchSelection();
    }

    [RelayCommand]
    private void SelectCommitment(CommitmentRow? row)
    {
        if (row is null)
        {
            return;
        }

        SelectedRecordId = row.Id;
        RematchSelection();
    }

    [RelayCommand]
    private void BeginAddTransfer()
    {
        if (!TryLeaveEditor())
        {
            return;
        }

        _editingTransferId = null;
        IsCommitmentEditor = false;
        Sender = string.Empty;
        Recipient = string.Empty;
        SendingOwnerId = Guid.Empty;
        ReceivingOwnerId = Guid.Empty;
        SendingCountry = "United States";
        ReceivingCountry = "South Africa";
        SourceCurrency = "USD";
        DestinationCurrency = "ZAR";
        AmountSent = string.Empty;
        ExchangeRate = string.Empty;
        AmountReceived = string.Empty;
        Provider = string.Empty;
        ProviderFee = "0";
        SendingBankFee = "0";
        IntermediaryBankFee = "0";
        RecipientFee = "0";
        Purpose = TransferPurpose.Unknown;
        Relationship = TransferRelationship.OtherFamilyMember;
        TaxClassification = TransferTaxClassification.UnknownExcludeFromSafeToSpend;
        TransferDate = null;
        TransferFrequency = Frequency.OneOff;
        IsFixedDestinationAmount = false;
        _originalFingerprint = Fingerprint();
        ErrorMessage = null;
        IsEditorOpen = true;
        OnPropertyChanged(nameof(EditorTitle));
        OnPropertyChanged(nameof(DirectionHint));
    }

    [RelayCommand]
    private void BeginAddCommitment()
    {
        if (!TryLeaveEditor())
        {
            return;
        }

        _editingCommitmentId = null;
        IsCommitmentEditor = true;
        CommitmentName = string.Empty;
        FixedUsd = string.Empty;
        FixedZar = string.Empty;
        ExpectedRate = string.Empty;
        ExpectedFees = "0";
        SafetyMargin = "0";
        CommitmentFrequency = Frequency.Monthly;
        CommitmentTier = null;
        CommitmentDue = null;
        _originalFingerprint = Fingerprint();
        ErrorMessage = null;
        IsEditorOpen = true;
        OnPropertyChanged(nameof(EditorTitle));
    }

    [RelayCommand]
    private void BeginEditTransfer(InternationalTransferRow? row)
    {
        if (row is null || !_session.IsOpen || !TryLeaveEditor())
        {
            return;
        }

        var transfer = _session.Document.InternationalTransfers.FirstOrDefault(item => item.Id == row.Id);
        if (transfer is null)
        {
            return;
        }

        SelectedRecordId = transfer.Id;
        _editingTransferId = transfer.Id;
        IsCommitmentEditor = false;
        Sender = transfer.Sender;
        Recipient = transfer.Recipient;
        SendingOwnerId = transfer.SendingAccountOwnerMemberId ?? Guid.Empty;
        ReceivingOwnerId = transfer.ReceivingAccountOwnerMemberId ?? Guid.Empty;
        SendingCountry = transfer.SendingCountry;
        ReceivingCountry = transfer.ReceivingCountry;
        SourceCurrency = transfer.SourceCurrency;
        DestinationCurrency = transfer.DestinationCurrency;
        AmountSent = AmountParsing.Format(transfer.AmountSent);
        ExchangeRate = AmountParsing.Format(transfer.ExchangeRate);
        AmountReceived = AmountParsing.Format(transfer.AmountReceived);
        Provider = transfer.Provider ?? string.Empty;
        ProviderFee = AmountParsing.Format(transfer.ProviderFee);
        SendingBankFee = AmountParsing.Format(transfer.SendingBankFee);
        IntermediaryBankFee = AmountParsing.Format(transfer.IntermediaryBankFee);
        RecipientFee = AmountParsing.Format(transfer.RecipientFee);
        Purpose = transfer.Purpose;
        Relationship = transfer.Relationship;
        TaxClassification = transfer.TaxClassification;
        TransferDate = transfer.TransferDate.ToDateTime(TimeOnly.MinValue);
        TransferFrequency = transfer.Frequency;
        IsFixedDestinationAmount = transfer.IsFixedDestinationAmount;
        _originalFingerprint = Fingerprint();
        ErrorMessage = null;
        IsEditorOpen = true;
        OnPropertyChanged(nameof(EditorTitle));
        OnPropertyChanged(nameof(DirectionHint));
        RematchSelection();
    }

    [RelayCommand]
    private void BeginEditCommitment(CommitmentRow? row)
    {
        if (row is null || !_session.IsOpen || !TryLeaveEditor())
        {
            return;
        }

        var commitment = _session.Document.SupportCommitments.FirstOrDefault(item => item.Id == row.Id);
        if (commitment is null)
        {
            return;
        }

        SelectedRecordId = commitment.Id;
        _editingCommitmentId = commitment.Id;
        IsCommitmentEditor = true;
        CommitmentName = commitment.Name;
        FixedUsd = commitment.FixedUsdToSend is { } usd ? AmountParsing.Format(usd) : string.Empty;
        FixedZar = commitment.FixedZarToReceive is { } zar ? AmountParsing.Format(zar) : string.Empty;
        ExpectedRate = AmountParsing.Format(commitment.ExpectedRateUsdToZar);
        ExpectedFees = AmountParsing.Format(commitment.ExpectedFees);
        SafetyMargin = AmountParsing.Format(commitment.SafetyMargin * 100m);
        CommitmentFrequency = commitment.Frequency;
        CommitmentTier = commitment.HierarchyTier;
        CommitmentDue = commitment.DueDate?.ToDateTime(TimeOnly.MinValue);
        _originalFingerprint = Fingerprint();
        ErrorMessage = null;
        IsEditorOpen = true;
        OnPropertyChanged(nameof(EditorTitle));
        RematchSelection();
    }

    [RelayCommand]
    private async Task SaveTransferAsync(CancellationToken cancellationToken)
    {
        if (!_session.IsOpen)
        {
            ErrorMessage = "Open the household database first.";
            return;
        }

        if (string.IsNullOrWhiteSpace(Sender) || string.IsNullOrWhiteSpace(Recipient)
            || !AmountParsing.TryParseMoney(AmountSent, out var sent)
            || !AmountParsing.TryParseMoney(AmountReceived, out var received)
            || !AmountParsing.TryParseDecimal(ExchangeRate, out var rate)
            || !AmountParsing.TryParseMoney(ProviderFee, out var fee)
            || !AmountParsing.TryParseMoney(SendingBankFee, out var sendingFee)
            || !AmountParsing.TryParseMoney(IntermediaryBankFee, out var intermediaryFee)
            || !AmountParsing.TryParseMoney(RecipientFee, out var receivingFee)
            || rate <= 0m)
        {
            ErrorMessage = "Enter sender, recipient, amounts, a positive exchange rate and fees. Do not leave the rate blank for the programme to invent one.";
            return;
        }

        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        var existing = _editingTransferId is { } editing
            ? _session.Document.InternationalTransfers.FirstOrDefault(item => item.Id == editing)
            : null;
        var transfer = new InternationalTransfer
        {
            Id = existing?.Id ?? Guid.NewGuid(),
            TransferDate = TransferDate is { } date ? DateOnly.FromDateTime(date) : existing?.TransferDate ?? today,
            Sender = Sender.Trim(),
            Recipient = Recipient.Trim(),
            SendingCountry = string.IsNullOrWhiteSpace(SendingCountry) ? "United States" : SendingCountry.Trim(),
            ReceivingCountry = string.IsNullOrWhiteSpace(ReceivingCountry) ? "South Africa" : ReceivingCountry.Trim(),
            SendingAccountOwnerMemberId = SendingOwnerId == Guid.Empty ? null : SendingOwnerId,
            ReceivingAccountOwnerMemberId = ReceivingOwnerId == Guid.Empty ? null : ReceivingOwnerId,
            SourceCurrency = string.IsNullOrWhiteSpace(SourceCurrency) ? "USD" : SourceCurrency.Trim().ToUpperInvariant(),
            DestinationCurrency = string.IsNullOrWhiteSpace(DestinationCurrency) ? "ZAR" : DestinationCurrency.Trim().ToUpperInvariant(),
            AmountSent = sent,
            ExchangeRate = rate,
            AmountReceived = received,
            Provider = string.IsNullOrWhiteSpace(Provider) ? null : Provider.Trim(),
            ProviderFee = fee,
            SendingBankFee = sendingFee,
            IntermediaryBankFee = intermediaryFee,
            RecipientFee = receivingFee,
            Purpose = Purpose,
            Relationship = Relationship,
            TaxClassification = Purpose == TransferPurpose.Unknown
                ? TransferTaxClassification.UnknownExcludeFromSafeToSpend
                : TaxClassification,
            Frequency = TransferFrequency,
            IsFixedDestinationAmount = IsFixedDestinationAmount,
            OriginalExchangeRate = existing?.OriginalExchangeRate ?? rate,
            OriginalClassification = existing?.OriginalClassification ?? TaxClassification,
            LinkedTransferId = existing?.LinkedTransferId,
            SupportingDocumentId = existing?.SupportingDocumentId,
            HierarchyTier = existing?.HierarchyTier,
            ExchangeRateSafetyMargin = existing?.ExchangeRateSafetyMargin,
            DueDate = existing?.DueDate,
            ReviewStatus = existing?.ReviewStatus ?? ClassificationReviewStatus.Unreviewed,
            Notes = existing?.Notes
        };

        SelectedRecordId = transfer.Id;
        var transfers = _session.Document.InternationalTransfers
            .Where(item => item.Id != transfer.Id)
            .Append(transfer)
            .ToList();

        if (!_session.TryReplace(_session.Document with { InternationalTransfers = transfers }, out var error))
        {
            ErrorMessage = error;
            return;
        }

        ErrorMessage = null;
        StatusMessage = await _session.SaveAsync(cancellationToken)
            ? "Transfer saved with its original rate. Later rate changes will not rewrite it."
            : _session.LastError ?? "The transfer could not be saved.";

        if (StatusMessage?.StartsWith("Transfer saved", StringComparison.Ordinal) == true)
        {
            DismissEditor();
        }
    }

    [RelayCommand]
    private async Task SaveCommitmentAsync(CancellationToken cancellationToken)
    {
        if (!_session.IsOpen)
        {
            ErrorMessage = "Open the household database first.";
            return;
        }

        if (string.IsNullOrWhiteSpace(CommitmentName)
            || !AmountParsing.TryParseDecimal(ExpectedRate, out var rate) || rate <= 0m
            || !AmountParsing.TryParseMoney(ExpectedFees, out var fees)
            || !AmountParsing.TryParseDecimal(SafetyMargin, out var margin) || margin < 0m)
        {
            ErrorMessage = "Enter a name, expected USD/ZAR rate, fees and a non-negative safety margin. Do not leave the rate blank for the programme to invent one.";
            return;
        }

        Money? usd = null;
        Money? zar = null;
        if (!string.IsNullOrWhiteSpace(FixedUsd))
        {
            if (!AmountParsing.TryParseMoney(FixedUsd, out var parsed))
            {
                ErrorMessage = "Enter a valid fixed USD amount or leave it blank.";
                return;
            }

            usd = parsed;
        }

        if (!string.IsNullOrWhiteSpace(FixedZar))
        {
            if (!AmountParsing.TryParseMoney(FixedZar, out var parsed))
            {
                ErrorMessage = "Enter a valid fixed ZAR amount or leave it blank.";
                return;
            }

            zar = parsed;
        }

        var commitment = new RecurringSupportCommitment
        {
            Id = _editingCommitmentId ?? Guid.NewGuid(),
            Name = CommitmentName.Trim(),
            Frequency = CommitmentFrequency,
            FixedUsdToSend = usd,
            FixedZarToReceive = zar,
            ExpectedRateUsdToZar = rate,
            ExpectedFees = fees,
            SafetyMargin = margin / 100m,
            DueDate = CommitmentDue is { } due ? DateOnly.FromDateTime(due) : null,
            HierarchyTier = CommitmentTier
        };

        SelectedRecordId = commitment.Id;
        var commitments = _session.Document.SupportCommitments
            .Where(item => item.Id != commitment.Id)
            .Append(commitment)
            .ToList();

        if (!_session.TryReplace(_session.Document with { SupportCommitments = commitments }, out var error))
        {
            ErrorMessage = error;
            return;
        }

        ErrorMessage = null;
        StatusMessage = await _session.SaveAsync(cancellationToken)
            ? "Support commitment saved. It uses the household-chosen hierarchy tier."
            : _session.LastError ?? "The commitment could not be saved.";

        if (StatusMessage?.StartsWith("Support commitment saved", StringComparison.Ordinal) == true)
        {
            DismissEditor();
        }
    }

    partial void OnSendingCountryChanged(string value) => OnPropertyChanged(nameof(DirectionHint));

    partial void OnReceivingCountryChanged(string value) => OnPropertyChanged(nameof(DirectionHint));

    private bool IsOutgoingFromUnitedStates() =>
        IsUnitedStates(SendingCountry) && !IsUnitedStates(ReceivingCountry);

    private bool IsIncomingToUnitedStates() =>
        !IsUnitedStates(SendingCountry) && IsUnitedStates(ReceivingCountry);

    private static bool IsUnitedStates(string country) =>
        country.Equals("United States", StringComparison.OrdinalIgnoreCase)
        || country.Equals("US", StringComparison.OrdinalIgnoreCase)
        || country.Equals("USA", StringComparison.OrdinalIgnoreCase);

    private void OnSessionChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        if (!_session.IsOpen)
        {
            Transfers = [];
            Commitments = [];
            ReviewFlags = [];
            OwnerOptions = [new(Guid.Empty, "Not assigned")];
            Sender = string.Empty;
            Recipient = string.Empty;
            AmountSent = string.Empty;
            AmountReceived = string.Empty;
            ExchangeRate = string.Empty;
            Provider = string.Empty;
            CommitmentName = string.Empty;
            ConversionSummary = string.Empty;
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

        Transfers = document.InternationalTransfers
            .OrderByDescending(item => item.TransferDate)
            .Select(item => new InternationalTransferRow(
                item.Id,
                $"{item.Sender} → {item.Recipient} · {item.SendingCountry} → {item.ReceivingCountry}",
                $"{item.AmountSent.ToDisplayString()} {item.SourceCurrency} → {item.AmountReceived.ToDisplayString()} {item.DestinationCurrency} at {item.ExchangeRate}",
                item.Purpose.ToString(),
                item.TaxClassification.ToString(),
                item.ExcludedFromConfidentSafeToSpend
                    ? "Excluded from confident safe-to-spend"
                    : item.ReviewStatus.ToString(),
                item.Id == SelectedRecordId))
            .ToList();

        Commitments = document.SupportCommitments
            .Select(item => new CommitmentRow(
                item.Id,
                item.Name,
                item.EstimatedUsdReserve().ToDisplayString(),
                item.HierarchyTier?.ToDisplayName() ?? "Tier not set",
                item.Id == SelectedRecordId))
            .ToList();

        ReviewFlags = document.InternationalTransfers
            .SelectMany(SouthAfricanReview.FlagsFor)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        ConversionSummary =
            "Enter the provider's completed rate. Historical transfers keep the rate they received. " +
            "Unknown incoming money is excluded from confident safe-to-spend.";
        Notify();
    }

    private void RematchSelection()
    {
        Transfers = Transfers
            .Select(item => item with { IsSelected = item.Id == SelectedRecordId })
            .ToList();
        Commitments = Commitments
            .Select(item => item with { IsSelected = item.Id == SelectedRecordId })
            .ToList();
        OnPropertyChanged(nameof(Transfers));
        OnPropertyChanged(nameof(Commitments));
        OnPropertyChanged(nameof(HasTransfers));
        OnPropertyChanged(nameof(HasCommitments));
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(Transfers));
        OnPropertyChanged(nameof(Commitments));
        OnPropertyChanged(nameof(ReviewFlags));
        OnPropertyChanged(nameof(OwnerOptions));
        OnPropertyChanged(nameof(HasTransfers));
        OnPropertyChanged(nameof(HasCommitments));
        OnPropertyChanged(nameof(DirectionHint));
    }
}
