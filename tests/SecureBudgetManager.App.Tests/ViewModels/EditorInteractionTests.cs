using SecureBudgetManager.App.Interaction;
using SecureBudgetManager.App.Tests.Fakes;
using SecureBudgetManager.App.ViewModels;
using SecureBudgetManager.Core.Expenses;
using SecureBudgetManager.Core.Household;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.App.Tests.ViewModels;

public sealed class EditorInteractionTests
{
    [Fact]
    public void HouseholdCancelLeavesTheDocumentUnchanged()
    {
        var (session, _, _) = SessionFactory.Open();
        var dialog = new FakeUserDialog();
        var vm = new HouseholdViewModel(session, dialog);

        vm.BeginAddCommand.Execute(null);
        vm.EditorName = "Alex";
        Assert.True(vm.IsEditorOpen);
        Assert.True(vm.HasEditorChanges);

        vm.CancelEditorCommand.Execute(null);

        Assert.False(vm.IsEditorOpen);
        Assert.Empty(session.Document.Members);
    }

    [Fact]
    public void HouseholdTryLeaveEditorKeepsTheEditorWhenTheUserDeclines()
    {
        var (session, _, _) = SessionFactory.Open();
        var dialog = new FakeUserDialog { NextResult = false };
        var vm = new HouseholdViewModel(session, dialog);

        vm.BeginAddCommand.Execute(null);
        vm.EditorName = "Alex";

        Assert.False(vm.TryLeaveEditor());
        Assert.True(vm.IsEditorOpen);
        Assert.Equal("Unsaved changes", dialog.LastTitle);
    }

    [Fact]
    public void HouseholdDismissEditorClosesWithoutAPrompt()
    {
        var (session, _, _) = SessionFactory.Open();
        var dialog = new FakeUserDialog { NextResult = false };
        var vm = new HouseholdViewModel(session, dialog);

        vm.BeginAddCommand.Execute(null);
        vm.EditorName = "Alex";
        vm.DismissEditor();

        Assert.False(vm.IsEditorOpen);
        Assert.Equal(0, dialog.ConfirmCount);
    }

    [Fact]
    public void PrivacyClearClosesAnOpenHouseholdEditor()
    {
        var (session, _, _) = SessionFactory.Open();
        var vm = new HouseholdViewModel(session, new FakeUserDialog());

        vm.BeginAddCommand.Execute(null);
        vm.EditorName = "Alex";
        session.Clear();

        Assert.False(vm.IsEditorOpen);
        Assert.Empty(vm.Members);
    }

    [Fact]
    public async Task IncomeSaveClosesTheEditorAndRefreshShowsTheRow()
    {
        var (session, _, _) = SessionFactory.Open();
        var memberId = Guid.NewGuid();
        Assert.True(session.TryReplace(session.Document with
        {
            HouseholdName = "Ours",
            Members = [new HouseholdMember { Id = memberId, Name = "Alex" }]
        }, out _));

        var vm = new IncomeViewModel(session, new FakeUserDialog());
        vm.BeginAddCommand.Execute(null);
        vm.EditorName = "Warehouse";
        vm.EditorKind = IncomeKindSelection.Salary;
        vm.EditorOwnerId = memberId;
        vm.EditorAnnualSalary = "31200";
        vm.EditorStartDate = new DateTime(2026, 1, 9);
        await vm.SaveEntryCommand.ExecuteAsync(null);

        Assert.False(vm.IsEditorOpen);
        Assert.Single(vm.Items);
        Assert.Equal("Warehouse", vm.Items[0].Name);
    }

    [Fact]
    public void OpeningABillAndPreviewingAssignmentDoesNotChangeSavedData()
    {
        var (session, repository, _) = SessionFactory.Open();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var expenseId = Guid.NewGuid();
        Assert.True(session.TryReplace(session.Document with
        {
            HouseholdName = "Ours",
            Members =
            [
                new HouseholdMember { Id = first, Name = "Alex" },
                new HouseholdMember { Id = second, Name = "Sam" }
            ],
            Expenses =
            [
                new ExpenseItem
                {
                    Id = expenseId,
                    Name = "Rent",
                    ExpectedAmount = new Money(1200m),
                    Frequency = Frequency.Monthly,
                    Category = ExpenseCategory.Housing,
                    Necessity = ExpenseNecessity.Essential,
                    AnchorDueDate = new DateOnly(2026, 9, 1),
                    Assignment = BillAssignment.Unassigned
                }
            ]
        }, out _));

        var vm = new AllocationsViewModel(session, new FakeUserDialog(), TimeProvider.System);
        var row = Assert.Single(vm.Reservations, item => item.Name == "Rent");
        var saves = repository.SaveCount;

        vm.OpenBillCommand.Execute(row);
        Assert.True(((IEditablePage)vm).IsEditorOpen);
        Assert.Equal(BillAssignment.Unassigned, vm.EditorAssignment);

        vm.EditorAssignment = BillAssignment.MemberPaysAll;
        vm.EditorPayer = vm.Members.First(member => member.Id == first);
        vm.PreviewAssignmentCommand.Execute(null);

        Assert.False(string.IsNullOrWhiteSpace(vm.AssignmentPreview));
        Assert.Equal(BillAssignment.Unassigned, session.Document.Expenses.Single().Assignment);
        Assert.Equal(saves, repository.SaveCount);
    }

    [Fact]
    public void SavingsAddOpensAFocusedEditor()
    {
        var (session, _, _) = SessionFactory.Open();
        var vm = new SavingsViewModel(session, new FakeUserDialog(), TimeProvider.System);

        vm.BeginAddGoalCommand.Execute(null);

        Assert.True(vm.IsEditorOpen);
        Assert.True(vm.IsGoalEditor);
        Assert.Equal("Add savings goal", vm.EditorTitle);
        vm.CancelEditorCommand.Execute(null);
        Assert.False(vm.IsEditorOpen);
        Assert.Empty(session.Document.Goals);
    }
}
