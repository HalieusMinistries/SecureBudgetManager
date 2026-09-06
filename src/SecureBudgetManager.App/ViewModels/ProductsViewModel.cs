using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureBudgetManager.App.Services;
using SecureBudgetManager.Core.Guidance;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Products;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.App.ViewModels;

public sealed record ProductRow(
    Guid Id,
    string Name,
    string Category,
    string Brand,
    string Size,
    string Status);

public sealed record PriceRow(
    Guid Id,
    string Product,
    string Retailer,
    string Shelf,
    string Working,
    string Unit,
    string Observed,
    string Freshness,
    string TaxNote);

/// <summary>Household products and dated price observations. Guidance never overwrites an actual payment.</summary>
public sealed partial class ProductsViewModel : PageViewModel
{
    private readonly IBudgetSession _session;
    private readonly IUserDialog _dialog;
    private readonly TimeProvider _clock;

    public ProductsViewModel(IBudgetSession session, IUserDialog dialog, TimeProvider clock)
        : base(
            "Products and prices",
            "Catalogue",
            "Named products, package sizes and dated shelf prices. A larger pack is not assumed " +
            "cheaper. Suggested category totals are built only from observations that exist.")
    {
        _session = session;
        _dialog = dialog;
        _clock = clock;
        _session.Changed += OnSessionChanged;
        Refresh();
    }

    public IReadOnlyList<ChoiceOption<NeedTier>> TierOptions { get; } =
        NeedTierExtensions.All.Select(tier => new ChoiceOption<NeedTier>(tier, tier.ToDisplayName())).ToList();

    public IReadOnlyList<ChoiceOption<ProductTaxCategory>> TaxCategoryOptions { get; } =
        Enum.GetValues<ProductTaxCategory>()
            .Select(item => new ChoiceOption<ProductTaxCategory>(item, item.ToString()))
            .ToList();

    public IReadOnlyList<ChoiceOption<TaxCollectionMethod>> TaxCollectionOptions { get; } =
        Enum.GetValues<TaxCollectionMethod>()
            .Select(item => new ChoiceOption<TaxCollectionMethod>(item, item.ToString()))
            .ToList();

    public IReadOnlyList<ChoiceOption<Frequency>> Frequencies { get; } = FrequencyChoices.Expense;

    [ObservableProperty] private ProductRow? selectedProduct;
    [ObservableProperty] private string productName = string.Empty;
    [ObservableProperty] private string category = string.Empty;
    [ObservableProperty] private string subcategory = string.Empty;
    [ObservableProperty] private string brand = string.Empty;
    [ObservableProperty] private string packageSize = "1";
    [ObservableProperty] private string quantity = "1";
    [ObservableProperty] private string unitOfMeasure = "item";
    [ObservableProperty] private string unitsPerPackage = "1";
    [ObservableProperty] private bool isEssential;
    [ObservableProperty] private NeedTier tier = NeedTier.DiscretionaryFreedom;
    [ObservableProperty] private Frequency purchaseFrequency = Frequency.Weekly;
    [ObservableProperty] private bool isPreferred;
    [ObservableProperty] private bool isActive = true;
    [ObservableProperty] private ProductTaxCategory taxCategory = ProductTaxCategory.Unknown;
    [ObservableProperty] private string notes = string.Empty;
    [ObservableProperty] private string shelfPrice = string.Empty;
    [ObservableProperty] private string salePrice = string.Empty;
    [ObservableProperty] private string retailer = string.Empty;
    [ObservableProperty] private string city = string.Empty;
    [ObservableProperty] private TaxCollectionMethod taxCollection = TaxCollectionMethod.Unknown;
    [ObservableProperty] private bool exciseEmbedded;
    [ObservableProperty] private string? statusMessage;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private string categorySuggestion = string.Empty;

    public IReadOnlyList<ProductRow> Products { get; private set; } = [];
    public IReadOnlyList<PriceRow> Prices { get; private set; } = [];

    [RelayCommand]
    private async Task SaveProductAsync(CancellationToken cancellationToken)
    {
        if (!_session.IsOpen)
        {
            ErrorMessage = "Open the household database first.";
            return;
        }

        if (string.IsNullOrWhiteSpace(ProductName) || string.IsNullOrWhiteSpace(Category))
        {
            ErrorMessage = "A product needs a name and a category.";
            return;
        }

        if (!decimal.TryParse(PackageSize, out var size) || size <= 0
            || !decimal.TryParse(Quantity, out var qty) || qty <= 0
            || !decimal.TryParse(UnitsPerPackage, out var units) || units <= 0)
        {
            ErrorMessage = "Package size, quantity and units per package must be positive.";
            return;
        }

        var product = new Product
        {
            Id = SelectedProduct?.Id ?? Guid.NewGuid(),
            Name = ProductName.Trim(),
            Category = Category.Trim(),
            Subcategory = string.IsNullOrWhiteSpace(Subcategory) ? null : Subcategory.Trim(),
            Brand = string.IsNullOrWhiteSpace(Brand) ? null : Brand.Trim(),
            PackageSize = size,
            Quantity = qty,
            UnitOfMeasure = string.IsNullOrWhiteSpace(UnitOfMeasure) ? "item" : UnitOfMeasure.Trim(),
            UnitsPerPackage = units,
            IsEssential = IsEssential,
            Tier = Tier,
            PurchaseFrequency = PurchaseFrequency,
            IsPreferred = IsPreferred,
            IsActive = IsActive,
            TaxCategory = TaxCategory,
            Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim()
        };

        var products = _session.Document.Products.Where(item => item.Id != product.Id).Append(product).ToList();

        if (!_session.TryReplace(_session.Document with { Products = products }, out var error))
        {
            ErrorMessage = error;
            return;
        }

        ErrorMessage = null;
        StatusMessage = await _session.SaveAsync(cancellationToken)
            ? "Product saved."
            : _session.LastError ?? "The product could not be saved.";
    }

    [RelayCommand]
    private async Task SavePriceAsync(CancellationToken cancellationToken)
    {
        if (!_session.IsOpen || SelectedProduct is null)
        {
            ErrorMessage = "Choose a product first.";
            return;
        }

        if (!AmountParsing.TryParseMoney(ShelfPrice, out var shelf) || shelf.IsNegative)
        {
            ErrorMessage = "Enter a shelf price.";
            return;
        }

        Money? sale = null;
        if (!string.IsNullOrWhiteSpace(SalePrice))
        {
            if (!AmountParsing.TryParseMoney(SalePrice, out var parsed) || parsed.IsNegative)
            {
                ErrorMessage = "Enter a valid sale price or leave it blank.";
                return;
            }

            sale = parsed;
        }

        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        var observation = new PriceObservation
        {
            Id = Guid.NewGuid(),
            ProductId = SelectedProduct.Id,
            Retailer = string.IsNullOrWhiteSpace(Retailer) ? null : Retailer.Trim(),
            Locality = new CostLocality
            {
                State = _session.Document.Locality.State,
                County = _session.Document.Locality.County,
                City = string.IsNullOrWhiteSpace(City) ? _session.Document.Locality.City : City.Trim()
            },
            ShelfPrice = shelf,
            LoyaltyOrSalePrice = sale,
            ObservedOn = today,
            SourceName = string.IsNullOrWhiteSpace(Retailer) ? "Household observation" : Retailer.Trim(),
            SourceType = CostGuidanceSourceType.LocallyObservedPrice,
            PriceKind = PriceKind.LocallyObserved,
            TaxCategory = TaxCategory,
            TaxCollection = TaxCollection,
            ExciseEmbeddedInShelfPrice = ExciseEmbedded,
            ReviewByDate = today.AddDays(14)
        };

        var observations = _session.Document.PriceObservations.Append(observation).ToList();

        if (!_session.TryReplace(_session.Document with { PriceObservations = observations }, out var error))
        {
            ErrorMessage = error;
            return;
        }

        ErrorMessage = null;
        StatusMessage = await _session.SaveAsync(cancellationToken)
            ? "Price observation saved. Historical observations stay as recorded."
            : _session.LastError ?? "The price could not be saved.";
    }

    [RelayCommand]
    private async Task DeactivateProductAsync(CancellationToken cancellationToken)
    {
        if (SelectedProduct is null || !_session.IsOpen)
        {
            return;
        }

        var products = _session.Document.Products
            .Select(product => product.Id == SelectedProduct.Id ? product with { IsActive = false } : product)
            .ToList();

        if (!_session.TryReplace(_session.Document with { Products = products }, out var error))
        {
            ErrorMessage = error;
            return;
        }

        ErrorMessage = null;
        StatusMessage = await _session.SaveAsync(cancellationToken)
            ? "Product deactivated. Price history was kept."
            : _session.LastError ?? "The product could not be updated.";
    }

    private void OnSessionChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        if (!_session.IsOpen)
        {
            Products = [];
            Prices = [];
            ProductName = string.Empty;
            Category = string.Empty;
            Brand = string.Empty;
            ShelfPrice = string.Empty;
            SalePrice = string.Empty;
            Retailer = string.Empty;
            Notes = string.Empty;
            CategorySuggestion = string.Empty;
            StatusMessage = null;
            ErrorMessage = null;
            SelectedProduct = null;
            Notify();
            return;
        }

        var document = _session.Document;
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);

        Products = document.Products
            .OrderBy(product => product.Name, StringComparer.CurrentCulture)
            .Select(product => new ProductRow(
                product.Id,
                product.Name,
                product.Category,
                product.Brand ?? "—",
                $"{product.PackageSize} {product.UnitOfMeasure}",
                product.IsActive ? "Active" : "Inactive"))
            .ToList();

        Prices = document.PriceObservations
            .OrderByDescending(item => item.ObservedOn)
            .Select(item =>
            {
                var product = document.Products.FirstOrDefault(candidate => candidate.Id == item.ProductId);
                var working = item.WorkingPrice(today);
                var unit = product is null
                    ? "—"
                    : UnitPriceCalculator.For(product, working).Explanation;
                var checkout = product is null
                    ? null
                    : CheckoutPricer.Estimate(item, product, document.SalesTaxRules, today);
                var age = today.DayNumber - item.ObservedOn.DayNumber;
                var freshness = item.SaleHasExpired(today)
                    ? "Sale expired"
                    : item.ReviewByDate is { } due && today > due
                        ? "Review required"
                        : age > 14 ? "Ageing" : "Current";

                return new PriceRow(
                    item.Id,
                    product?.Name ?? "Unknown product",
                    item.Retailer ?? "Unspecified",
                    item.ShelfPrice.ToDisplayString(),
                    working.ToDisplayString(),
                    unit,
                    item.ObservedOn.ToString("yyyy-MM-dd"),
                    freshness,
                    checkout?.Explanation ?? "Tax information required");
            })
            .ToList();

        if (!string.IsNullOrWhiteSpace(Category))
        {
            var (total, explanation) = GroceryCategoryFromProducts.TypicalWeekly(
                Category,
                document.Products,
                document.PriceObservations,
                today);
            CategorySuggestion = total is { } amount
                ? $"{amount.ToDisplayString()} suggested from product observations. {explanation}"
                : explanation;
        }

        Notify();
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(Products));
        OnPropertyChanged(nameof(Prices));
    }
}
