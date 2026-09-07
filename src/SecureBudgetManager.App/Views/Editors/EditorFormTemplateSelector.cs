using System.Windows;
using System.Windows.Controls;
using SecureBudgetManager.App.ViewModels;

namespace SecureBudgetManager.App.Views.Editors;

/// <summary>
/// Picks the overlay editor form explicitly. Implicit DataTemplates on the ContentControl lose to
/// the application page templates for the same view-model type, which leaves the overlay chrome
/// visible and the body empty (the page Grid measures to zero inside the dialog ScrollViewer).
/// </summary>
public sealed class EditorFormTemplateSelector : DataTemplateSelector
{
    public DataTemplate? HouseholdTemplate { get; set; }

    public DataTemplate? IncomeTemplate { get; set; }

    public DataTemplate? ExpenseTemplate { get; set; }

    public DataTemplate? PayrollTemplate { get; set; }

    public DataTemplate? AllocationsTemplate { get; set; }

    public DataTemplate? GroceryTemplate { get; set; }

    public DataTemplate? SavingsTemplate { get; set; }

    public DataTemplate? DebtTemplate { get; set; }

    public DataTemplate? ActualsTemplate { get; set; }

    public DataTemplate? ProductsTemplate { get; set; }

    public DataTemplate? PlanningTemplate { get; set; }

    public DataTemplate? GuidanceTemplate { get; set; }

    public DataTemplate? TransfersTemplate { get; set; }

    public DataTemplate? ForeignAccountsTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object? item, DependencyObject container) => item switch
    {
        HouseholdViewModel => HouseholdTemplate,
        IncomeViewModel => IncomeTemplate,
        ExpensesViewModel => ExpenseTemplate,
        PayrollViewModel => PayrollTemplate,
        AllocationsViewModel => AllocationsTemplate,
        GroceryPlanViewModel => GroceryTemplate,
        SavingsViewModel => SavingsTemplate,
        DebtViewModel => DebtTemplate,
        ActualsViewModel => ActualsTemplate,
        ProductsViewModel => ProductsTemplate,
        PlanningViewModel => PlanningTemplate,
        LocalGuidanceViewModel => GuidanceTemplate,
        InternationalTransfersViewModel => TransfersTemplate,
        ForeignAccountsViewModel => ForeignAccountsTemplate,
        _ => null
    };
}
