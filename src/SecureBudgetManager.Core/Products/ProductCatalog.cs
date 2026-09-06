using SecureBudgetManager.Core.Guidance;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.Core.Products;

public enum ProductTaxCategory
{
    Exempt = 0,
    ReducedRateGroceryFood = 1,
    GeneralMerchandise = 2,
    PreparedFood = 3,
    Alcohol = 4,
    Tobacco = 5,
    Fuel = 6,
    Other = 7,
    Unknown = 8
}

public enum TaxCollectionMethod
{
    IncludedUpstream = 0,
    AddedAtCheckout = 1,
    SeparatelyStated = 2,
    Unknown = 3
}

public sealed record Product
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required string Category { get; init; }

    public string? Subcategory { get; init; }

    public string? Brand { get; init; }

    public string? Description { get; init; }

    public decimal PackageSize { get; init; } = 1m;

    public decimal Quantity { get; init; } = 1m;

    public string UnitOfMeasure { get; init; } = "item";

    public decimal UnitsPerPackage { get; init; } = 1m;

    public string? UpcOrSku { get; init; }

    public bool IsEssential { get; init; }

    public NeedTier Tier { get; init; } = NeedTier.DiscretionaryFreedom;

    public Frequency PurchaseFrequency { get; init; } = Frequency.Weekly;

    public bool IsPreferred { get; init; }

    public IReadOnlyList<Guid> SubstituteProductIds { get; init; } = [];

    public bool IsActive { get; init; } = true;

    public ProductTaxCategory TaxCategory { get; init; } = ProductTaxCategory.Unknown;

    public string? Notes { get; init; }

    public void Validate()
    {
        if (Id == Guid.Empty || string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(Category))
        {
            throw new ArgumentException("A product needs an identifier, name and category.");
        }

        if (PackageSize <= 0m || Quantity <= 0m || UnitsPerPackage <= 0m)
        {
            throw new ArgumentException($"Product \"{Name}\" needs positive package size, quantity and units.");
        }
    }
}

public sealed record PriceObservation
{
    public required Guid Id { get; init; }

    public required Guid ProductId { get; init; }

    public string? Retailer { get; init; }

    public string? StoreLocation { get; init; }

    public CostLocality Locality { get; init; } = CostLocality.UnitedStates;

    public required Money ShelfPrice { get; init; }

    public Money? LoyaltyOrSalePrice { get; init; }

    public Money? TaxAmount { get; init; }

    public Money? DepositOrFee { get; init; }

    public Money? EstimatedCheckoutPrice { get; init; }

    public Money? ActualCheckoutPrice { get; init; }

    public required DateOnly ObservedOn { get; init; }

    public DateOnly? SaleStartsOn { get; init; }

    public DateOnly? SaleEndsOn { get; init; }

    public required string SourceName { get; init; }

    public CostGuidanceSourceType SourceType { get; init; } = CostGuidanceSourceType.LocallyObservedPrice;

    public GuidanceConfidence Confidence { get; init; } = GuidanceConfidence.Medium;

    public PriceKind PriceKind { get; init; } = PriceKind.LocallyObserved;

    public DateOnly? ReviewByDate { get; init; }

    public ProductTaxCategory TaxCategory { get; init; } = ProductTaxCategory.Unknown;

    public TaxCollectionMethod TaxCollection { get; init; } = TaxCollectionMethod.Unknown;

    public bool ExciseEmbeddedInShelfPrice { get; init; }

    public string? Notes { get; init; }

    public bool SaleHasExpired(DateOnly today) =>
        SaleEndsOn is { } end && today > end;

    public Money WorkingPrice(DateOnly today)
    {
        if (LoyaltyOrSalePrice is { } sale && !SaleHasExpired(today))
        {
            return sale;
        }

        return ShelfPrice;
    }

    public void Validate()
    {
        if (Id == Guid.Empty || ProductId == Guid.Empty)
        {
            throw new ArgumentException("A price observation needs identifiers.");
        }

        if (ShelfPrice.IsNegative)
        {
            throw new ArgumentException("A shelf price cannot be negative.");
        }

        if (string.IsNullOrWhiteSpace(SourceName))
        {
            throw new ArgumentException("A price observation needs a source.");
        }
    }
}

public sealed record UnitPriceSet
{
    public required Money PerItem { get; init; }

    public Money? PerOunce { get; init; }

    public Money? PerPound { get; init; }

    public Money? PerKilogram { get; init; }

    public Money? PerLitre { get; init; }

    public Money? PerServing { get; init; }

    public Money? PerPack { get; init; }

    public Money? PerCarton { get; init; }

    public required string Explanation { get; init; }
}

public static class UnitPriceCalculator
{
    public static UnitPriceSet For(Product product, Money packagePrice)
    {
        ArgumentNullException.ThrowIfNull(product);

        var perItem = product.UnitsPerPackage <= 0m
            ? packagePrice
            : (packagePrice / product.UnitsPerPackage).Round();

        var unit = product.UnitOfMeasure.Trim().ToLowerInvariant();
        Money? perOunce = null;
        Money? perPound = null;
        Money? perKilogram = null;
        Money? perLitre = null;

        if (product.PackageSize > 0m)
        {
            var perUnit = (packagePrice / product.PackageSize).Round();
            if (unit is "oz" or "ounce" or "ounces")
            {
                perOunce = perUnit;
                perPound = (perUnit * 16m).Round();
            }
            else if (unit is "lb" or "pound" or "pounds")
            {
                perPound = perUnit;
                perOunce = (perUnit / 16m).Round();
            }
            else if (unit is "kg" or "kilogram" or "kilograms")
            {
                perKilogram = perUnit;
                perPound = (perUnit / 2.2046226218m).Round();
            }
            else if (unit is "l" or "litre" or "liter" or "litres" or "liters")
            {
                perLitre = perUnit;
            }
        }

        return new UnitPriceSet
        {
            PerItem = perItem,
            PerOunce = perOunce,
            PerPound = perPound,
            PerKilogram = perKilogram,
            PerLitre = perLitre,
            PerPack = packagePrice.Round(),
            PerCarton = product.UnitsPerPackage > 1m ? packagePrice.Round() : null,
            Explanation =
                $"Package {packagePrice.ToDisplayString()} ÷ {product.UnitsPerPackage} unit(s) " +
                $"= {perItem.ToDisplayString()} each. A larger package is not assumed cheaper."
        };
    }
}

public sealed record CheckoutEstimate
{
    public required Money ShelfOrSale { get; init; }

    public required Money EstimatedTax { get; init; }

    public required Money DepositOrFee { get; init; }

    public required Money EstimatedTotal { get; init; }

    public required bool TaxInformationRequired { get; init; }

    public required string Explanation { get; init; }
}

public sealed record SalesTaxRule
{
    public required Guid Id { get; init; }

    public required string State { get; init; }

    public string? County { get; init; }

    public string? City { get; init; }

    public required ProductTaxCategory Category { get; init; }

    public required decimal Rate { get; init; }

    public required DateOnly EffectiveDate { get; init; }

    public DateOnly? ReviewByDate { get; init; }

    public required string OfficialSource { get; init; }

    public required string RuleSetVersion { get; init; }

    public TaxCollectionMethod Collection { get; init; } = TaxCollectionMethod.AddedAtCheckout;

    public bool ExciseAlreadyInShelfPrice { get; init; }

    public void Validate()
    {
        if (Rate is < 0m or > 1m)
        {
            throw new ArgumentException("A sales-tax rate must be a fraction between 0 and 1.");
        }
    }
}

public static class CheckoutPricer
{
    public static CheckoutEstimate Estimate(
        PriceObservation observation,
        Product product,
        IEnumerable<SalesTaxRule> rules,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(rules);

        var price = observation.WorkingPrice(today);
        var deposit = observation.DepositOrFee ?? Money.Zero;
        var category = observation.TaxCategory == ProductTaxCategory.Unknown
            ? product.TaxCategory
            : observation.TaxCategory;

        if (observation.TaxCollection == TaxCollectionMethod.Unknown
            || category == ProductTaxCategory.Unknown)
        {
            return new CheckoutEstimate
            {
                ShelfOrSale = price,
                EstimatedTax = Money.Zero,
                DepositOrFee = deposit,
                EstimatedTotal = (price + deposit).Round(),
                TaxInformationRequired = true,
                Explanation = "Tax information required. Checkout cost is an estimate without added tax."
            };
        }

        if (observation.TaxCollection == TaxCollectionMethod.IncludedUpstream
            || observation.ExciseEmbeddedInShelfPrice)
        {
            return new CheckoutEstimate
            {
                ShelfOrSale = price,
                EstimatedTax = Money.Zero,
                DepositOrFee = deposit,
                EstimatedTotal = (price + deposit).Round(),
                TaxInformationRequired = false,
                Explanation =
                    "Shelf price is treated as already including upstream tax or excise. " +
                    "Federal, state or local tax is not added again."
            };
        }

        var rule = rules
            .Where(item => item.Category == category)
            .Where(item => item.EffectiveDate <= today)
            .Where(item => string.Equals(item.State, observation.Locality.State, StringComparison.OrdinalIgnoreCase)
                           || item.State.Length == 0)
            .OrderByDescending(item => item.City is null ? 0 : 1)
            .ThenByDescending(item => item.EffectiveDate)
            .FirstOrDefault();

        if (rule is null)
        {
            return new CheckoutEstimate
            {
                ShelfOrSale = price,
                EstimatedTax = Money.Zero,
                DepositOrFee = deposit,
                EstimatedTotal = (price + deposit).Round(),
                TaxInformationRequired = true,
                Explanation = "Tax information required. No matching sales-tax rule is on file."
            };
        }

        var tax = rule.ExciseAlreadyInShelfPrice
            ? Money.Zero
            : (price * rule.Rate).Round();

        return new CheckoutEstimate
        {
            ShelfOrSale = price,
            EstimatedTax = tax,
            DepositOrFee = deposit,
            EstimatedTotal = (price + tax + deposit).Round(),
            TaxInformationRequired = false,
            Explanation =
                $"{rule.RuleSetVersion} {rule.OfficialSource}: {rule.Rate:P2} on {price.ToDisplayString()} " +
                (rule.ExciseAlreadyInShelfPrice
                    ? "not added because excise is already in the shelf price."
                    : $"= {tax.ToDisplayString()} tax.")
        };
    }
}

public sealed record TobaccoUse
{
    public required Guid ProductId { get; init; }

    public decimal PacksPerDay { get; init; }

    public int PacksPerCarton { get; init; } = 10;

    public Money WeeklyCost(Money pricePerPack) =>
        (pricePerPack * (PacksPerDay * 7m)).Round();

    public Money AverageMonthlyCost(Money pricePerPack) =>
        (WeeklyCost(pricePerPack) * 4.333m).Round();

    public Money AnnualCost(Money pricePerPack) =>
        (WeeklyCost(pricePerPack) * 52m).Round();
}

public sealed record AlcoholUse
{
    public required Guid ProductId { get; init; }

    public Frequency PurchaseFrequency { get; init; } = Frequency.Weekly;

    public decimal QuantityPerPurchase { get; init; } = 1m;

    public Money WeeklyCost(Money checkoutPrice) =>
        FrequencyConverter.Convert(checkoutPrice * QuantityPerPurchase, PurchaseFrequency, Frequency.Weekly).Round();
}

public static class GroceryCategoryFromProducts
{
    public static (Money? Total, string Explanation) TypicalWeekly(
        string category,
        IReadOnlyList<Product> products,
        IReadOnlyList<PriceObservation> observations,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(products);
        ArgumentNullException.ThrowIfNull(observations);

        var included = products
            .Where(product => product.IsActive)
            .Where(product => string.Equals(product.Category, category, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (included.Count == 0)
        {
            return (null, CostGuidanceLookup.NoLocalInformation);
        }

        var running = Money.Zero;
        var parts = new List<string>();

        foreach (var product in included)
        {
            var price = observations
                .Where(item => item.ProductId == product.Id)
                .Where(item => !item.SaleHasExpired(today) || item.LoyaltyOrSalePrice is null)
                .OrderByDescending(item => item.ObservedOn)
                .FirstOrDefault();

            if (price is null)
            {
                return (null, $"Insufficient product price information for {product.Name}.");
            }

            var weekly = FrequencyConverter.Convert(
                price.WorkingPrice(today) * product.Quantity,
                product.PurchaseFrequency,
                Frequency.Weekly);
            running += weekly;
            parts.Add(
                $"{product.Name} {price.WorkingPrice(today).ToDisplayString()} at {price.Retailer ?? "unspecified"} " +
                $"on {price.ObservedOn:yyyy-MM-dd}");
        }

        return (running.Round(),
            "Suggested from product-level observations: " + string.Join("; ", parts) + ".");
    }
}
