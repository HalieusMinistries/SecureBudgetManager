using SecureBudgetManager.App.Tests.Fakes;
using SecureBudgetManager.App.ViewModels;
using SecureBudgetManager.Core.Household;
using SecureBudgetManager.Core.Income;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.App.Tests.ViewModels;

public sealed class IncomeViewModelTests
{
    private static IncomeViewModel Ready(out FakeBudgetRepository repository)
    {
        var (session, repo, _) = SessionFactory.Open();
        repository = repo;
        var memberId = Guid.NewGuid();
        Assert.True(session.TryReplace(session.Document with
        {
            HouseholdName = "Ours",
            Members = [new HouseholdMember { Id = memberId, Name = "Alex" }]
        }, out _));

        var vm = new IncomeViewModel(session, new FakeUserDialog());
        vm.EditorOwnerId = memberId;
        return vm;
    }

    [Fact]
    public void CannotAddIncomeWithoutAMember()
    {
        var (session, _, _) = SessionFactory.Open();
        var vm = new IncomeViewModel(session, new FakeUserDialog());

        Assert.False(vm.CanAdd);
        Assert.Contains("member", vm.CannotAddReason, StringComparison.OrdinalIgnoreCase);
        Assert.False(vm.BeginAddCommand.CanExecute(null));
    }

    [Fact]
    public async Task HourlyIncomeRequiresARate()
    {
        var vm = Ready(out _);
        vm.BeginAddCommand.Execute(null);
        vm.EditorName = "Warehouse";
        vm.EditorKind = IncomeKindSelection.Hourly;
        vm.EditorHourlyRate = "";

        await vm.SaveEntryCommand.ExecuteAsync(null);

        Assert.Equal("Enter a valid hourly rate.", vm.ErrorMessage);
    }

    [Fact]
    public void FrequencyChoicesExcludeOneOffAndQuarterly()
    {
        var vm = Ready(out _);

        Assert.Contains(vm.FrequencyOptions, option => option.Value == Frequency.Weekly);
        Assert.Contains(vm.FrequencyOptions, option => option.Value == Frequency.Fortnightly);
        Assert.Contains(vm.FrequencyOptions, option => option.Value == Frequency.FourWeekly);
        Assert.Contains(vm.FrequencyOptions, option => option.Value == Frequency.TwiceMonthly);
        Assert.Contains(vm.FrequencyOptions, option => option.Value == Frequency.Monthly);
        Assert.Contains(vm.FrequencyOptions, option => option.Value == Frequency.Annual);
        Assert.Contains(vm.FrequencyOptions, option => option.Value == Frequency.OneOff);
        Assert.DoesNotContain(vm.FrequencyOptions, option => option.Value == Frequency.Quarterly);
    }

    [Fact]
    public void EquivalentsUseAnnualDividedByTwelve()
    {
        var vm = Ready(out _);
        vm.BeginAddCommand.Execute(null);
        vm.EditorName = "Job";
        vm.EditorKind = IncomeKindSelection.Salary;
        vm.EditorFrequency = Frequency.Weekly;
        vm.EditorAnnualSalary = "52000";
        vm.EditorStartDate = new DateTime(2026, 1, 2);

        Assert.Null(vm.ValidateEditor());
        Assert.Contains("1,000.00", vm.EquivalentSummary);
        Assert.Contains("4,333.33", vm.EquivalentSummary);
        Assert.Contains("52,000.00", vm.EquivalentSummary);
        Assert.DoesNotContain("4,000.00", vm.EquivalentSummary);
    }

    [Fact]
    public async Task MileageIsStoredAsNonTaxable()
    {
        var vm = Ready(out var repository);
        vm.BeginAddCommand.Execute(null);
        vm.EditorName = "Van miles";
        vm.EditorKind = IncomeKindSelection.Mileage;
        vm.EditorMiles = "100";
        vm.EditorRatePerMile = "0.67";
        vm.EditorFrequency = Frequency.Weekly;
        vm.EditorStartDate = new DateTime(2026, 1, 2);

        await vm.SaveEntryCommand.ExecuteAsync(null);

        var mileage = Assert.IsType<MileageReimbursement>(Assert.Single(repository.Stored.IncomeSources));
        Assert.False(mileage.IsTaxable);
        Assert.Equal(100m, mileage.MilesPerPeriod);
    }

    [Fact]
    public async Task AddEditDuplicateAndRemove()
    {
        var (session, repository, _) = SessionFactory.Open();
        var memberId = Guid.NewGuid();
        Assert.True(session.TryReplace(session.Document with
        {
            HouseholdName = "Ours",
            Members = [new HouseholdMember { Id = memberId, Name = "Alex" }]
        }, out _));

        var dialog = new FakeUserDialog { NextResult = true };
        var vm = new IncomeViewModel(session, dialog);

        vm.BeginAddCommand.Execute(null);
        vm.EditorName = "Job";
        vm.EditorKind = IncomeKindSelection.Salary;
        vm.EditorAnnualSalary = "26000";
        vm.EditorFrequency = Frequency.Fortnightly;
        vm.EditorStartDate = new DateTime(2026, 1, 9);
        await vm.SaveEntryCommand.ExecuteAsync(null);

        Assert.Single(repository.Stored.IncomeSources);

        vm.BeginEditCommand.Execute(Assert.Single(vm.Items));
        vm.EditorAnnualSalary = "31200";
        await vm.SaveEntryCommand.ExecuteAsync(null);

        Assert.Equal(new Money(31_200m), Assert.IsType<SalaryIncome>(repository.Stored.IncomeSources[0]).AnnualSalary);

        vm.DuplicateCommand.Execute(Assert.Single(vm.Items));
        Assert.True(vm.IsEditorOpen);
        Assert.Contains("(copy)", vm.EditorName, StringComparison.Ordinal);
        await vm.SaveEntryCommand.ExecuteAsync(null);
        Assert.Equal(2, repository.Stored.IncomeSources.Count);

        await vm.RemoveCommand.ExecuteAsync(vm.Items[0]);
        Assert.Single(repository.Stored.IncomeSources);
    }

    [Fact]
    public async Task SaveFailureSurfacesASafeErrorAndKeepsTheEditorOpen()
    {
        var vm = Ready(out var repository);
        repository.ThrowOnSave = true;
        repository.SaveException = new InvalidOperationException("SQLCipher SQLITE_NOTADB cipher mismatch page 1");

        vm.BeginAddCommand.Execute(null);
        vm.EditorName = "Job";
        vm.EditorKind = IncomeKindSelection.Salary;
        vm.EditorAnnualSalary = "20000";
        vm.EditorStartDate = new DateTime(2026, 1, 2);
        await vm.SaveEntryCommand.ExecuteAsync(null);

        Assert.True(vm.IsEditorOpen);
        Assert.NotNull(vm.ErrorMessage);
        Assert.DoesNotContain("SQLCipher", vm.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SQLITE", vm.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }
}
