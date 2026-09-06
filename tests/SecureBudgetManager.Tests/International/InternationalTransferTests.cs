using SecureBudgetManager.Core.Guidance;
using SecureBudgetManager.Core.International;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Tax;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.Tests.International;

public sealed class InternationalTransferTests
{
    [Fact]
    public void SameOwnerTransferIsNotNewIncome()
    {
        var transfer = Sample(TransferPurpose.SamePersonAccounts, TransferRelationship.SameOwner);

        Assert.False(transfer.CountsAsNewIncome);
        Assert.True(transfer.ExcludedFromConfidentSafeToSpend);
        Assert.Equal(Money.Zero, TransferSafeToSpend.IncomingSpendable([transfer]));
    }

    [Fact]
    public void LinkedMovementsAreNotDoubleCounted()
    {
        var outgoing = Sample(TransferPurpose.SamePersonAccounts, TransferRelationship.SameOwner) with
        {
            Id = Guid.NewGuid(),
            AmountReceived = new Money(17000m)
        };
        var incoming = outgoing with
        {
            Id = Guid.NewGuid(),
            LinkedTransferId = outgoing.Id,
            AmountReceived = new Money(17000m)
        };
        outgoing = outgoing with { LinkedTransferId = incoming.Id };

        var unique = TransferSafeToSpend.UniqueMovements([outgoing, incoming]);

        Assert.Single(unique);
        Assert.Equal(Money.Zero, TransferSafeToSpend.IncomingSpendable([outgoing, incoming]));
    }

    [Fact]
    public void GiftsStaySeparateFromEarnings()
    {
        var gift = Sample(TransferPurpose.Gift, TransferRelationship.OtherFamilyMember) with
        {
            TaxClassification = TransferTaxClassification.ReportingReview
        };
        var wages = Sample(TransferPurpose.EmploymentOrBusinessIncome, TransferRelationship.UnrelatedPerson) with
        {
            TaxClassification = TransferTaxClassification.PossibleTaxableIncome,
            AmountReceived = new Money(500m)
        };

        Assert.False(gift.CountsAsNewIncome);
        Assert.True(wages.CountsAsNewIncome);
    }

    [Fact]
    public void LoansCreateLiabilitiesAndRepaymentsReduceReceivables()
    {
        var loan = Sample(TransferPurpose.LoanReceived, TransferRelationship.OtherFamilyMember) with
        {
            TaxClassification = TransferTaxClassification.Liability
        };
        var repayment = Sample(TransferPurpose.LoanRepayment, TransferRelationship.OtherFamilyMember) with
        {
            TaxClassification = TransferTaxClassification.ReceivableReduction
        };

        Assert.Equal(TransferTaxClassification.Liability, loan.TaxClassification);
        Assert.Equal(TransferTaxClassification.ReceivableReduction, repayment.TaxClassification);
        Assert.False(loan.CountsAsNewIncome);
    }

    [Fact]
    public void ReimbursementsAndUnknownTransfersAreNotConfidentSafeToSpend()
    {
        var reimbursement = Sample(TransferPurpose.Reimbursement, TransferRelationship.BusinessOrService) with
        {
            TaxClassification = TransferTaxClassification.ExpenseOffset
        };
        var unknown = Sample(TransferPurpose.Unknown, TransferRelationship.OtherFamilyMember);

        Assert.True(unknown.ExcludedFromConfidentSafeToSpend);
        Assert.Equal(Money.Zero, TransferSafeToSpend.IncomingSpendable([unknown]));
        Assert.Equal(TransferTaxClassification.ExpenseOffset, reimbursement.TaxClassification);
    }

    [Fact]
    public void UsdZarConversionKeepsFeesAndSpread()
    {
        var transfer = Sample(TransferPurpose.FamilySupport, TransferRelationship.OtherFamilyMember) with
        {
            AmountSent = new Money(200m),
            ExchangeRate = 18.10m,
            AmountReceived = new Money(3547.60m),
            ProviderFee = new Money(4m),
            SendingBankFee = new Money(0m),
            ExchangeRateSpread = 0.15m,
            OriginalExchangeRate = 18.10m
        };

        Assert.Equal(new Money(204m), transfer.TotalCost);
        Assert.Equal(2m, transfer.PercentageLost);
        Assert.Equal(17.738m, transfer.EffectiveRate);
        Assert.Equal(18.10m, transfer.OriginalExchangeRate);
    }

    [Fact]
    public void HistoricalExchangeRateIsNotReplacedByALaterQuote()
    {
        var completed = Sample(TransferPurpose.FamilySupport, TransferRelationship.OtherFamilyMember) with
        {
            ExchangeRate = 17.90m,
            OriginalExchangeRate = 17.90m,
            AmountReceived = new Money(1790m)
        };

        var laterQuote = new ExchangeRateQuote
        {
            Id = Guid.NewGuid(),
            SourceCurrency = "USD",
            DestinationCurrency = "ZAR",
            Rate = 18.40m,
            Timestamp = DateTimeOffset.UtcNow,
            SourceName = "Later quote"
        };

        Assert.Equal(17.90m, completed.ExchangeRate);
        Assert.NotEqual(completed.ExchangeRate, laterQuote.Rate);
    }

    [Fact]
    public void FixedUsdAndFixedZarCommitmentsCalculateDifferentReserves()
    {
        var usd = new RecurringSupportCommitment
        {
            Id = Guid.NewGuid(),
            Name = "Fixed USD",
            Frequency = Frequency.Monthly,
            FixedUsdToSend = new Money(200m),
            ExpectedRateUsdToZar = 18m,
            ExpectedFees = new Money(5m)
        };
        var zar = new RecurringSupportCommitment
        {
            Id = Guid.NewGuid(),
            Name = "Fixed ZAR",
            Frequency = Frequency.Monthly,
            FixedZarToReceive = new Money(3600m),
            ExpectedRateUsdToZar = 18m,
            ExpectedFees = new Money(5m),
            SafetyMargin = 0.05m
        };

        Assert.Equal(new Money(205m), usd.EstimatedUsdReserve());
        Assert.Equal(new Money(215m), zar.EstimatedUsdReserve());
    }

    [Fact]
    public void FbarAndFormThresholdsAreYearSpecificAndUsePossibleLanguage()
    {
        var accounts = new[]
        {
            new ForeignAccount
            {
                Id = Guid.NewGuid(),
                Institution = "Bank",
                Country = "South Africa",
                Nickname = "SA current",
                Currency = "ZAR",
                MaximumCalendarYearBalance = new Money(12_000m)
            }
        };

        var reminders = ForeignReportingLibrary.Evaluate(
            2026,
            FilingStatus.Single,
            livesInUnitedStates: true,
            accounts,
            []);

        Assert.Contains(reminders, item => item.Form.Contains("FBAR", StringComparison.Ordinal) && item.Message.StartsWith(ForeignReportingLibrary.PossibleRequirement, StringComparison.Ordinal));
        Assert.Equal(new Money(10_000m), ForeignReportingLibrary.FbarThreshold(2025));
        Assert.Equal(new Money(10_000m), ForeignReportingLibrary.FbarThreshold(2026));
        Assert.Null(ForeignReportingLibrary.FbarThreshold(2027));

        var form8938 = ForeignReportingLibrary.Form8938Threshold(2026, FilingStatus.MarriedFilingJointly, true);
        Assert.Equal(new Money(100_000m), form8938!.Value.YearEnd);
        Assert.Null(ForeignReportingLibrary.Form8938Threshold(2027, FilingStatus.Single, true));
        Assert.Equal(new Money(100_000m), ForeignReportingLibrary.Form3520IndividualGiftThreshold(2026));
        Assert.Equal(new Money(20_573m), ForeignReportingLibrary.Form3520EntityGiftThreshold(2026));
        Assert.Equal(new Money(20_116m), ForeignReportingLibrary.Form3520EntityGiftThreshold(2025));
    }

    [Fact]
    public void SouthAfricanReviewStaysSeparateFromUnitedStatesClassification()
    {
        var transfer = Sample(TransferPurpose.Gift, TransferRelationship.OtherFamilyMember);
        var flags = SouthAfricanReview.FlagsFor(transfer);

        Assert.Contains(SouthAfricanReview.RequiresReview, flags);
        Assert.Contains(flags, flag => flag.Contains("does not determine", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UnsupportedReportingYearSaysRulesUnavailable()
    {
        var reminders = ForeignReportingLibrary.Evaluate(2028, FilingStatus.Single, true, [], []);

        Assert.All(reminders, item => Assert.Equal("Tax rules unavailable or require review", item.Message));
        Assert.DoesNotContain(reminders, item => item.Message.Contains("legally required", StringComparison.OrdinalIgnoreCase));
    }

    private static InternationalTransfer Sample(TransferPurpose purpose, TransferRelationship relationship) => new()
    {
        Id = Guid.NewGuid(),
        TransferDate = new DateOnly(2026, 8, 1),
        Sender = "Sender",
        Recipient = "Recipient",
        SendingCountry = "United States",
        ReceivingCountry = "South Africa",
        SourceCurrency = "USD",
        DestinationCurrency = "ZAR",
        AmountSent = new Money(100m),
        ExchangeRate = 18m,
        AmountReceived = new Money(1800m),
        Purpose = purpose,
        Relationship = relationship,
        OriginalExchangeRate = 18m
    };
}
