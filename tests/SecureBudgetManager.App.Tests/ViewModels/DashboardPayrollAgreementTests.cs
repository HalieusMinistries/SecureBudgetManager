using SecureBudgetManager.App.Tests.Fakes;
using SecureBudgetManager.App.ViewModels;
using SecureBudgetManager.Core.Budgeting;
using SecureBudgetManager.Core.Household;
using SecureBudgetManager.Core.Income;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Tax;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.App.Tests.ViewModels;

public sealed class DashboardPayrollAgreementTests
{
    [Fact]
    public void DashboardTaxesMatchTakeHomeCalculatorForTheSameDocument()
    {
        var (session, _, _) = SessionFactory.Open();
        var memberId = Guid.NewGuid();
        var today = new DateOnly(2026, 3, 6);
        Assert.True(session.TryReplace(session.Document with
        {
            Members = [new HouseholdMember { Id = memberId, Name = "Sam" }],
            IncomeSources =
            [
                new SalaryIncome
                {
                    Id = Guid.NewGuid(),
                    Name = "Warehouse",
                    MemberId = memberId,
                    PayFrequency = Frequency.Weekly,
                    AnchorPayDate = new DateOnly(2026, 1, 9),
                    AnnualSalary = new Money(31_200m)
                }
            ],
            PayrollProfiles =
            [
                new PayrollProfile
                {
                    MemberId = memberId,
                    TaxYear = 2025,
                    StateFlatRate = 0.0455m,
                    W4 = new W4Settings { FilingStatus = FilingStatus.Single }
                }
            ]
        }, out _));

        var dashboard = new DashboardViewModel(session, new FixedClock(today));
        var takeHome = TakeHomeCalculator.From(session.Document, IncomeEstimate.Normal, today);
        var expectedTax = BudgetOverviewCalculator.ToPeriod(takeHome.AnnualTax, DisplayPeriod.AverageMonthly);
        var expectedNet = BudgetOverviewCalculator.ToPeriod(takeHome.AnnualTakeHome, DisplayPeriod.AverageMonthly);

        Assert.False(dashboard.Overview.EstimatedTaxes.IsUnavailable);
        Assert.Equal(expectedTax, dashboard.Overview.EstimatedTaxes.Amount);
        Assert.Equal(expectedNet, dashboard.Overview.TakeHomePay.Amount);
        Assert.True(dashboard.Overview.EstimatedTaxes.IsEstimated);
    }

    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedClock(DateOnly day) =>
            _now = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
