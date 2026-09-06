using SecureBudgetManager.Core.Guidance;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Tax;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.Core.International;

public enum TransferPurpose
{
    Gift = 0,
    FamilySupport = 1,
    SamePersonAccounts = 2,
    LoanReceived = 3,
    LoanMade = 4,
    LoanRepayment = 5,
    Reimbursement = 6,
    PaymentForGoods = 7,
    PaymentForServices = 8,
    EmploymentOrBusinessIncome = 9,
    SaleOfAsset = 10,
    Inheritance = 11,
    InvestmentContribution = 12,
    InvestmentProceeds = 13,
    PensionOrRetirement = 14,
    CharitableContribution = 15,
    Unknown = 16,
    Custom = 17
}

public enum TransferRelationship
{
    SameOwner = 0,
    Spouse = 1,
    OtherFamilyMember = 2,
    UnrelatedPerson = 3,
    BusinessOrService = 4
}

public enum TransferTaxClassification
{
    NotIncome = 0,
    PossibleTaxableIncome = 1,
    Liability = 2,
    ReceivableReduction = 3,
    ExpenseOffset = 4,
    PossibleGainOrLoss = 5,
    ReportingReview = 6,
    UnknownExcludeFromSafeToSpend = 7
}

public enum ClassificationReviewStatus
{
    Unreviewed = 0,
    Reviewed = 1,
    CorrectionRecorded = 2
}

public sealed record ExchangeRateQuote
{
    public required Guid Id { get; init; }

    public required string SourceCurrency { get; init; }

    public required string DestinationCurrency { get; init; }

    public required decimal Rate { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    public required string SourceName { get; init; }

    public decimal? MidMarketRate { get; init; }

    public decimal? ProviderRate { get; init; }

    public decimal? ActualRateReceived { get; init; }

    public decimal? Spread { get; init; }

    public Money ProviderFee { get; init; } = Money.Zero;

    public void Validate()
    {
        if (Rate <= 0m)
        {
            throw new ArgumentException("An exchange rate must be greater than zero.");
        }
    }
}

public sealed record InternationalTransfer
{
    public required Guid Id { get; init; }

    public required DateOnly TransferDate { get; init; }

    public required string Sender { get; init; }

    public required string Recipient { get; init; }

    public required string SendingCountry { get; init; }

    public required string ReceivingCountry { get; init; }

    public Guid? SendingAccountOwnerMemberId { get; init; }

    public Guid? ReceivingAccountOwnerMemberId { get; init; }

    public TransferRelationship Relationship { get; init; } = TransferRelationship.OtherFamilyMember;

    public required string SourceCurrency { get; init; }

    public required string DestinationCurrency { get; init; }

    public required Money AmountSent { get; init; }

    public required decimal ExchangeRate { get; init; }

    public required Money AmountReceived { get; init; }

    public string? Provider { get; init; }

    public Money ProviderFee { get; init; } = Money.Zero;

    public Money SendingBankFee { get; init; } = Money.Zero;

    public Money IntermediaryBankFee { get; init; } = Money.Zero;

    public Money RecipientFee { get; init; } = Money.Zero;

    public decimal? ExchangeRateSpread { get; init; }

    public TransferPurpose Purpose { get; init; } = TransferPurpose.Unknown;

    public string? CustomPurpose { get; init; }

    public TransferTaxClassification TaxClassification { get; init; } =
        TransferTaxClassification.UnknownExcludeFromSafeToSpend;

    public string? ReportingClassification { get; init; }

    public Guid? LinkedTransferId { get; init; }

    public Guid? SupportingDocumentId { get; init; }

    public NeedTier? HierarchyTier { get; init; }

    public Frequency Frequency { get; init; } = Frequency.OneOff;

    public bool IsFixedDestinationAmount { get; init; }

    public decimal? ExchangeRateSafetyMargin { get; init; }

    public DateOnly? DueDate { get; init; }

    public ClassificationReviewStatus ReviewStatus { get; init; }

    public DateOnly? ClassificationCorrectedOn { get; init; }

    public string? ClassificationCorrectionNote { get; init; }

    public TransferTaxClassification? OriginalClassification { get; init; }

    public decimal? OriginalExchangeRate { get; init; }

    public string? Notes { get; init; }

    public Money TotalFees =>
        (ProviderFee + SendingBankFee + IntermediaryBankFee + RecipientFee).Round();

    public Money TotalCost => (AmountSent + TotalFees).Round();

    public decimal EffectiveRate => AmountSent.Amount == 0m
        ? 0m
        : Math.Round(AmountReceived.Amount / AmountSent.Amount, 6);

    public decimal PercentageLost => AmountSent.Amount == 0m
        ? 0m
        : Math.Round(TotalFees.Amount / AmountSent.Amount * 100m, 2);

    public bool CountsAsNewIncome =>
        Purpose is TransferPurpose.EmploymentOrBusinessIncome
            or TransferPurpose.PaymentForServices
            or TransferPurpose.InvestmentProceeds
            or TransferPurpose.PensionOrRetirement
        && TaxClassification == TransferTaxClassification.PossibleTaxableIncome;

    public bool ExcludedFromConfidentSafeToSpend =>
        Purpose == TransferPurpose.Unknown
        || TaxClassification == TransferTaxClassification.UnknownExcludeFromSafeToSpend
        || Purpose == TransferPurpose.SamePersonAccounts
        || Relationship == TransferRelationship.SameOwner;

    public void Validate()
    {
        if (Id == Guid.Empty)
        {
            throw new ArgumentException("A transfer needs an identifier.");
        }

        if (string.IsNullOrWhiteSpace(Sender) || string.IsNullOrWhiteSpace(Recipient))
        {
            throw new ArgumentException("A transfer needs a sender and a recipient.");
        }

        if (AmountSent.IsNegative || AmountReceived.IsNegative || ExchangeRate <= 0m)
        {
            throw new ArgumentException("Transfer amounts and the exchange rate must be positive.");
        }

        if (Purpose == TransferPurpose.Unknown && TaxClassification != TransferTaxClassification.UnknownExcludeFromSafeToSpend)
        {
            throw new ArgumentException("An unknown-purpose transfer cannot be treated as safely spendable.");
        }
    }
}

public sealed record RecurringSupportCommitment
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required Frequency Frequency { get; init; }

    public Money? FixedUsdToSend { get; init; }

    public Money? FixedZarToReceive { get; init; }

    public required decimal ExpectedRateUsdToZar { get; init; }

    public Money ExpectedFees { get; init; } = Money.Zero;

    public decimal SafetyMargin { get; init; }

    public DateOnly? DueDate { get; init; }

    public NeedTier? HierarchyTier { get; init; }

    public Money EstimatedUsdReserve()
    {
        if (FixedUsdToSend is { } usd)
        {
            return (usd + ExpectedFees).Round();
        }

        if (FixedZarToReceive is { } zar)
        {
            if (ExpectedRateUsdToZar <= 0m)
            {
                return Money.Zero;
            }

            var beforeMargin = zar.Amount / ExpectedRateUsdToZar;
            var withMargin = beforeMargin * (1m + SafetyMargin);
            return (new Money(withMargin) + ExpectedFees).Round();
        }

        return Money.Zero;
    }

    public void Validate()
    {
        if (Id == Guid.Empty || string.IsNullOrWhiteSpace(Name))
        {
            throw new ArgumentException("A support commitment needs an identifier and a name.");
        }

        if (FixedUsdToSend is null && FixedZarToReceive is null)
        {
            throw new ArgumentException("A support commitment needs a fixed USD amount to send or a fixed ZAR amount to receive.");
        }

        if (ExpectedRateUsdToZar <= 0m)
        {
            throw new ArgumentException("A support commitment needs an expected exchange rate greater than zero.");
        }

        if (SafetyMargin < 0m)
        {
            throw new ArgumentException("An exchange-rate safety margin cannot be negative.");
        }
    }
}

public sealed record ForeignAccount
{
    public required Guid Id { get; init; }

    public Guid? OwnerMemberId { get; init; }

    public required string Institution { get; init; }

    public required string Country { get; init; }

    public required string Nickname { get; init; }

    public required string Currency { get; init; }

    public Money? MaximumCalendarYearBalance { get; init; }

    public Money? YearEndBalance { get; init; }

    public decimal? ReportingExchangeRate { get; init; }

    public Money? UsdEquivalent { get; init; }

    public bool HasFinancialInterest { get; init; } = true;

    public bool HasSignatureAuthority { get; init; }

    public DateOnly? OpenedOn { get; init; }

    public DateOnly? ClosedOn { get; init; }

    public ClassificationReviewStatus ReviewStatus { get; init; }

    public void Validate()
    {
        if (Id == Guid.Empty || string.IsNullOrWhiteSpace(Institution) || string.IsNullOrWhiteSpace(Nickname))
        {
            throw new ArgumentException("A foreign account needs an identifier, institution and nickname.");
        }

        if (Nickname.Contains("password", StringComparison.OrdinalIgnoreCase)
            || Nickname.Contains("pin", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Do not store banking credentials or PINs.");
        }
    }
}

public sealed record SupportingDocumentRef
{
    public required Guid Id { get; init; }

    public required string Title { get; init; }

    public required string Kind { get; init; }

    public DateOnly? DocumentDate { get; init; }

    public string? EncryptedStorageKey { get; init; }

    public string? Notes { get; init; }

    public void Validate()
    {
        if (Id == Guid.Empty || string.IsNullOrWhiteSpace(Title))
        {
            throw new ArgumentException("A supporting document needs an identifier and a title.");
        }
    }
}

public sealed record ReportingReminder
{
    public required string Form { get; init; }

    public required string Message { get; init; }

    public required string Source { get; init; }

    public required int TaxYear { get; init; }

    public required Money Threshold { get; init; }

    public required bool PossibleRequirement { get; init; }
}

public static class ForeignReportingLibrary
{
    public const string PossibleRequirement = "Possible reporting requirement";

    public static IReadOnlyList<ReportingReminder> Evaluate(
        int taxYear,
        FilingStatus filingStatus,
        bool livesInUnitedStates,
        IReadOnlyList<ForeignAccount> accounts,
        IReadOnlyList<InternationalTransfer> transfers)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(transfers);

        var reminders = new List<ReportingReminder>();
        var fbar = FbarThreshold(taxYear);
        var aggregate = Money.Sum(accounts
            .Select(account => account.MaximumCalendarYearBalance ?? account.UsdEquivalent ?? Money.Zero));

        if (fbar is { } fbarThreshold && aggregate > fbarThreshold)
        {
            reminders.Add(new ReportingReminder
            {
                Form = "FBAR / FinCEN Form 114",
                Message = PossibleRequirement +
                    $": aggregate recorded foreign-account high balances {aggregate.ToDisplayString()} " +
                    $"exceed the {taxYear} FinCEN $10,000 any-time threshold. A reportable account is not " +
                    "necessarily taxable income.",
                Source = "IRS / FinCEN FBAR instructions: aggregate foreign financial accounts over $10,000",
                TaxYear = taxYear,
                Threshold = fbarThreshold,
                PossibleRequirement = true
            });
        }
        else if (fbar is null)
        {
            reminders.Add(Unavailable(taxYear, "FBAR / FinCEN Form 114"));
        }

        var form8938 = Form8938Threshold(taxYear, filingStatus, livesInUnitedStates);
        if (form8938 is { } eight)
        {
            if (aggregate > eight.YearEnd)
            {
                reminders.Add(new ReportingReminder
                {
                    Form = "IRS Form 8938",
                    Message = PossibleRequirement +
                        $": recorded foreign-account value {aggregate.ToDisplayString()} exceeds the " +
                        $"{taxYear} Form 8938 year-end threshold of {eight.YearEnd.ToDisplayString()} " +
                        $"for {filingStatus} {(livesInUnitedStates ? "living in the United States" : "living abroad")}.",
                    Source = "IRS Form 8938 instructions, specified foreign financial asset thresholds",
                    TaxYear = taxYear,
                    Threshold = eight.YearEnd,
                    PossibleRequirement = true
                });
            }
        }
        else
        {
            reminders.Add(Unavailable(taxYear, "IRS Form 8938"));
        }

        var gift = Form3520IndividualGiftThreshold(taxYear);
        var gifts = Money.Sum(transfers
            .Where(transfer => transfer.Purpose is TransferPurpose.Gift or TransferPurpose.Inheritance)
            .Where(transfer => transfer.SendingCountry != "United States")
            .Select(transfer => transfer.AmountReceived));

        if (gift is { } giftThreshold && gifts > giftThreshold)
        {
            reminders.Add(new ReportingReminder
            {
                Form = "IRS Form 3520",
                Message = PossibleRequirement +
                    $": recorded foreign gifts or bequests {gifts.ToDisplayString()} exceed the " +
                    $"{taxYear} Form 3520 individual/estate threshold of {giftThreshold.ToDisplayString()}. " +
                    "A reportable gift is not necessarily taxable income.",
                Source = "IRS Form 3520 instructions, foreign gifts and bequests",
                TaxYear = taxYear,
                Threshold = giftThreshold,
                PossibleRequirement = true
            });
        }
        else if (gift is null)
        {
            reminders.Add(Unavailable(taxYear, "IRS Form 3520"));
        }

        return reminders;
    }

    public static Money? FbarThreshold(int taxYear) =>
        taxYear is 2025 or 2026 ? new Money(10_000m) : null;

    public static (Money YearEnd, Money Anytime)? Form8938Threshold(
        int taxYear,
        FilingStatus status,
        bool livesInUnitedStates)
    {
        if (taxYear is not (2025 or 2026))
        {
            return null;
        }

        if (livesInUnitedStates)
        {
            return status == FilingStatus.MarriedFilingJointly
                ? (new Money(100_000m), new Money(150_000m))
                : (new Money(50_000m), new Money(75_000m));
        }

        return status == FilingStatus.MarriedFilingJointly
            ? (new Money(400_000m), new Money(600_000m))
            : (new Money(200_000m), new Money(300_000m));
    }

    public static Money? Form3520IndividualGiftThreshold(int taxYear) =>
        taxYear is 2025 or 2026 ? new Money(100_000m) : null;

    public static Money? Form3520EntityGiftThreshold(int taxYear) => taxYear switch
    {
        2025 => new Money(20_116m),
        2026 => new Money(20_573m),
        _ => null
    };

    private static ReportingReminder Unavailable(int year, string form) =>
        new()
        {
            Form = form,
            Message = "Tax rules unavailable or require review",
            Source = "No verified threshold is on file for this year.",
            TaxYear = year,
            Threshold = Money.Zero,
            PossibleRequirement = false
        };
}

public static class SouthAfricanReview
{
    public const string RequiresReview =
        "South African tax or exchange-control treatment requires review";

    public static IReadOnlyList<string> FlagsFor(InternationalTransfer transfer)
    {
        ArgumentNullException.ThrowIfNull(transfer);

        var flags = new List<string>();
        var involvesSa =
            IsSouthAfrica(transfer.SendingCountry) || IsSouthAfrica(transfer.ReceivingCountry);

        if (!involvesSa)
        {
            return flags;
        }

        flags.Add(RequiresReview);
        flags.Add(IsSouthAfrica(transfer.SendingCountry)
            ? "Money sent out of South Africa"
            : "Money sent into South Africa");

        flags.Add(transfer.Purpose switch
        {
            TransferPurpose.Gift or TransferPurpose.CharitableContribution => "Gifts and donations",
            TransferPurpose.FamilySupport => "Family maintenance",
            TransferPurpose.LoanMade or TransferPurpose.LoanReceived => "Loans",
            TransferPurpose.LoanRepayment => "Loan repayments",
            TransferPurpose.EmploymentOrBusinessIncome or TransferPurpose.PaymentForServices => "Foreign earnings",
            TransferPurpose.SaleOfAsset => "Asset-sale proceeds",
            TransferPurpose.Inheritance => "Inheritances",
            TransferPurpose.SamePersonAccounts => "Own-account transfers",
            _ => "Classification not yet mapped to a South African treatment"
        });

        if (transfer.Purpose != TransferPurpose.SamePersonAccounts)
        {
            flags.Add("Exchange-control review");
        }

        flags.Add("A United States classification does not determine the South African treatment.");
        return flags;
    }

    private static bool IsSouthAfrica(string country) =>
        country.Equals("South Africa", StringComparison.OrdinalIgnoreCase)
        || country.Equals("ZA", StringComparison.OrdinalIgnoreCase);
}

public static class TransferSafeToSpend
{
    public static IReadOnlyList<InternationalTransfer> UniqueMovements(
        IEnumerable<InternationalTransfer> transfers)
    {
        ArgumentNullException.ThrowIfNull(transfers);

        var list = transfers.ToList();
        var skip = new HashSet<Guid>();
        var unique = new List<InternationalTransfer>();

        foreach (var transfer in list)
        {
            if (skip.Contains(transfer.Id))
            {
                continue;
            }

            if (transfer.LinkedTransferId is { } linked)
            {
                skip.Add(linked);
            }

            unique.Add(transfer);
        }

        return unique;
    }

    public static Money IncomingSpendable(IEnumerable<InternationalTransfer> transfers)
    {
        ArgumentNullException.ThrowIfNull(transfers);

        return Money.Sum(UniqueMovements(transfers)
            .Where(transfer => !transfer.ExcludedFromConfidentSafeToSpend)
            .Where(transfer => transfer.CountsAsNewIncome
                               || transfer.TaxClassification == TransferTaxClassification.NotIncome
                                  && transfer.Purpose is TransferPurpose.Gift or TransferPurpose.FamilySupport)
            .Select(transfer => transfer.AmountReceived)).Round();
    }

    public static Money IncomingExcluded(IEnumerable<InternationalTransfer> transfers)
    {
        ArgumentNullException.ThrowIfNull(transfers);

        return Money.Sum(UniqueMovements(transfers)
            .Where(transfer => transfer.ExcludedFromConfidentSafeToSpend)
            .Select(transfer => transfer.AmountReceived)).Round();
    }
}
