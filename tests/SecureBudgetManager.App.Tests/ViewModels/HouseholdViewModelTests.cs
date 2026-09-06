using SecureBudgetManager.App.Tests.Fakes;
using SecureBudgetManager.App.ViewModels;
using SecureBudgetManager.Core.Household;
using SecureBudgetManager.Core.Income;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.App.Tests.ViewModels;

public sealed class HouseholdViewModelTests
{
    [Fact]
    public async Task SavingRequiresAMemberName()
    {
        var (session, _, _) = SessionFactory.Open();
        var dialog = new FakeUserDialog();
        var vm = new HouseholdViewModel(session, dialog);

        vm.BeginAddCommand.Execute(null);
        vm.EditorName = "   ";

        await vm.SaveMemberCommand.ExecuteAsync(null);

        Assert.Equal("A household member needs a name.", vm.ErrorMessage);
        Assert.Empty(session.Document.Members);
    }

    [Fact]
    public async Task AddEditAndRemoveMembers()
    {
        var (session, repository, _) = SessionFactory.Open();
        var dialog = new FakeUserDialog { NextResult = true };
        var vm = new HouseholdViewModel(session, dialog);

        vm.HouseholdName = "Our house";
        vm.BeginAddCommand.Execute(null);
        vm.EditorName = "Alex";
        await vm.SaveMemberCommand.ExecuteAsync(null);

        Assert.Single(session.Document.Members);
        Assert.Equal("Alex", session.Document.Members[0].Name);
        Assert.False(session.Document.Members[0].IsDependant);
        Assert.Equal(1, repository.SaveCount);
        Assert.False(vm.IsEditorOpen);

        var row = Assert.Single(vm.Members);
        vm.BeginEditCommand.Execute(row);
        vm.EditorName = "Alexandra";
        await vm.SaveMemberCommand.ExecuteAsync(null);

        Assert.Equal("Alexandra", session.Document.Members[0].Name);

        vm.BeginAddCommand.Execute(null);
        vm.EditorName = "Sam";
        await vm.SaveMemberCommand.ExecuteAsync(null);

        var adult = vm.Members.Single(item => item.Name == "Alexandra");
        await vm.RemoveCommand.ExecuteAsync(adult);

        Assert.Single(session.Document.Members);
        Assert.Equal("Sam", session.Document.Members[0].Name);
        Assert.True(dialog.ConfirmCount >= 1);
    }

    [Fact]
    public async Task RemoveIsCancelledWhenTheUserDeclines()
    {
        var (session, repository, _) = SessionFactory.Open();
        var dialog = new FakeUserDialog { NextResult = true };
        var vm = new HouseholdViewModel(session, dialog);

        vm.BeginAddCommand.Execute(null);
        vm.EditorName = "Alex";
        await vm.SaveMemberCommand.ExecuteAsync(null);
        var saves = repository.SaveCount;

        dialog.NextResult = false;
        await vm.RemoveCommand.ExecuteAsync(Assert.Single(vm.Members));

        Assert.Single(session.Document.Members);
        Assert.Equal(saves, repository.SaveCount);
    }

    [Fact]
    public void UnsavedEditorWarnsBeforeDiscard()
    {
        var (session, _, _) = SessionFactory.Open();
        var dialog = new FakeUserDialog { NextResult = false };
        var vm = new HouseholdViewModel(session, dialog);

        vm.BeginAddCommand.Execute(null);
        vm.EditorName = "Alex";

        vm.BeginAddCommand.Execute(null);

        Assert.True(vm.IsEditorOpen);
        Assert.Equal("Alex", vm.EditorName);
        Assert.Equal(1, dialog.ConfirmCount);
    }

    [Fact]
    public async Task CannotRemoveAMemberWhoStillHasIncome()
    {
        var (session, _, _) = SessionFactory.Open();
        var dialog = new FakeUserDialog();
        var memberId = Guid.NewGuid();
        Assert.True(session.TryReplace(session.Document with
        {
            HouseholdName = "Ours",
            Members = [new HouseholdMember { Id = memberId, Name = "Alex" }],
            IncomeSources =
            [
                new SalaryIncome
                {
                    Id = Guid.NewGuid(),
                    Name = "Job",
                    MemberId = memberId,
                    PayFrequency = Frequency.Weekly,
                    AnchorPayDate = new DateOnly(2026, 1, 1),
                    AnnualSalary = new Money(20_000m)
                }
            ]
        }, out _));

        var vm = new HouseholdViewModel(session, dialog);
        await vm.RemoveCommand.ExecuteAsync(Assert.Single(vm.Members));

        Assert.Contains("income", vm.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Single(session.Document.Members);
    }

    [Fact]
    public async Task DiscretionaryEligibilityRequiresAnExplicitConfirmation()
    {
        var (session, _, _) = SessionFactory.Open();
        var dialog = new FakeUserDialog { NextResult = true };
        var vm = new HouseholdViewModel(session, dialog);

        vm.HouseholdName = "Ours";
        vm.BeginAddCommand.Execute(null);
        vm.EditorName = "Alex";
        vm.EditorIsDiscretionaryEligible = true;
        await vm.SaveMemberCommand.ExecuteAsync(null);

        Assert.True(Assert.Single(session.Document.Members).IsDiscretionaryEligible);
        Assert.True(dialog.ConfirmCount >= 1);
        Assert.Contains("personal discretionary", Assert.Single(vm.Members).Designation, StringComparison.OrdinalIgnoreCase);

        vm.BeginAddCommand.Execute(null);
        vm.EditorName = "Sam";
        vm.EditorIsDependant = true;
        await vm.SaveMemberCommand.ExecuteAsync(null);

        var dependant = session.Document.Members.Single(member => member.IsDependant);
        Assert.False(dependant.IsDiscretionaryEligible);
        Assert.Contains("No personal discretionary", vm.Members.Single(item => item.Name == "Sam").Designation, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyStateIsShownWhenThereAreNoMembers()
    {
        var (session, _, _) = SessionFactory.Open();
        var vm = new HouseholdViewModel(session, new FakeUserDialog());

        Assert.True(vm.ShowEmptyState);
        Assert.False(vm.HasMembers);
    }

    [Fact]
    public void ValidateHouseholdNameRequiresANameOnceMembersExist()
    {
        Assert.Equal("A household needs a name.", HouseholdViewModel.ValidateHouseholdName("  ", 1));
        Assert.Null(HouseholdViewModel.ValidateHouseholdName("Ours", 1));
    }
}
