using SecureBudgetManager.App.Services;
using SecureBudgetManager.App.Tests.Fakes;
using SecureBudgetManager.App.ViewModels;
using SecureBudgetManager.Core.Household;
using SecureBudgetManager.Core.Income;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.App.Tests.Security;

public sealed class LockClearsViewModelTests
{
    [Fact]
    public async Task LockClearsHouseholdIncomeExpenseAndDashboard()
    {
        var (session, _, _) = SessionFactory.Open();
        var dialog = new FakeUserDialog();
        var household = new HouseholdViewModel(session, dialog);
        var income = new IncomeViewModel(session, dialog);
        var expenses = new ExpensesViewModel(session, dialog);
        var payroll = new PayrollViewModel(session, dialog);
        var cashFlow = new CashFlowViewModel(session, TimeProvider.System);
        var planning = new PlanningViewModel(session, dialog, TimeProvider.System);
        var debt = new DebtViewModel(session, dialog, TimeProvider.System);
        var savings = new SavingsViewModel(session, dialog, TimeProvider.System);
        var actuals = new ActualsViewModel(session, dialog, TimeProvider.System);
        var thisWeek = new ThisWeekViewModel(session, TimeProvider.System);
        var allocations = new AllocationsViewModel(session, dialog, TimeProvider.System);
        var grocery = new GroceryPlanViewModel(session, dialog, TimeProvider.System);
        var guidance = new LocalGuidanceViewModel(session, dialog, TimeProvider.System);
        var rules = new AllocationRulesViewModel(session, dialog);
        var products = new ProductsViewModel(session, dialog, TimeProvider.System);
        var transfers = new InternationalTransfersViewModel(session, TimeProvider.System);
        var foreign = new ForeignAccountsViewModel(session);
        var dashboard = new DashboardViewModel(session, TimeProvider.System);

        household.HouseholdName = "The Harris household";
        household.BeginAddCommand.Execute(null);
        household.EditorName = "Sam";
        await household.SaveMemberCommand.ExecuteAsync(null);

        income.BeginAddCommand.Execute(null);
        income.EditorName = "Warehouse";
        income.EditorKind = IncomeKindSelection.Salary;
        income.EditorAnnualSalary = "31200";
        income.EditorStartDate = new DateTime(2026, 1, 9);
        await income.SaveEntryCommand.ExecuteAsync(null);

        Assert.NotEmpty(household.Members);
        Assert.NotEmpty(income.Items);
        Assert.False(dashboard.Overview.GrossIncome.IsUnavailable);

        session.Clear();

        Assert.Empty(household.Members);
        Assert.Equal(string.Empty, household.HouseholdName);
        Assert.False(household.IsEditorOpen);
        Assert.Empty(income.Items);
        Assert.False(income.IsEditorOpen);
        Assert.Equal(string.Empty, income.EditorName);
        Assert.Empty(expenses.Items);
        Assert.Empty(payroll.MemberOptions);
        Assert.Equal(string.Empty, payroll.EstimatedNetPayText);
        Assert.Equal(string.Empty, payroll.StateRatePercent);
        Assert.Empty(cashFlow.Days);
        Assert.Equal(string.Empty, cashFlow.StartingBalanceText);
        Assert.Empty(planning.Scenarios);
        Assert.Equal(string.Empty, planning.AdvertisedVsTrue);
        Assert.Equal(string.Empty, planning.AdvertisedPayment);
        Assert.Equal(string.Empty, planning.Description);
        Assert.Empty(debt.Debts);
        Assert.Equal(string.Empty, debt.ComparisonText);
        Assert.Empty(savings.Funds);
        Assert.Empty(savings.Goals);
        Assert.Empty(actuals.Transactions);
        Assert.Empty(actuals.Payslips);
        Assert.Empty(thisWeek.MoneyIn);
        Assert.Equal(string.Empty, thisWeek.SafeToSpendText);
        Assert.Equal(string.Empty, thisWeek.MustNotSpendText);
        Assert.Empty(thisWeek.People);
        Assert.Empty(allocations.Reservations);
        Assert.Empty(allocations.Shares);
        Assert.Empty(grocery.Categories);
        Assert.Equal(string.Empty, grocery.PlanSummary);
        Assert.Empty(guidance.Records);
        Assert.Empty(rules.HierarchyRules);
        Assert.Equal(string.Empty, rules.SafetyBuffer);
        Assert.Empty(products.Products);
        Assert.Empty(products.Prices);
        Assert.Equal(string.Empty, products.ShelfPrice);
        Assert.Empty(transfers.Transfers);
        Assert.Equal(string.Empty, transfers.Sender);
        Assert.Equal(string.Empty, transfers.AmountSent);
        Assert.Empty(foreign.Accounts);
        Assert.Equal(string.Empty, foreign.Institution);
        Assert.Equal(string.Empty, foreign.Nickname);
        Assert.True(dashboard.Overview.GrossIncome.IsUnavailable);
        Assert.Null(dashboard.Overview.GrossIncome.Amount);
        Assert.DoesNotContain("Harris", household.HouseholdName, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Warehouse", income.EditorName, StringComparison.OrdinalIgnoreCase);
    }
}
