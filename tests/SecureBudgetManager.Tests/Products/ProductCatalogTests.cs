using SecureBudgetManager.Core.Guidance;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Products;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.Tests.Products;

public sealed class ProductCatalogTests
{
    private static readonly DateOnly Today = new(2026, 9, 3);

    [Fact]
    public void UnitPricesDoNotAssumeALargerPackageIsCheaper()
    {
        var small = Product("Small oats", 18m, "oz", 1m);
        var large = Product("Large oats", 42m, "oz", 1m);
        var smallUnit = UnitPriceCalculator.For(small, new Money(3.00m));
        var largeUnit = UnitPriceCalculator.For(large, new Money(8.00m));

        Assert.Equal(new Money(3.00m), smallUnit.PerItem);
        Assert.True(largeUnit.PerOunce > smallUnit.PerOunce);
        Assert.Contains("not assumed cheaper", largeUnit.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BrandAndPackageSizeStayDistinct()
    {
        var generic = Product("Store brand milk", 1m, "item", 1m) with { Brand = "Store" };
        var named = Product("Named milk", 1m, "item", 1m) with { Brand = "Named" };

        Assert.NotEqual(generic.Brand, named.Brand);
        Assert.NotEqual(generic.Id, named.Id);
    }

    [Fact]
    public void ExpiredSalePriceFallsBackToShelfPrice()
    {
        var observation = new PriceObservation
        {
            Id = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            ShelfPrice = new Money(10m),
            LoyaltyOrSalePrice = new Money(7m),
            ObservedOn = Today.AddDays(-10),
            SaleEndsOn = Today.AddDays(-1),
            SourceName = "Shelf"
        };

        Assert.True(observation.SaleHasExpired(Today));
        Assert.Equal(new Money(10m), observation.WorkingPrice(Today));
    }

    [Fact]
    public void TobaccoAndAlcoholCostsAreSeparate()
    {
        var tobacco = new TobaccoUse { ProductId = Guid.NewGuid(), PacksPerDay = 1m, PacksPerCarton = 10 };
        var alcohol = new AlcoholUse { ProductId = Guid.NewGuid(), PurchaseFrequency = Frequency.Weekly, QuantityPerPurchase = 1m };

        Assert.Equal(new Money(70m), tobacco.WeeklyCost(new Money(10m)));
        Assert.Equal(new Money(10m), alcohol.WeeklyCost(new Money(10m)));
        Assert.NotEqual(tobacco.AnnualCost(new Money(10m)), alcohol.WeeklyCost(new Money(10m)));
    }

    [Fact]
    public void SalesTaxIsNotAddedWhenExciseIsAlreadyInTheShelfPrice()
    {
        var product = Product("Cigarettes", 1m, "pack", 1m) with { TaxCategory = ProductTaxCategory.Tobacco };
        var observation = new PriceObservation
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            ShelfPrice = new Money(9.50m),
            ObservedOn = Today,
            SourceName = "Retailer",
            TaxCategory = ProductTaxCategory.Tobacco,
            TaxCollection = TaxCollectionMethod.IncludedUpstream,
            ExciseEmbeddedInShelfPrice = true
        };

        var estimate = CheckoutPricer.Estimate(
            observation,
            product,
            [
                new SalesTaxRule
                {
                    Id = Guid.NewGuid(),
                    State = "Utah",
                    Category = ProductTaxCategory.Tobacco,
                    Rate = 0.0725m,
                    EffectiveDate = new DateOnly(2026, 1, 1),
                    OfficialSource = "Utah State Tax Commission",
                    RuleSetVersion = "UT-ST-2026",
                    ExciseAlreadyInShelfPrice = true
                }
            ],
            Today);

        Assert.Equal(Money.Zero, estimate.EstimatedTax);
        Assert.Contains("not added", estimate.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnknownTaxTreatmentRequiresInformation()
    {
        var product = Product("Bread", 20m, "oz", 1m);
        var observation = new PriceObservation
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            ShelfPrice = new Money(3.50m),
            ObservedOn = Today,
            SourceName = "Retailer",
            TaxCollection = TaxCollectionMethod.Unknown
        };

        var estimate = CheckoutPricer.Estimate(observation, product, [], Today);

        Assert.True(estimate.TaxInformationRequired);
        Assert.Contains("Tax information required", estimate.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void GroceryCategoryTotalDisclosesMissingProductPrices()
    {
        var product = Product("Apples", 3m, "lb", 1m) with { Category = "Fruit" };
        var (total, explanation) = GroceryCategoryFromProducts.TypicalWeekly("Fruit", [product], [], Today);

        Assert.Null(total);
        Assert.Contains("Insufficient", explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SuggestedPlannedAndActualPricesRemainSeparate()
    {
        var observation = new PriceObservation
        {
            Id = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            ShelfPrice = new Money(4m),
            EstimatedCheckoutPrice = new Money(4.30m),
            ActualCheckoutPrice = new Money(4.41m),
            ObservedOn = Today,
            SourceName = "Receipt",
            PriceKind = PriceKind.HouseholdActual
        };

        Assert.Equal(PriceKind.HouseholdActual, observation.PriceKind);
        Assert.NotEqual(observation.ShelfPrice, observation.ActualCheckoutPrice);
        Assert.NotEqual(observation.EstimatedCheckoutPrice, observation.ActualCheckoutPrice);
    }

    private static Product Product(string name, decimal size, string unit, decimal units) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Category = "Groceries",
        PackageSize = size,
        UnitOfMeasure = unit,
        UnitsPerPackage = units
    };
}
