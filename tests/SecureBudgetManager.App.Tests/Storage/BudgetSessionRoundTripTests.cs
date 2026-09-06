using Microsoft.Extensions.Logging.Abstractions;
using SecureBudgetManager.App.Services;
using SecureBudgetManager.App.ViewModels;
using SecureBudgetManager.Core.Expenses;
using SecureBudgetManager.Core.Household;
using SecureBudgetManager.Core.Income;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Security;
using SecureBudgetManager.Core.Time;
using SecureBudgetManager.Infrastructure.Security;
using SecureBudgetManager.Infrastructure.Storage;
using SecureBudgetManager.App.Tests.Fakes;

namespace SecureBudgetManager.App.Tests.Storage;

public sealed class BudgetSessionRoundTripTests : IDisposable
{
    private readonly TempVaultDirectory _directory = new();
    private readonly SqliteBudgetStore _store;
    private readonly BudgetRepository _repository;

    public BudgetSessionRoundTripTests()
    {
        _store = new SqliteBudgetStore(_directory, NullLogger<SqliteBudgetStore>.Instance);
        _store.Open();
        _repository = new BudgetRepository(_store, NullLogger<BudgetRepository>.Instance);
    }

    [Fact]
    public async Task HouseholdIncomeAndExpensesReloadAfterAFreshSession()
    {
        var vault = new FakeVault { Status = VaultStatus.Unlocked };
        var session = new BudgetSession(_repository, vault, NullLogger<BudgetSession>.Instance);
        Assert.True(session.Open());

        var dialog = new FakeUserDialog { NextResult = true };
        var household = new HouseholdViewModel(session, dialog);
        household.HouseholdName = "Round trip house";
        household.BeginAddCommand.Execute(null);
        household.EditorName = "Alex";
        await household.SaveMemberCommand.ExecuteAsync(null);

        var income = new IncomeViewModel(session, dialog);
        income.BeginAddCommand.Execute(null);
        income.EditorName = "Weekly job";
        income.EditorKind = IncomeKindSelection.Salary;
        income.EditorAnnualSalary = "52000";
        income.EditorFrequency = Frequency.Weekly;
        income.EditorStartDate = new DateTime(2026, 2, 6);
        await income.SaveEntryCommand.ExecuteAsync(null);

        var expenses = new ExpensesViewModel(session, dialog);
        expenses.BeginAddCommand.Execute(null);
        expenses.EditorName = "Rent";
        expenses.EditorAmount = "1200";
        expenses.EditorFrequency = Frequency.Monthly;
        expenses.EditorCategory = ExpenseCategory.Housing.Name;
        expenses.EditorDueDate = new DateTime(2026, 2, 1);
        await expenses.SaveEntryCommand.ExecuteAsync(null);

        session.Clear();
        Assert.True(session.Document.IsEmpty);

        var reopened = new BudgetSession(_repository, vault, NullLogger<BudgetSession>.Instance);
        Assert.True(reopened.Open());

        Assert.Equal("Round trip house", reopened.Document.HouseholdName);
        Assert.Equal("Alex", Assert.Single(reopened.Document.Members).Name);
        var salary = Assert.IsType<SalaryIncome>(Assert.Single(reopened.Document.IncomeSources));
        Assert.Equal(new Money(52_000m), salary.AnnualSalary);
        Assert.Equal(Frequency.Weekly, salary.PayFrequency);
        var rent = Assert.Single(reopened.Document.Expenses);
        Assert.Equal(new Money(1200m), rent.ExpectedAmount);
        Assert.Equal(new Money(14_400m), rent.AnnualCost.Round());
    }

    public void Dispose()
    {
        _store.Dispose();
        _directory.Dispose();
    }
}
