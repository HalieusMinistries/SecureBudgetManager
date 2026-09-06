using SecureBudgetManager.Core.Benefits;
using SecureBudgetManager.Core.Budgeting;
using SecureBudgetManager.Core.CashFlow;
using SecureBudgetManager.Core.Expenses;
using SecureBudgetManager.Core.Household;
using SecureBudgetManager.Core.Import;
using SecureBudgetManager.Core.Income;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Storage;
using SecureBudgetManager.Core.Tax;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.Tests.Import;

public sealed class HouseholdImportMergerTests
{
    internal static readonly Guid Alex = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    internal static readonly Guid Sam = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    internal static readonly Guid Child = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    internal static readonly Guid WarehouseId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    [Fact]
    public void AnEmptyDocumentReceivesIncomingMembersWithoutDuplicatingThemOnASecondMerge()
    {
        var incoming = SampleHousehold();
        var first = HouseholdImportMerger.Merge(new BudgetDocument(), incoming);
        var second = HouseholdImportMerger.Merge(first.Document, incoming);

        Assert.Equal(3, first.Document.Members.Count);
        Assert.Contains(first.Preview.Created, line => line.Contains("Alex", StringComparison.Ordinal));
        Assert.Equal(3, second.Document.Members.Count);
        Assert.DoesNotContain(second.Preview.Created, line => line.Contains("Alex", StringComparison.Ordinal));
        Assert.All(second.Document.Members.Where(member => member.IsDependant), member =>
        {
            Assert.False(member.IsDiscretionaryEligible);
            Assert.True(member.PersonalAllowance.IsZero);
        });
    }

    [Fact]
    public void MatchingMembersKeepASingleRecordEach()
    {
        var existing = SampleHousehold();
        var result = HouseholdImportMerger.Merge(existing, SampleHousehold());

        Assert.Equal(3, result.Document.Members.Count);
        Assert.Equal(2, result.Document.Members.Count(member => member.IsDiscretionaryEligible));
        Assert.DoesNotContain(result.Preview.Created, line => line.StartsWith("Member ", StringComparison.Ordinal));
    }

    [Fact]
    public void ADifferentHourlyRateOnAMatchingJobIsUpdated()
    {
        var existing = SampleHousehold();
        var incomingIncome = existing.IncomeSources.OfType<HourlyIncome>().Single() with
        {
            HourlyRate = new Money(21m)
        };
        var incoming = existing with
        {
            IncomeSources = existing.IncomeSources
                .Select(source => source.Id == incomingIncome.Id ? incomingIncome : source)
                .ToList()
        };

        var result = HouseholdImportMerger.Merge(existing, incoming);

        var stored = Assert.Single(result.Document.IncomeSources.OfType<HourlyIncome>());
        Assert.Equal(new Money(21m), stored.HourlyRate);
        Assert.Contains(result.Preview.Updated, line => line.Contains("Warehouse", StringComparison.Ordinal));
    }

    internal static BudgetDocument SampleHousehold() => new()
    {
        HouseholdName = "Sample household",
        Members =
        [
            new HouseholdMember { Id = Alex, Name = "Alex", IsDiscretionaryEligible = true },
            new HouseholdMember { Id = Sam, Name = "Sam", IsDiscretionaryEligible = true },
            new HouseholdMember { Id = Child, Name = "Child", IsDependant = true }
        ],
        IncomeSources =
        [
            new HourlyIncome
            {
                Id = WarehouseId,
                Name = "Warehouse",
                MemberId = Alex,
                PayFrequency = Frequency.Weekly,
                AnchorPayDate = new DateOnly(2026, 9, 11),
                HourlyRate = new Money(20m),
                WeeklyHours = VariableHours.Standard
            }
        ]
    };
}

public sealed class IncomeScheduleTests
{
    [Fact]
    public void AnUnconfirmedFortnightlyScheduleEmitsOnlyTheAnchorPayday()
    {
        var source = new HourlyIncome
        {
            Id = Guid.NewGuid(),
            Name = "Cafe",
            MemberId = Guid.NewGuid(),
            PayFrequency = Frequency.Fortnightly,
            AnchorPayDate = new DateOnly(2026, 9, 15),
            PayScheduleConfirmed = false,
            HourlyRate = new Money(18m),
            WeeklyHours = new VariableHours(35m, 40m, 45m)
        };

        var dates = source.PayDates(new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 31)).ToList();

        Assert.Equal([new DateOnly(2026, 9, 15)], dates);
    }

    [Fact]
    public void CashFlowDoesNotInventPaydaysAfterAnUnconfirmedAnchor()
    {
        var source = new HourlyIncome
        {
            Id = Guid.NewGuid(),
            Name = "Cafe",
            MemberId = Guid.NewGuid(),
            PayFrequency = Frequency.Fortnightly,
            AnchorPayDate = new DateOnly(2026, 9, 15),
            PayScheduleConfirmed = false,
            HourlyRate = new Money(18m),
            WeeklyHours = new VariableHours(35m, 40m, 45m)
        };

        var projection = CashFlowProjector.Project(new CashFlowInputs
        {
            From = new DateOnly(2026, 9, 16),
            To = new DateOnly(2026, 11, 30),
            StartingBalance = new Money(250m),
            IncomeSources = [source],
            NetPayPerPeriod = new Dictionary<Guid, Money> { [source.Id] = new(200m) }
        });

        Assert.Equal(Money.Zero, projection.TotalDeposits);
    }
}

public sealed class BenefitConfirmationTests
{
    [Fact]
    public void UnconfirmedBenefitsRemainInTheForecastScenario()
    {
        var memberId = Guid.NewGuid();
        var benefit = new BenefitPlan
        {
            Id = Guid.NewGuid(),
            Name = "Medical",
            Kind = BenefitKind.Medical,
            MemberId = memberId,
            EmployeePremiumPerPeriod = new Money(125.00m),
            PremiumFrequency = Frequency.Weekly,
            IsConfirmed = false,
            Notes = "Likely start date unconfirmed."
        };

        var merged = TakeHomeCalculator.MergeDeductions(
            new PayrollProfile { MemberId = memberId },
            [benefit],
            memberId,
            Frequency.Weekly,
            new DateOnly(2026, 9, 4));

        Assert.Contains(merged, item => item.Name == "Medical" && item.AmountPerPeriod == new Money(125.00m));
        Assert.False(benefit.AppliesOn(new DateOnly(2026, 10, 1)));
    }
}

public sealed class UnknownDueDateTests
{
    [Fact]
    public void AnExpenseWithAnUnknownDueDateStillHasAMonthlyCostButNoCalendarEvents()
    {
        var expense = new ExpenseItem
        {
            Id = Guid.NewGuid(),
            Name = "Electricity",
            Category = ExpenseCategory.Utilities,
            ExpectedAmount = new Money(110m),
            Frequency = Frequency.Monthly,
            AnchorDueDate = new DateOnly(2026, 9, 4),
            DueDateUnknown = true,
            Notes = "Estimate. Due date unknown."
        };

        Assert.Equal(new Money(110m), expense.MonthlyCost);
        Assert.Empty(expense.DueDates(new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 31)));
    }
}

public sealed class HouseholdImportGuardTests
{
    [Fact]
    public void APayloadContainingASocialSecurityPatternIsRefused()
    {
        var json = """{ "householdName": "Sample", "notes": "123-45-6789" }""";
        Assert.Throws<ArgumentException>(() => HouseholdImportSerializer.Parse(json));
    }
}
