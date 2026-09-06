using SecureBudgetManager.App.Interaction;
using SecureBudgetManager.App.Tests.Fakes;
using SecureBudgetManager.App.ViewModels;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.App.Tests.ViewModels;

public sealed class CatalogueEditorInteractionTests
{
    [Fact]
    public void ProductAddOpensABlankEditor()
    {
        var (session, _, _) = SessionFactory.Open();
        var vm = new ProductsViewModel(session, new FakeUserDialog(), TimeProvider.System);

        vm.BeginAddCommand.Execute(null);

        Assert.True(vm.IsEditorOpen);
        Assert.Equal("Add product", vm.EditorTitle);
        Assert.Equal(string.Empty, vm.ProductName);
        Assert.Equal(string.Empty, vm.Category);
        Assert.False(vm.HasEditorChanges);
    }

    [Fact]
    public void ProductCancelLeavesTheCatalogueUnchanged()
    {
        var (session, _, _) = SessionFactory.Open();
        var vm = new ProductsViewModel(session, new FakeUserDialog(), TimeProvider.System);

        vm.BeginAddCommand.Execute(null);
        vm.ProductName = "Oats";
        vm.Category = "Dry goods";
        Assert.True(vm.HasEditorChanges);

        vm.CancelEditorCommand.Execute(null);

        Assert.False(vm.IsEditorOpen);
        Assert.Empty(session.Document.Products);
    }

    [Fact]
    public void ProductTryLeaveKeepsTheEditorWhenTheUserDeclines()
    {
        var (session, _, _) = SessionFactory.Open();
        var dialog = new FakeUserDialog { NextResult = false };
        var vm = new ProductsViewModel(session, dialog, TimeProvider.System);

        vm.BeginAddCommand.Execute(null);
        vm.ProductName = "Oats";

        Assert.False(vm.TryLeaveEditor());
        Assert.True(vm.IsEditorOpen);
        Assert.Equal("Unsaved changes", dialog.LastTitle);
    }

    [Fact]
    public async Task ProductSaveValidatesThenPersistsAndRestoresSelection()
    {
        var (session, _, _) = SessionFactory.Open();
        var vm = new ProductsViewModel(session, new FakeUserDialog(), TimeProvider.System);

        vm.BeginAddCommand.Execute(null);
        await vm.SaveProductCommand.ExecuteAsync(null);
        Assert.True(vm.IsEditorOpen);
        Assert.Equal("A product needs a name and a category.", vm.EditorError);
        Assert.Empty(session.Document.Products);

        vm.ProductName = "Rolled oats";
        vm.Category = "Dry goods";
        vm.PackageSize = "1";
        vm.Quantity = "1";
        vm.UnitsPerPackage = "1";
        await vm.SaveProductCommand.ExecuteAsync(null);

        Assert.False(vm.IsEditorOpen);
        var saved = Assert.Single(session.Document.Products);
        Assert.Equal("Rolled oats", saved.Name);
        var row = Assert.Single(vm.Products);
        Assert.True(row.IsSelected);
        Assert.Equal(saved.Id, vm.SelectedRecordId);
    }

    [Fact]
    public async Task ProductClickSelectsWithoutOpening()
    {
        var (session, _, _) = SessionFactory.Open();
        var vm = new ProductsViewModel(session, new FakeUserDialog(), TimeProvider.System);
        vm.BeginAddCommand.Execute(null);
        vm.ProductName = "Rolled oats";
        vm.Category = "Dry goods";
        await vm.SaveProductCommand.ExecuteAsync(null);

        var row = Assert.Single(vm.Products);
        vm.SelectProductCommand.Execute(row);

        Assert.False(vm.IsEditorOpen);
        Assert.True(Assert.Single(vm.Products).IsSelected);
        Assert.Equal(row.Id, vm.SelectedRecordId);
    }

    [Fact]
    public async Task ProductEditAndDoubleClickOpenTheSelectedRecord()
    {
        var (session, _, _) = SessionFactory.Open();
        var vm = new ProductsViewModel(session, new FakeUserDialog(), TimeProvider.System);
        vm.BeginAddCommand.Execute(null);
        vm.ProductName = "Rolled oats";
        vm.Category = "Dry goods";
        await vm.SaveProductCommand.ExecuteAsync(null);

        var row = Assert.Single(vm.Products);
        vm.BeginEditCommand.Execute(row);

        Assert.True(vm.IsEditorOpen);
        Assert.Equal("Edit product", vm.EditorTitle);
        Assert.Equal("Rolled oats", vm.ProductName);
        Assert.True(row.IsSelected);

        vm.ProductName = "Steel-cut oats";
        await vm.SaveProductCommand.ExecuteAsync(null);

        Assert.Equal("Steel-cut oats", Assert.Single(session.Document.Products).Name);
        Assert.True(Assert.Single(vm.Products).IsSelected);
    }

    [Fact]
    public void PlanningAddOpensABlankEditor()
    {
        var (session, _, _) = SessionFactory.Open();
        var vm = new PlanningViewModel(session, new FakeUserDialog(), TimeProvider.System);

        vm.BeginAddCommand.Execute(null);

        Assert.True(vm.IsEditorOpen);
        Assert.Equal("Add scenario", vm.EditorTitle);
        Assert.Equal(string.Empty, vm.Description);
        Assert.Equal(string.Empty, vm.AdvertisedPayment);
        Assert.Null(vm.PurchaseDate);
        Assert.False(vm.HasEditorChanges);
    }

    [Fact]
    public void PlanningCancelLeavesScenariosUnchanged()
    {
        var (session, _, _) = SessionFactory.Open();
        var vm = new PlanningViewModel(session, new FakeUserDialog(), TimeProvider.System);

        vm.BeginAddCommand.Execute(null);
        vm.Description = "Used hatchback";
        vm.AdvertisedPayment = "235";
        vm.PurchaseDate = new DateTime(2026, 10, 1);
        Assert.True(vm.HasEditorChanges);

        vm.CancelEditorCommand.Execute(null);

        Assert.False(vm.IsEditorOpen);
        Assert.Empty(session.Document.Scenarios);
    }

    [Fact]
    public async Task PlanningSaveValidatesPersistsAndKeepsThePlannerOpen()
    {
        var (session, _, _) = SessionFactory.Open();
        var vm = new PlanningViewModel(session, new FakeUserDialog(), TimeProvider.System);

        vm.BeginAddCommand.Execute(null);
        await vm.SaveScenarioCommand.ExecuteAsync(null);
        Assert.True(vm.IsEditorOpen);
        Assert.Equal("Name the proposed vehicle.", vm.EditorError);
        Assert.Empty(session.Document.Scenarios);

        vm.Description = "Used hatchback";
        vm.PurchaseDate = new DateTime(2026, 10, 1);
        vm.AdvertisedPayment = "235";
        await vm.SaveScenarioCommand.ExecuteAsync(null);

        Assert.True(vm.IsEditorOpen);
        Assert.False(vm.HasEditorChanges);
        var saved = Assert.Single(session.Document.Scenarios);
        Assert.Equal("Used hatchback", saved.Name);
        Assert.True(Assert.Single(vm.Scenarios).IsSelected);
        Assert.False(string.IsNullOrWhiteSpace(vm.AdvertisedVsTrue));
    }

    [Fact]
    public async Task PlanningEditOpensTheSelectedScenario()
    {
        var (session, _, _) = SessionFactory.Open();
        var vm = new PlanningViewModel(session, new FakeUserDialog(), TimeProvider.System);
        vm.BeginAddCommand.Execute(null);
        vm.Description = "Used hatchback";
        vm.PurchaseDate = new DateTime(2026, 10, 1);
        vm.AdvertisedPayment = "235";
        await vm.SaveScenarioCommand.ExecuteAsync(null);
        vm.DismissEditor();

        var row = Assert.Single(vm.Scenarios);
        vm.BeginEditCommand.Execute(row);

        Assert.True(vm.IsEditorOpen);
        Assert.Equal("Edit scenario", vm.EditorTitle);
        Assert.Equal("Used hatchback", vm.Description);
        Assert.True(Assert.Single(vm.Scenarios).IsSelected);
    }

    [Fact]
    public void PlanningUnsavedChangesAreProtected()
    {
        var (session, _, _) = SessionFactory.Open();
        var dialog = new FakeUserDialog { NextResult = false };
        var vm = new PlanningViewModel(session, dialog, TimeProvider.System);

        vm.BeginAddCommand.Execute(null);
        vm.Description = "Used hatchback";

        Assert.False(vm.TryLeaveEditor());
        Assert.True(vm.IsEditorOpen);
        Assert.Equal("Unsaved changes", dialog.LastTitle);
    }

    [Fact]
    public void GuidanceAddOpensABlankEditor()
    {
        var (session, _, _) = SessionFactory.Open();
        var vm = new LocalGuidanceViewModel(session, new FakeUserDialog(), TimeProvider.System);

        vm.BeginAddCommand.Execute(null);

        Assert.True(vm.IsEditorOpen);
        Assert.Equal("Add guidance figure", vm.EditorTitle);
        Assert.Equal(string.Empty, vm.Category);
        Assert.Equal(string.Empty, vm.SourceName);
        Assert.False(vm.HasEditorChanges);
    }

    [Fact]
    public void GuidanceCancelLeavesRecordsUnchanged()
    {
        var (session, _, _) = SessionFactory.Open();
        var vm = new LocalGuidanceViewModel(session, new FakeUserDialog(), TimeProvider.System);

        vm.BeginAddCommand.Execute(null);
        vm.Category = "Produce";
        vm.SourceName = "Store receipt";
        Assert.True(vm.HasEditorChanges);

        vm.CancelEditorCommand.Execute(null);

        Assert.False(vm.IsEditorOpen);
        Assert.Empty(session.Document.CostGuidance);
    }

    [Fact]
    public async Task GuidanceSaveValidatesPersistsAndRestoresSelection()
    {
        var (session, _, _) = SessionFactory.Open();
        var vm = new LocalGuidanceViewModel(session, new FakeUserDialog(), TimeProvider.System);

        vm.BeginAddCommand.Execute(null);
        await vm.SaveRecordCommand.ExecuteAsync(null);
        Assert.True(vm.IsEditorOpen);
        Assert.Equal("Choose or enter the category this figure is for.", vm.EditorError);
        Assert.Empty(session.Document.CostGuidance);

        vm.Category = "Produce";
        vm.SourceName = "Store receipt";
        vm.Low = "20";
        vm.Typical = "35";
        vm.Comfortable = "50";
        await vm.SaveRecordCommand.ExecuteAsync(null);

        Assert.False(vm.IsEditorOpen);
        var saved = Assert.Single(session.Document.CostGuidance);
        Assert.Equal("Produce", saved.Category);
        Assert.Equal("Store receipt", saved.SourceName);
        Assert.True(Assert.Single(vm.Records).IsSelected);
        Assert.Equal(saved.Id, vm.SelectedRecordId);
    }

    [Fact]
    public async Task GuidanceEditOpensTheSelectedRecord()
    {
        var (session, _, _) = SessionFactory.Open();
        var vm = new LocalGuidanceViewModel(session, new FakeUserDialog(), TimeProvider.System);
        vm.BeginAddCommand.Execute(null);
        vm.Category = "Produce";
        vm.SourceName = "Store receipt";
        vm.Low = "20";
        vm.Typical = "35";
        vm.Comfortable = "50";
        await vm.SaveRecordCommand.ExecuteAsync(null);

        var row = Assert.Single(vm.Records);
        vm.BeginEditCommand.Execute(row);

        Assert.True(vm.IsEditorOpen);
        Assert.Equal("Edit guidance figure", vm.EditorTitle);
        Assert.Equal("Produce", vm.Category);
        Assert.Equal("Store receipt", vm.SourceName);

        vm.Typical = "40";
        await vm.SaveRecordCommand.ExecuteAsync(null);

        Assert.Equal(40m, Assert.Single(session.Document.CostGuidance).Typical.Amount);
        Assert.True(Assert.Single(vm.Records).IsSelected);
    }

    [Fact]
    public void GuidanceUnsavedChangesAreProtectedAndScrollOffsetIsKept()
    {
        var (session, _, _) = SessionFactory.Open();
        var dialog = new FakeUserDialog { NextResult = false };
        var vm = new LocalGuidanceViewModel(session, dialog, TimeProvider.System)
        {
            ListScrollOffset = 140
        };

        vm.BeginAddCommand.Execute(null);
        vm.Category = "Produce";

        Assert.False(vm.TryLeaveEditor());
        Assert.True(vm.IsEditorOpen);
        Assert.Equal(140, vm.ListScrollOffset);

        dialog.NextResult = true;
        Assert.True(vm.TryLeaveEditor());
        Assert.False(vm.IsEditorOpen);
        Assert.Equal(140, vm.ListScrollOffset);
    }

    [Fact]
    public void ProductPlanningAndGuidanceImplementTheSharedEditorContract()
    {
        var (session, _, _) = SessionFactory.Open();
        IEditablePage products = new ProductsViewModel(session, new FakeUserDialog(), TimeProvider.System);
        IEditablePage planning = new PlanningViewModel(session, new FakeUserDialog(), TimeProvider.System);
        IEditablePage guidance = new LocalGuidanceViewModel(session, new FakeUserDialog(), TimeProvider.System);

        Assert.NotNull(products.SaveEditorCommand);
        Assert.NotNull(products.CancelEditorCommand);
        Assert.NotNull(planning.SaveEditorCommand);
        Assert.NotNull(planning.CancelEditorCommand);
        Assert.NotNull(guidance.SaveEditorCommand);
        Assert.NotNull(guidance.CancelEditorCommand);
    }
}
