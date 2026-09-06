using SecureBudgetManager.App.Interaction;
using SecureBudgetManager.App.Services;
using SecureBudgetManager.App.Tests.Fakes;
using SecureBudgetManager.App.ViewModels;
using SecureBudgetManager.Core.Expenses;
using SecureBudgetManager.Core.Guidance;
using SecureBudgetManager.Core.Household;
using SecureBudgetManager.Core.Income;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Storage;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.App.Tests.ViewModels;

public sealed class GuidedBudgetSaveAndNavigationTests
{
    [Fact]
    public async Task SuccessfulExpenseSaveClosesTheOverlayAndShowsANotice()
    {
        var notice = new WorkspaceNoticeService();
        WorkspaceNoticeService.Current = notice;
        var (session, repository, _) = SessionFactory.Open();
        Assert.True(session.TryReplace(session.Document with
        {
            HouseholdName = "Ours",
            Members = [new HouseholdMember { Id = Guid.NewGuid(), Name = "Alex" }]
        }, out _));

        var vm = new ExpensesViewModel(session, new FakeUserDialog());
        vm.BeginAddCommand.Execute(null);
        vm.EditorName = "Water";
        vm.EditorAmount = "40";
        vm.EditorFrequency = Frequency.Monthly;
        vm.EditorDueDate = new DateTime(2026, 9, 15);
        await vm.SaveEntryCommand.ExecuteAsync(null);

        Assert.False(vm.IsEditorOpen);
        Assert.Equal("Expense saved", vm.StatusMessage);
        Assert.Equal("Expense saved", notice.Message);
        Assert.Equal("Water", Assert.Single(session.Document.Expenses).Name);
        Assert.Equal(1, repository.SaveCount);
        await vm.SaveEntryCommand.ExecuteAsync(null);
        Assert.Single(session.Document.Expenses);
    }

    [Fact]
    public async Task ValidationFailureKeepsTheEditorOpenAndPreservesValues()
    {
        var (session, _, _) = SessionFactory.Open();
        var vm = new ExpensesViewModel(session, new FakeUserDialog());
        vm.BeginAddCommand.Execute(null);
        vm.EditorName = "Water";
        vm.EditorAmount = "";
        await vm.SaveEntryCommand.ExecuteAsync(null);

        Assert.True(vm.IsEditorOpen);
        Assert.Equal("Water", vm.EditorName);
        Assert.Equal("Enter a valid amount.", vm.ErrorMessage);
        Assert.Empty(session.Document.Expenses);
    }

    [Fact]
    public async Task PersistenceFailureKeepsTheEditorOpenWithoutASuccessNotice()
    {
        var notice = new WorkspaceNoticeService();
        WorkspaceNoticeService.Current = notice;
        var (session, repository, _) = SessionFactory.Open();
        Assert.True(session.TryReplace(session.Document with
        {
            HouseholdName = "Ours",
            Members = [new HouseholdMember { Id = Guid.NewGuid(), Name = "Alex" }]
        }, out _));
        repository.ThrowOnSave = true;
        repository.SaveException = new InvalidOperationException("disk unavailable");

        var vm = new ExpensesViewModel(session, new FakeUserDialog());
        vm.BeginAddCommand.Execute(null);
        vm.EditorName = "Water";
        vm.EditorAmount = "40";
        vm.EditorDueDate = new DateTime(2026, 9, 15);
        await vm.SaveEntryCommand.ExecuteAsync(null);

        Assert.True(vm.IsEditorOpen);
        Assert.Contains("This information was not saved.", vm.ErrorMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("cipher", vm.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual("Expense saved", notice.Message);
    }

    [Fact]
    public void CancelLeavesTheDocumentUnchanged()
    {
        var (session, repository, _) = SessionFactory.Open();
        var vm = new ExpensesViewModel(session, new FakeUserDialog());
        vm.BeginAddCommand.Execute(null);
        vm.EditorName = "Water";
        vm.CancelEditorCommand.Execute(null);

        Assert.False(vm.IsEditorOpen);
        Assert.Empty(session.Document.Expenses);
        Assert.Equal(0, repository.SaveCount);
    }

    [Fact]
    public void SuggestedBudgetReviewOpensAndKeepsPeopleSeparate()
    {
        var (session, _, _) = SessionFactory.Open();
        var alex = Guid.NewGuid();
        var sam = Guid.NewGuid();
        Assert.True(session.TryReplace(session.Document with
        {
            HouseholdName = "Ours",
            Members =
            [
                new HouseholdMember { Id = alex, Name = "Alex", IsDiscretionaryEligible = true },
                new HouseholdMember { Id = sam, Name = "Sam", IsDiscretionaryEligible = true }
            ],
            IncomeSources =
            [
                new HourlyIncome
                {
                    Id = Guid.NewGuid(),
                    MemberId = alex,
                    Name = "Warehouse",
                    HourlyRate = new Money(22m),
                    WeeklyHours = new VariableHours(35m, 40m, 40m),
                    PayFrequency = Frequency.Weekly,
                    AnchorPayDate = new DateOnly(2026, 9, 4)
                }
            ],
            Expenses =
            [
                new ExpenseItem
                {
                    Id = Guid.NewGuid(),
                    Name = "Rent",
                    Category = ExpenseCategory.Housing,
                    ExpectedAmount = new Money(1450m),
                    Frequency = Frequency.Monthly,
                    AnchorDueDate = new DateOnly(2026, 9, 1)
                }
            ]
        }, out _));

        var vm = new ExpensesViewModel(session, new FakeUserDialog());
        vm.BeginSuggestedBudget();
        Assert.True(vm.IsSuggestionEditor);
        Assert.True(vm.IsEditorOpen);
        Assert.Contains(vm.SuggestionLines, line => line.Category == "Rent" && line.IsExistingActual);
        Assert.Contains(vm.SuggestionLines, line => line.Owner == "Alex");
        Assert.DoesNotContain(vm.SuggestionLines, line => line.Owner.Contains("Matthew", StringComparison.Ordinal));
        Assert.Equal(new Money(1450m), session.Document.Expenses.Single().ExpectedAmount);
    }

    [Fact]
    public void WorkspaceSidebarUsesDependencyOrder()
    {
        var xaml = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "SecureBudgetManager.App", "Views", "WorkspaceView.xaml"));
        Assert.Contains("SET UP YOUR BUDGET", xaml, StringComparison.Ordinal);
        Assert.Contains("USE YOUR BUDGET", xaml, StringComparison.Ordinal);
        Assert.Contains("PLAN AND REFERENCE", xaml, StringComparison.Ordinal);
        Assert.Contains("Create our suggested starting budget", xaml, StringComparison.Ordinal);

        var setup = xaml.IndexOf("SET UP YOUR BUDGET", StringComparison.Ordinal);
        var use = xaml.IndexOf("USE YOUR BUDGET", StringComparison.Ordinal);
        var plan = xaml.IndexOf("PLAN AND REFERENCE", StringComparison.Ordinal);
        Assert.True(setup < use && use < plan);

        Assert.True(xaml.IndexOf("1. Household", StringComparison.Ordinal) < xaml.IndexOf("2. Income", StringComparison.Ordinal));
        Assert.True(xaml.IndexOf("6. Grocery Plan", StringComparison.Ordinal) < xaml.IndexOf("10. Bills", StringComparison.Ordinal));
        Assert.True(xaml.IndexOf("This Week", use, StringComparison.Ordinal) < xaml.IndexOf("Dashboard", use, StringComparison.Ordinal));
        Assert.Equal(1, Count(xaml, "NavigateToHouseholdCommand"));
        Assert.Equal(1, Count(xaml, "NavigateToAllocationsCommand"));
    }

    [Fact]
    public void EditorSaveCoordinatorReportsNotSavedWithoutAFalseSuccess()
    {
        var notice = new WorkspaceNoticeService();
        WorkspaceNoticeService.Current = notice;
        var result = EditorSaveResult.NotSaved("The local database may be unavailable.");
        Assert.False(result.IsSuccess);
        Assert.True(result.KeepEditorOpen);
        Assert.StartsWith("This information was not saved.", result.Message, StringComparison.Ordinal);
        Assert.Null(notice.Message);
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return index == 0 ? count : count;
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Join(directory.FullName, "SecureBudgetManager.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
