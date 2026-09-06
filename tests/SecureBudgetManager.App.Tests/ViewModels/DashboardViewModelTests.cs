using SecureBudgetManager.App.Tests.Fakes;
using SecureBudgetManager.App.ViewModels;
using SecureBudgetManager.Core.Budgeting;
using SecureBudgetManager.Core.Household;
using SecureBudgetManager.Core.Income;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.App.Tests.ViewModels;

public sealed class DashboardViewModelTests
{
    [Fact]
    public void LockedSessionShowsNoBudgetFigures()
    {
        var (session, _, _) = SessionFactory.Open();
        var clock = TimeProvider.System;
        var vm = new DashboardViewModel(session, clock);

        session.Clear();

        Assert.True(vm.Overview.GrossIncome.IsUnavailable);
        Assert.Null(vm.Overview.GrossIncome.Amount);
        Assert.False(vm.IsUnlocked);
        Assert.Equal("Open the household database to see household figures.", vm.Overview.GrossIncome.Text);
    }

    [Fact]
    public void EmptyDocumentShowsNoRecordsYet()
    {
        var (session, _, _) = SessionFactory.Open();
        var vm = new DashboardViewModel(session, TimeProvider.System);

        Assert.Equal("No records yet", vm.Overview.GrossIncome.Text);
        Assert.True(vm.HasAttention);
    }

    [Fact]
    public void PeriodToggleChangesDisplayedGross()
    {
        var (session, _, _) = SessionFactory.Open();
        var memberId = Guid.NewGuid();
        Assert.True(session.TryReplace(session.Document with
        {
            Members = [new HouseholdMember { Id = memberId, Name = "Alex" }],
            IncomeSources =
            [
                new SalaryIncome
                {
                    Id = Guid.NewGuid(),
                    Name = "Job",
                    MemberId = memberId,
                    PayFrequency = Frequency.Weekly,
                    AnchorPayDate = new DateOnly(2026, 1, 2),
                    AnnualSalary = new Money(52_000m)
                }
            ]
        }, out _));

        var vm = new DashboardViewModel(session, TimeProvider.System);
        vm.ShowWeeklyCommand.Execute(null);
        Assert.Equal(DisplayPeriod.Weekly, vm.Period);
        Assert.Equal(new Money(1000m), vm.Overview.GrossIncome.Amount);

        vm.ShowAnnualCommand.Execute(null);
        Assert.Equal(new Money(52_000m), vm.Overview.GrossIncome.Amount);
    }
}
