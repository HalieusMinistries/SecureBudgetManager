using Microsoft.Extensions.Logging.Abstractions;
using SecureBudgetManager.Core.Guidance;
using SecureBudgetManager.Core.Household;
using SecureBudgetManager.Core.International;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Products;
using SecureBudgetManager.Core.Security;
using SecureBudgetManager.Core.Storage;
using SecureBudgetManager.Core.Tax;
using SecureBudgetManager.Core.Time;
using SecureBudgetManager.Infrastructure.Storage;
using SecureBudgetManager.Tests.TestSupport;

namespace SecureBudgetManager.Tests.Storage;

public sealed class GuidanceProductAndTransferPersistenceTests : IDisposable
{
    private readonly TempVaultDirectory _directory = new();
    private readonly SqliteBudgetStore _store;
    private readonly BudgetRepository _repository;

    public GuidanceProductAndTransferPersistenceTests()
    {
        _store = new SqliteBudgetStore(_directory, NullLogger<SqliteBudgetStore>.Instance);
        _store.Open();
        _repository = new BudgetRepository(_store, NullLogger<BudgetRepository>.Instance);
    }

    [Fact]
    public void GuidanceProductsTransfersAndTaxProfileRoundTrip()
    {
        var memberId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var transferId = Guid.NewGuid();

        var document = new BudgetDocument
        {
            HouseholdName = "Persistence check",
            Members = [new HouseholdMember { Id = memberId, Name = "Adult" }],
            Rules = new AllocationRules
            {
                FundingOrder = AllocationRules.DefaultFundingOrder,
                ReductionOrder = AllocationRules.DefaultReductionOrder
            },
            CostGuidance = [StarterGuidancePack.Records[0] with { UserSelectedAmount = new Money(150m) }],
            ImportedGuidancePackVersion = StarterGuidancePack.Version,
            Products =
            [
                new Product
                {
                    Id = productId,
                    Name = "Milk",
                    Category = "Dairy and eggs",
                    Brand = "Store",
                    PackageSize = 1m,
                    UnitOfMeasure = "gallon"
                }
            ],
            PriceObservations =
            [
                new PriceObservation
                {
                    Id = Guid.NewGuid(),
                    ProductId = productId,
                    Retailer = "Local shop",
                    ShelfPrice = new Money(3.49m),
                    ObservedOn = new DateOnly(2026, 8, 20),
                    SourceName = "Shelf"
                }
            ],
            InternationalTransfers =
            [
                new InternationalTransfer
                {
                    Id = transferId,
                    TransferDate = new DateOnly(2026, 8, 1),
                    Sender = "Adult",
                    Recipient = "Relative",
                    SendingCountry = "United States",
                    ReceivingCountry = "South Africa",
                    SendingAccountOwnerMemberId = memberId,
                    SourceCurrency = "USD",
                    DestinationCurrency = "ZAR",
                    AmountSent = new Money(100m),
                    ExchangeRate = 18.2m,
                    AmountReceived = new Money(1820m),
                    Purpose = TransferPurpose.FamilySupport,
                    OriginalExchangeRate = 18.2m
                }
            ],
            SupportCommitments =
            [
                new RecurringSupportCommitment
                {
                    Id = Guid.NewGuid(),
                    Name = "Monthly support",
                    Frequency = Frequency.Monthly,
                    FixedUsdToSend = new Money(150m),
                    ExpectedRateUsdToZar = 18m,
                    HierarchyTier = NeedTier.FamilyAndBelonging
                }
            ],
            ForeignAccounts =
            [
                new ForeignAccount
                {
                    Id = Guid.NewGuid(),
                    OwnerMemberId = memberId,
                    Institution = "SA Bank",
                    Country = "South Africa",
                    Nickname = "Current-ZA",
                    Currency = "ZAR",
                    MaximumCalendarYearBalance = new Money(8000m)
                }
            ],
            PayrollProfiles =
            [
                new PayrollProfile
                {
                    MemberId = memberId,
                    TaxYear = 2026,
                    UsesUtahWithholding = true,
                    TaxResidency = UsTaxResidency.NotYetDetermined
                }
            ]
        };

        _repository.Save(document);
        var loaded = _repository.Load();

        Assert.Equal(StarterGuidancePack.Version, loaded.ImportedGuidancePackVersion);
        Assert.Equal(new Money(150m), Assert.Single(loaded.CostGuidance).UserSelectedAmount);
        Assert.Equal("Milk", Assert.Single(loaded.Products).Name);
        Assert.Equal(new Money(3.49m), Assert.Single(loaded.PriceObservations).ShelfPrice);
        Assert.Equal(18.2m, Assert.Single(loaded.InternationalTransfers).ExchangeRate);
        Assert.Equal(NeedTier.FamilyAndBelonging, Assert.Single(loaded.SupportCommitments).HierarchyTier);
        Assert.Equal("Current-ZA", Assert.Single(loaded.ForeignAccounts).Nickname);
        Assert.True(Assert.Single(loaded.PayrollProfiles).UsesUtahWithholding);
        Assert.Equal(UsTaxResidency.NotYetDetermined, loaded.PayrollProfiles[0].TaxResidency);
        Assert.Equal(AllocationRules.DefaultFundingOrder, loaded.Rules.FundingOrder);
    }

    public void Dispose()
    {
        _store.Dispose();
        _directory.Dispose();
    }
}
