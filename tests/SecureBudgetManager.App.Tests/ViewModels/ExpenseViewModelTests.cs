using SecureBudgetManager.App.Tests.Fakes;
using SecureBudgetManager.App.ViewModels;
using SecureBudgetManager.Core.Expenses;
using SecureBudgetManager.Core.Household;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.App.Tests.ViewModels;

public sealed class ExpenseViewModelTests
{
    private static (ExpensesViewModel Vm, FakeBudgetRepository Repository, FakeUserDialog Dialog) Ready()
    {
        var (session, repository, _) = SessionFactory.Open();
        var memberId = Guid.NewGuid();
        Assert.True(session.TryReplace(session.Document with
        {
            HouseholdName = "Ours",
            Members = [new HouseholdMember { Id = memberId, Name = "Alex" }]
        }, out _));

        var dialog = new FakeUserDialog();
        return (new ExpensesViewModel(session, dialog), repository, dialog);
    }

    [Fact]
    public async Task AmountIsRequired()
    {
        var (vm, _, _) = Ready();
        vm.BeginAddCommand.Execute(null);
        vm.EditorName = "Rent";
        vm.EditorAmount = "";

        await vm.SaveEntryCommand.ExecuteAsync(null);

        Assert.Equal("Enter a valid amount.", vm.ErrorMessage);
    }

    [Fact]
    public void ExpenseFrequenciesIncludeQuarterlyAndNotOneOff()
    {
        var (vm, _, _) = Ready();

        Assert.Contains(vm.FrequencyOptions, option => option.Value == Frequency.Weekly);
        Assert.Contains(vm.FrequencyOptions, option => option.Value == Frequency.Quarterly);
        Assert.Contains(vm.FrequencyOptions, option => option.Value == Frequency.Annual);
        Assert.DoesNotContain(vm.FrequencyOptions, option => option.Value == Frequency.OneOff);
    }

    [Fact]
    public void EquivalentsDoNotTreatAMonthAsFourWeeks()
    {
        var (vm, _, _) = Ready();
        vm.BeginAddCommand.Execute(null);
        vm.EditorName = "Rent";
        vm.EditorAmount = "1200";
        vm.EditorFrequency = Frequency.Monthly;
        vm.EditorDueDate = new DateTime(2026, 1, 1);

        Assert.Null(vm.ValidateEditor());
        Assert.Contains("14,400.00", vm.EquivalentSummary);
        Assert.Contains("1,200.00", vm.EquivalentSummary);
        Assert.DoesNotContain("4,800.00", vm.EquivalentSummary);
    }

    [Fact]
    public async Task SharedAndIndividualExpensesRoundTripThroughSave()
    {
        var (vm, repository, dialog) = Ready();
        dialog.NextResult = true;

        vm.BeginAddCommand.Execute(null);
        vm.EditorName = "Rent";
        vm.EditorAmount = "1200";
        vm.EditorFrequency = Frequency.Monthly;
        vm.EditorCategory = ExpenseCategory.Housing.Name;
        vm.EditorOwnerId = null;
        vm.EditorDueDate = new DateTime(2026, 3, 1);
        await vm.SaveEntryCommand.ExecuteAsync(null);

        var rent = Assert.Single(repository.Stored.Expenses);
        Assert.Equal(Core.Household.Ownership.Shared, rent.Ownership);

        vm.BeginAddCommand.Execute(null);
        vm.EditorName = "Phone";
        vm.EditorAmount = "40";
        vm.EditorFrequency = Frequency.Monthly;
        vm.EditorOwnerId = repository.Stored.Members[0].Id;
        vm.EditorDueDate = new DateTime(2026, 3, 5);
        await vm.SaveEntryCommand.ExecuteAsync(null);

        Assert.Equal(2, repository.Stored.Expenses.Count);
        Assert.Contains(repository.Stored.Expenses, expense => expense.Ownership == Core.Household.Ownership.Individual);

        vm.DuplicateCommand.Execute(vm.Items[0]);
        await vm.SaveEntryCommand.ExecuteAsync(null);
        Assert.Equal(3, repository.Stored.Expenses.Count);

        await vm.RemoveCommand.ExecuteAsync(vm.Items[0]);
        Assert.Equal(2, repository.Stored.Expenses.Count);
    }

    [Fact]
    public async Task InactiveMapsToPaused()
    {
        var (vm, repository, _) = Ready();
        vm.BeginAddCommand.Execute(null);
        vm.EditorName = "Streaming";
        vm.EditorAmount = "15";
        vm.EditorFrequency = Frequency.Monthly;
        vm.EditorIsActive = false;
        vm.EditorDueDate = new DateTime(2026, 1, 1);
        await vm.SaveEntryCommand.ExecuteAsync(null);

        Assert.True(Assert.Single(repository.Stored.Expenses).IsPaused);
    }
}
