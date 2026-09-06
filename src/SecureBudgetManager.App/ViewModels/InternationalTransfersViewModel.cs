using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    string Review);

public sealed record CommitmentRow(Guid Id, string Name, string Reserve, string Tier);

/// <summary>United States–South Africa movements. A transfer is not automatically income or a gift.</summary>
public sealed partial class InternationalTransfersViewModel : PageViewModel
{
    private readonly IBudgetSession _session;
    private readonly TimeProvider _clock;

    public InternationalTransfersViewModel(IBudgetSession session, TimeProvider clock)
        : base(
            "International transfers",
            "Remittances",
            "Money moving between the United States and South Africa. Purpose and ownership " +
            "must be recorded. Same-owner movements are not new income. Completed transfers keep " +
            "their original rate.")
    {
        _session = session;
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

    [ObservableProperty] private string sender = string.Empty;
    [ObservableProperty] private string recipient = string.Empty;
    [ObservableProperty] private string sendingCountry = "United States";
    [ObservableProperty] private string receivingCountry = "South Africa";
    [ObservableProperty] private string sourceCurrency = "USD";
    [ObservableProperty] private string destinationCurrency = "ZAR";
    [ObservableProperty] private string amountSent = string.Empty;
    [ObservableProperty] private string exchangeRate = string.Empty;
    [ObservableProperty] private string amountReceived = string.Empty;
    [ObservableProperty] private string provider = string.Empty;
    [ObservableProperty] private string providerFee = "0";
    [ObservableProperty] private TransferPurpose purpose = TransferPurpose.Unknown;
    [ObservableProperty] private TransferRelationship relationship = TransferRelationship.OtherFamilyMember;
    [ObservableProperty] private TransferTaxClassification taxClassification = TransferTaxClassification.UnknownExcludeFromSafeToSpend;
    [ObservableProperty] private DateTime? transferDate;
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

    public IReadOnlyList<InternationalTransferRow> Transfers { get; private set; } = [];
    public IReadOnlyList<CommitmentRow> Commitments { get; private set; } = [];
    public IReadOnlyList<string> ReviewFlags { get; private set; } = [];

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
            || rate <= 0m)
        {
            ErrorMessage = "Enter sender, recipient, amounts, a positive exchange rate and fees.";
            return;
        }

        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        var transfer = new InternationalTransfer
        {
            Id = Guid.NewGuid(),
            TransferDate = TransferDate is { } date ? DateOnly.FromDateTime(date) : today,
            Sender = Sender.Trim(),
            Recipient = Recipient.Trim(),
            SendingCountry = SendingCountry.Trim(),
            ReceivingCountry = ReceivingCountry.Trim(),
            SourceCurrency = SourceCurrency.Trim().ToUpperInvariant(),
            DestinationCurrency = DestinationCurrency.Trim().ToUpperInvariant(),
            AmountSent = sent,
            ExchangeRate = rate,
            AmountReceived = received,
            Provider = string.IsNullOrWhiteSpace(Provider) ? null : Provider.Trim(),
            ProviderFee = fee,
            Purpose = Purpose,
            Relationship = Relationship,
            TaxClassification = Purpose == TransferPurpose.Unknown
                ? TransferTaxClassification.UnknownExcludeFromSafeToSpend
                : TaxClassification,
            OriginalExchangeRate = rate,
            OriginalClassification = TaxClassification
        };

        if (!_session.TryReplace(
                _session.Document with
                {
                    InternationalTransfers = _session.Document.InternationalTransfers.Append(transfer).ToList()
                },
                out var error))
        {
            ErrorMessage = error;
            return;
        }

        ErrorMessage = null;
        StatusMessage = await _session.SaveAsync(cancellationToken)
            ? "Transfer saved with its original rate. Later rate changes will not rewrite it."
            : _session.LastError ?? "The transfer could not be saved.";
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
            ErrorMessage = "Enter a name, expected USD/ZAR rate, fees and a non-negative safety margin.";
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
            Id = Guid.NewGuid(),
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

        if (!_session.TryReplace(
                _session.Document with
                {
                    SupportCommitments = _session.Document.SupportCommitments.Append(commitment).ToList()
                },
                out var error))
        {
            ErrorMessage = error;
            return;
        }

        ErrorMessage = null;
        StatusMessage = await _session.SaveAsync(cancellationToken)
            ? "Support commitment saved. It uses the household-chosen hierarchy tier."
            : _session.LastError ?? "The commitment could not be saved.";
    }

    private void OnSessionChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        if (!_session.IsOpen)
        {
            Transfers = [];
            Commitments = [];
            ReviewFlags = [];
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
            Notify();
            return;
        }

        var document = _session.Document;
        Transfers = document.InternationalTransfers
            .OrderByDescending(item => item.TransferDate)
            .Select(item => new InternationalTransferRow(
                item.Id,
                $"{item.SendingCountry} → {item.ReceivingCountry}",
                $"{item.AmountSent.ToDisplayString()} {item.SourceCurrency} → {item.AmountReceived.ToDisplayString()} {item.DestinationCurrency} at {item.ExchangeRate}",
                item.Purpose.ToString(),
                item.TaxClassification.ToString(),
                item.ExcludedFromConfidentSafeToSpend
                    ? "Excluded from confident safe-to-spend"
                    : item.ReviewStatus.ToString()))
            .ToList();

        Commitments = document.SupportCommitments
            .Select(item => new CommitmentRow(
                item.Id,
                item.Name,
                item.EstimatedUsdReserve().ToDisplayString(),
                item.HierarchyTier?.ToDisplayName() ?? "Tier not set"))
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

    private void Notify()
    {
        OnPropertyChanged(nameof(Transfers));
        OnPropertyChanged(nameof(Commitments));
        OnPropertyChanged(nameof(ReviewFlags));
    }
}
