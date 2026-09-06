using SecureBudgetManager.Core.CashFlow;
using SecureBudgetManager.Core.Expenses;
using SecureBudgetManager.Core.Household;
using SecureBudgetManager.Core.Income;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Planning;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.Tests.Planning;

/// <summary>
/// The defining behaviour of this program: never call something affordable merely because the
/// monthly payment fits. Each test below is one way a payment can fit while the household cannot
/// actually afford it.
/// </summary>
public sealed class AffordabilityEngineTests
{
    private static readonly DateOnly Start = new(2026, 3, 1);

    [Fact]
    public void MissingCriticalCostsProduceInsufficientInformationRatherThanAGuess()
    {
        var inputs = Inputs(TrueCostEngineTests.NewCarScenario());

        var assessment = AffordabilityEngine.Assess(inputs);

        Assert.Equal(AffordabilityRating.InsufficientInformation, assessment.Rating);
        Assert.Equal("Insufficient information", assessment.RatingLabel);
        Assert.NotEmpty(assessment.WhatMustChange);
        Assert.Contains("have not been entered", assessment.Findings[0].Explanation);
    }

    [Fact]
    public void AComfortablePurchasePassesEveryCheck()
    {
        var inputs = Inputs(
            TrueCostEngineTests.FullyAnsweredCar(),
            monthlyNetIncome: new Money(6500m),
            monthlyEssentials: new Money(2200m),
            emergencyFund: new Money(20_000m),
            startingBalance: new Money(9000m));

        var assessment = AffordabilityEngine.Assess(inputs);

        Assert.Equal(AffordabilityRating.Comfortable, assessment.Rating);
        Assert.Empty(assessment.FailedRules);
        Assert.Empty(assessment.WhatMustChange);
    }

    [Fact]
    public void APaymentThatFitsButDrivesTheBalanceNegativeIsUnsafe()
    {
        // Income comfortably covers the monthly total, but the timing of the bills means the
        // account still goes below zero. A monthly average hides this completely.
        var inputs = Inputs(
            TrueCostEngineTests.FullyAnsweredCar(),
            monthlyNetIncome: new Money(4200m),
            monthlyEssentials: new Money(1800m),
            emergencyFund: new Money(12_000m),
            startingBalance: new Money(120m),
            includeLumpyBill: true);

        var assessment = AffordabilityEngine.Assess(inputs);

        Assert.Equal(AffordabilityRating.Unsafe, assessment.Rating);
        Assert.Contains(
            assessment.FailedRules,
            rule => rule.Rule == "Cash flow stays positive");
    }

    [Fact]
    public void ADepositLargerThanTheEmergencyFundIsUnsafe()
    {
        var inputs = Inputs(
            TrueCostEngineTests.FullyAnsweredCar(),
            monthlyNetIncome: new Money(6500m),
            monthlyEssentials: new Money(2200m),
            emergencyFund: new Money(1200m),
            startingBalance: new Money(9000m));

        var assessment = AffordabilityEngine.Assess(inputs);

        Assert.Equal(AffordabilityRating.Unsafe, assessment.Rating);
        Assert.Contains(
            assessment.FailedRules,
            rule => rule.Rule == "Emergency fund survives the up-front cost");

        Assert.Contains(
            assessment.WhatMustChange,
            change => change.Contains("before committing", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WhenIncomeCannotCoverEssentialsPlusTheCarItIsUnsafe()
    {
        var inputs = Inputs(
            TrueCostEngineTests.FullyAnsweredCar(),
            monthlyNetIncome: new Money(2400m),
            monthlyEssentials: new Money(2100m),
            emergencyFund: new Money(20_000m),
            startingBalance: new Money(9000m));

        var assessment = AffordabilityEngine.Assess(inputs);

        Assert.Equal(AffordabilityRating.Unsafe, assessment.Rating);
        Assert.Contains(assessment.FailedRules, rule => rule.Rule == "Essential bills stay covered");
    }

    [Fact]
    public void AnEmergencyFundLeftBelowTargetIsAtBestTight()
    {
        var inputs = Inputs(
            TrueCostEngineTests.FullyAnsweredCar(),
            monthlyNetIncome: new Money(6500m),
            monthlyEssentials: new Money(2200m),
            emergencyFund: new Money(7000m),
            startingBalance: new Money(9000m));

        var assessment = AffordabilityEngine.Assess(inputs);

        // 7,000 minus 3,900 up front leaves 3,100, which is 1.4 months of 2,200 essentials.
        Assert.True(assessment.EmergencyFundMonthsAfterPurchase < 3m);
        Assert.NotEqual(AffordabilityRating.Comfortable, assessment.Rating);
        Assert.Contains(assessment.FailedRules, rule => rule.Rule == "Emergency fund stays at target");
    }

    [Fact]
    public void LosingBreathingRoomIsNotCalledComfortable()
    {
        var inputs = Inputs(
            TrueCostEngineTests.FullyAnsweredCar(),
            monthlyNetIncome: new Money(3400m),
            monthlyEssentials: new Money(2400m),
            emergencyFund: new Money(20_000m),
            startingBalance: new Money(9000m));

        var assessment = AffordabilityEngine.Assess(inputs);

        Assert.NotEqual(AffordabilityRating.Comfortable, assessment.Rating);
        Assert.Contains(assessment.FailedRules, rule => rule.Rule == "Breathing room remains");
    }

    [Fact]
    public void RetirementContributionsBeingSqueezedIsReported()
    {
        var inputs = Inputs(
            TrueCostEngineTests.FullyAnsweredCar(),
            monthlyNetIncome: new Money(3300m),
            monthlyEssentials: new Money(2400m),
            emergencyFund: new Money(20_000m),
            startingBalance: new Money(9000m)) with
        {
            MonthlyRetirementContributions = new Money(300m)
        };

        var assessment = AffordabilityEngine.Assess(inputs);

        var finding = assessment.Findings.Single(rule => rule.Rule == "Retirement contributions continue");

        Assert.False(finding.Passed);
        Assert.Contains("Employer matching may be lost", finding.Explanation);
    }

    [Fact]
    public void ExcessiveDebtToIncomeIsFlagged()
    {
        var inputs = Inputs(
            TrueCostEngineTests.FullyAnsweredCar(),
            monthlyNetIncome: new Money(3000m),
            monthlyEssentials: new Money(1200m),
            emergencyFund: new Money(20_000m),
            startingBalance: new Money(9000m)) with
        {
            ExistingMonthlyDebtPayments = new Money(1200m)
        };

        var assessment = AffordabilityEngine.Assess(inputs);

        var finding = assessment.Findings.Single(rule => rule.Rule == "Debt-to-income stays reasonable");

        Assert.False(finding.Passed);
        Assert.Contains("36%", finding.Explanation);
    }

    [Fact]
    public void VariableIncomeMustBeAssessedOnTheConservativeEstimate()
    {
        var optimistic = Inputs(
            TrueCostEngineTests.FullyAnsweredCar(),
            monthlyNetIncome: new Money(6500m),
            monthlyEssentials: new Money(2200m),
            emergencyFund: new Money(20_000m),
            startingBalance: new Money(9000m),
            assumption: IncomeEstimate.Optimistic) with
        {
            IncomeIsVariable = true
        };

        var assessment = AffordabilityEngine.Assess(optimistic);

        var finding = assessment.Findings.Single(rule => rule.Rule == "Variable income allowed for");

        Assert.False(finding.Passed);
        Assert.Contains("low-hours week", finding.Explanation);
    }

    [Fact]
    public void ConservativeVariableIncomePassesThatCheck()
    {
        var inputs = Inputs(
            TrueCostEngineTests.FullyAnsweredCar(),
            monthlyNetIncome: new Money(6500m),
            monthlyEssentials: new Money(2200m),
            emergencyFund: new Money(20_000m),
            startingBalance: new Money(9000m)) with
        {
            IncomeIsVariable = true
        };

        var assessment = AffordabilityEngine.Assess(inputs);

        Assert.True(assessment.Findings.Single(rule => rule.Rule == "Variable income allowed for").Passed);
    }

    [Fact]
    public void EveryRatingExplainsTheFiguresBehindIt()
    {
        var inputs = Inputs(
            TrueCostEngineTests.FullyAnsweredCar(),
            monthlyNetIncome: new Money(3400m),
            monthlyEssentials: new Money(2400m),
            emergencyFund: new Money(4000m),
            startingBalance: new Money(600m));

        var assessment = AffordabilityEngine.Assess(inputs);

        Assert.NotEmpty(assessment.Findings);
        Assert.All(assessment.Findings, finding => Assert.False(string.IsNullOrWhiteSpace(finding.Explanation)));
        Assert.All(assessment.Findings, finding => Assert.False(string.IsNullOrWhiteSpace(finding.Rule)));
        Assert.False(string.IsNullOrWhiteSpace(assessment.Headline));
    }

    [Fact]
    public void TheHeadlineNamesTheTrueCostNotTheAdvertisedPayment()
    {
        var inputs = Inputs(
            TrueCostEngineTests.FullyAnsweredCar(),
            monthlyNetIncome: new Money(6500m),
            monthlyEssentials: new Money(2200m),
            emergencyFund: new Money(20_000m),
            startingBalance: new Money(9000m));

        var assessment = AffordabilityEngine.Assess(inputs);

        Assert.Contains(assessment.TrueCost.MonthlyTrueCost.ToDisplayString(), assessment.Headline);
        Assert.DoesNotContain("$235.00", assessment.Headline);
    }

    [Fact]
    public void AnOverrideIsRecordedWithoutChangingTheRating()
    {
        var scenario = TrueCostEngineTests.FullyAnsweredCar() with
        {
            OverrideReason = "We accept the risk; the household needs the car for work."
        };

        var inputs = Inputs(
            scenario,
            monthlyNetIncome: new Money(2400m),
            monthlyEssentials: new Money(2100m),
            emergencyFund: new Money(500m),
            startingBalance: new Money(200m));

        var assessment = AffordabilityEngine.Assess(inputs);

        // The program states its verdict plainly; the household decides.
        Assert.True(scenario.HasOverride);
        Assert.Equal(AffordabilityRating.Unsafe, assessment.Rating);
    }

    [Fact]
    public void RatingsAreOrderedFromComfortableToUnsafe()
    {
        Assert.True(AffordabilityRating.Comfortable < AffordabilityRating.Manageable);
        Assert.True(AffordabilityRating.Manageable < AffordabilityRating.Tight);
        Assert.True(AffordabilityRating.Tight < AffordabilityRating.Unsafe);
    }

    private static AffordabilityInputs Inputs(
        Scenario scenario,
        Money? monthlyNetIncome = null,
        Money? monthlyEssentials = null,
        Money? emergencyFund = null,
        Money? startingBalance = null,
        IncomeEstimate assumption = IncomeEstimate.Conservative,
        bool includeLumpyBill = false)
    {
        var income = monthlyNetIncome ?? new Money(4000m);
        var essentials = monthlyEssentials ?? new Money(2000m);
        var balance = startingBalance ?? new Money(3000m);

        var expenses = new List<ExpenseItem>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Essentials",
                Category = ExpenseCategory.Housing,
                ExpectedAmount = essentials,
                Frequency = Frequency.Monthly,
                AnchorDueDate = new DateOnly(2026, 3, 1)
            },
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Car costs",
                Category = ExpenseCategory.Transport,
                ExpectedAmount = TrueCostEngine.Calculate(scenario, ScenarioCase.Expected).MonthlyCashCost,
                Frequency = Frequency.Monthly,
                AnchorDueDate = new DateOnly(2026, 3, 5)
            }
        };

        if (includeLumpyBill)
        {
            // A large annual bill landing days before payday is exactly the case a monthly
            // average cannot see.
            expenses.Add(new ExpenseItem
            {
                Id = Guid.NewGuid(),
                Name = "Annual insurance",
                Category = ExpenseCategory.Insurance,
                ExpectedAmount = new Money(1900m),
                Frequency = Frequency.Annual,
                AnchorDueDate = new DateOnly(2026, 3, 10)
            });
        }

        var incomeSource = new SalaryIncome
        {
            Id = Guid.NewGuid(),
            Name = "Wages",
            MemberId = Guid.NewGuid(),
            PayFrequency = Frequency.Monthly,
            AnchorPayDate = new DateOnly(2026, 3, 28),
            AnnualSalary = income * 12m
        };

        var projection = CashFlowProjector.Project(new CashFlowInputs
        {
            From = Start,
            To = Start.AddMonths(6),
            StartingBalance = balance,
            IncomeSources = [incomeSource],
            Expenses = expenses,
            Assumption = assumption
        });

        return new AffordabilityInputs
        {
            Scenario = scenario,
            Case = ScenarioCase.Expected,
            ProjectionWithScenario = projection,
            EmergencyFundBalance = emergencyFund ?? new Money(6000m),
            MonthlyEssentialSpending = essentials,
            MonthlyNetIncome = income,
            MinimumBalanceReserve = new Money(200m),
            MinimumBreathingRoom = new Money(250m),
            EmergencyFundTargetMonths = 3m
        };
    }
}
