using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Tax;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.Tests.Tax;

/// <summary>
/// Withholding estimates for budgeting. The tests assert behaviour and relationships rather than
/// exact IRS figures, because an employer's own wage-bracket tables will never match to the cent.
/// What must be right is the direction and the treatment of each deduction type.
/// </summary>
public sealed class PayrollCalculatorTests
{
    private static TaxYearTable Table => TaxYearLibrary.Default2025;

    [Fact]
    public void EveryEstimateCarriesTheNotTaxAdviceWarning()
    {
        var result = PayrollCalculator.Calculate(Request(new Money(1000m)));

        Assert.Contains(TaxYearLibrary.NotTaxAdviceWarning, result.Warnings);
    }

    [Fact]
    public void AnUnverifiedTaxTableCarriesItsOwnWarning()
    {
        var result = PayrollCalculator.Calculate(Request(new Money(1000m)));

        if (!Table.IsVerified)
        {
            Assert.Contains(TaxYearLibrary.UnverifiedTableWarning, result.Warnings);
        }
    }

    [Fact]
    public void SocialSecurityAndMedicareUseTheStatutoryRates()
    {
        var result = PayrollCalculator.Calculate(Request(new Money(1000m)));

        Assert.Equal(new Money(62m), result.SocialSecurity);
        Assert.Equal(new Money(14.5m), result.Medicare);
    }

    [Fact]
    public void SocialSecurityStopsAtTheWageBase()
    {
        var atTheCap = PayrollCalculator.Calculate(Request(new Money(5000m)) with
        {
            YearToDateTaxableWages = Table.SocialSecurityWageBase
        });

        Assert.Equal(Money.Zero, atTheCap.SocialSecurity);

        // Medicare has no cap, so it continues.
        Assert.True(atTheCap.Medicare > Money.Zero);
    }

    [Fact]
    public void ASection125HealthPremiumReducesBothIncomeTaxAndFicaWages()
    {
        var without = PayrollCalculator.Calculate(Request(new Money(1000m)));

        var with = PayrollCalculator.Calculate(Request(new Money(1000m)) with
        {
            Deductions =
            [
                new PayrollDeduction
                {
                    Name = "Medical",
                    AmountPerPeriod = new Money(100m),
                    TaxTreatment = DeductionTaxTreatment.PreTaxIncludingFica
                }
            ]
        });

        Assert.True(with.SocialSecurity < without.SocialSecurity);
        Assert.True(with.Medicare < without.Medicare);
        Assert.True(with.FederalWithholding <= without.FederalWithholding);
    }

    [Fact]
    public void ATraditionalRetirementContributionReducesIncomeTaxButNotFica()
    {
        var without = PayrollCalculator.Calculate(Request(new Money(2000m)));

        var with = PayrollCalculator.Calculate(Request(new Money(2000m)) with
        {
            Retirement = new RetirementPlan { EmployeeContributionPercent = 10m }
        });

        // This is the distinction most budgeting tools get wrong.
        Assert.Equal(without.SocialSecurity, with.SocialSecurity);
        Assert.Equal(without.Medicare, with.Medicare);
        Assert.True(with.FederalWithholding < without.FederalWithholding);
    }

    [Fact]
    public void ARothContributionReducesNeitherIncomeTaxNorFica()
    {
        var traditional = PayrollCalculator.Calculate(Request(new Money(2000m)) with
        {
            Retirement = new RetirementPlan { EmployeeContributionPercent = 10m }
        });

        var roth = PayrollCalculator.Calculate(Request(new Money(2000m)) with
        {
            Retirement = new RetirementPlan { EmployeeContributionPercent = 10m, IsRoth = true }
        });

        Assert.True(roth.FederalWithholding > traditional.FederalWithholding);
        Assert.Equal(traditional.RetirementContribution, roth.RetirementContribution);
    }

    [Fact]
    public void APostTaxDeductionOnlyReducesNetPay()
    {
        var without = PayrollCalculator.Calculate(Request(new Money(1000m)));

        var with = PayrollCalculator.Calculate(Request(new Money(1000m)) with
        {
            Deductions =
            [
                new PayrollDeduction
                {
                    Name = "Legal plan",
                    AmountPerPeriod = new Money(20m),
                    TaxTreatment = DeductionTaxTreatment.PostTax
                }
            ]
        });

        Assert.Equal(without.TotalTax, with.TotalTax);
        Assert.Equal((without.NetPay - new Money(20m)).Round(), with.NetPay);
    }

    [Fact]
    public void TheEmployerMatchIsCappedByItsLimitAndTheEmployeeContribution()
    {
        var plan = new RetirementPlan
        {
            EmployeeContributionPercent = 10m,
            EmployerMatchPercent = 3m,
            EmployerMatchLimitPercent = 6m
        };

        // Contributing 10% cannot earn more than the 3% the employer offers.
        Assert.Equal(new Money(30m), plan.EmployerContribution(new Money(1000m)));

        var underContributing = plan with { EmployeeContributionPercent = 2m };
        Assert.Equal(new Money(20m), underContributing.EmployerContribution(new Money(1000m)));
    }

    [Fact]
    public void VestingFractionRisesWithServiceAndCapsAtOne()
    {
        var plan = new RetirementPlan { VestingYears = 4 };

        Assert.Equal(0m, plan.VestedFraction(0m));
        Assert.Equal(0.5m, plan.VestedFraction(2m));
        Assert.Equal(1m, plan.VestedFraction(4m));
        Assert.Equal(1m, plan.VestedFraction(10m));
    }

    [Fact]
    public void ImmediateVestingReportsFullOwnership()
    {
        Assert.Equal(1m, new RetirementPlan { VestingYears = 0 }.VestedFraction(0m));
    }

    [Fact]
    public void ANonResidentAlienGetsNoStandardDeductionAndAClearWarning()
    {
        var resident = PayrollCalculator.Calculate(Request(new Money(1500m)));

        var nonResident = PayrollCalculator.Calculate(Request(new Money(1500m)) with
        {
            W4 = new W4Settings { IsNonResidentAlien = true }
        });

        Assert.True(nonResident.FederalWithholding > resident.FederalWithholding);
        Assert.Contains(
            nonResident.Warnings,
            warning => warning.Contains("Notice 1392", StringComparison.Ordinal));
    }

    [Fact]
    public void ClaimingDependantsAsANonResidentAlienIsQuestioned()
    {
        var result = PayrollCalculator.Calculate(Request(new Money(1500m)) with
        {
            W4 = new W4Settings { IsNonResidentAlien = true, QualifyingChildren = 2 }
        });

        Assert.Contains(
            result.Warnings,
            warning => warning.Contains("often not eligible", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DependantCreditsReduceWithholding()
    {
        var without = PayrollCalculator.Calculate(Request(new Money(2000m)));

        var with = PayrollCalculator.Calculate(Request(new Money(2000m)) with
        {
            W4 = new W4Settings { QualifyingChildren = 2 }
        });

        Assert.True(with.FederalWithholding < without.FederalWithholding);
    }

    [Fact]
    public void ExtraWithholdingIsAddedExactly()
    {
        var without = PayrollCalculator.Calculate(Request(new Money(1500m)));

        var with = PayrollCalculator.Calculate(Request(new Money(1500m)) with
        {
            W4 = new W4Settings { ExtraWithholdingPerPeriod = new Money(50m) }
        });

        Assert.Equal((without.FederalWithholding + new Money(50m)).Round(), with.FederalWithholding);
    }

    [Fact]
    public void WithholdingIsNeverNegativeWhenCreditsExceedTheTax()
    {
        var result = PayrollCalculator.Calculate(Request(new Money(300m)) with
        {
            W4 = new W4Settings { QualifyingChildren = 6 }
        });

        Assert.False(result.FederalWithholding.IsNegative);
    }

    [Fact]
    public void MarriedFilingJointlyWithholdsLessThanSingleAtTheSameWage()
    {
        var single = PayrollCalculator.Calculate(Request(new Money(2000m)));

        var married = PayrollCalculator.Calculate(Request(new Money(2000m)) with
        {
            W4 = new W4Settings { FilingStatus = FilingStatus.MarriedFilingJointly }
        });

        Assert.True(married.FederalWithholding < single.FederalWithholding);
    }

    [Fact]
    public void PreTaxDeductionsExceedingGrossPayAreFlooredWithAWarning()
    {
        var result = PayrollCalculator.Calculate(Request(new Money(200m)) with
        {
            Deductions =
            [
                new PayrollDeduction
                {
                    Name = "Medical",
                    AmountPerPeriod = new Money(400m),
                    TaxTreatment = DeductionTaxTreatment.PreTaxIncludingFica
                }
            ]
        });

        Assert.Equal(Money.Zero, result.FederalWithholding);
        Assert.Contains(
            result.Warnings,
            warning => warning.Contains("exceed gross pay", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ReimbursementsAreAddedToNetPayWithoutBeingTaxed()
    {
        var without = PayrollCalculator.Calculate(Request(new Money(1000m)));

        var with = PayrollCalculator.Calculate(Request(new Money(1000m)) with
        {
            NonTaxableReimbursementsPerPeriod = new Money(147.4m)
        });

        Assert.Equal(without.TotalTax, with.TotalTax);
        Assert.Equal((without.NetPay + new Money(147.4m)).Round(), with.NetPay);
    }

    [Fact]
    public void TheStateRateIsAppliedToIncomeTaxWages()
    {
        var result = PayrollCalculator.Calculate(Request(new Money(1000m)) with
        {
            StateFlatRate = 0.0307m
        });

        Assert.Equal(new Money(30.7m), result.StateWithholding);
    }

    [Fact]
    public void AStateRateGivenAsAPercentageRatherThanAFractionIsRefused()
    {
        // 3.07 would be a 307% tax. Refusing it prevents a catastrophic silent error.
        Assert.Throws<ArgumentException>(() => PayrollCalculator.Calculate(
            Request(new Money(1000m)) with { StateFlatRate = 3.07m }));
    }

    [Fact]
    public void TheAnnualProjectionScalesThePeriodConsistently()
    {
        var request = Request(new Money(1000m));
        var period = PayrollCalculator.Calculate(request);
        var projection = PayrollCalculator.Project(request);

        Assert.Equal((period.GrossPay * 52m).Round(), projection.GrossIncome);
        Assert.Equal((period.NetPay * 52m).Round(), projection.NetIncome);
        Assert.Equal(Table.Year, projection.Year);
        Assert.Equal(projection.Warnings, period.Warnings);
    }

    [Fact]
    public void TheEffectiveTaxRateIsReportedAsAPercentage()
    {
        var result = PayrollCalculator.Calculate(Request(new Money(1000m)));

        Assert.True(result.EffectiveTaxRate > 0m);
        Assert.True(result.EffectiveTaxRate < 100m);
    }

    [Fact]
    public void AOneOffPayFrequencyIsRefused()
    {
        Assert.Throws<ArgumentException>(() => PayrollCalculator.Calculate(
            Request(new Money(1000m)) with { PayFrequency = Frequency.OneOff }));
    }

    [Fact]
    public void ANegativeDeductionIsRefused()
    {
        Assert.Throws<ArgumentException>(() => PayrollCalculator.Calculate(
            Request(new Money(1000m)) with
            {
                Deductions =
                [
                    new PayrollDeduction
                    {
                        Name = "Broken",
                        AmountPerPeriod = new Money(-10m),
                        TaxTreatment = DeductionTaxTreatment.PostTax
                    }
                ]
            }));
    }

    [Fact]
    public void HigherPayNeverProducesLowerWithholding()
    {
        Money? previous = null;

        for (var gross = 500m; gross <= 6000m; gross += 250m)
        {
            var withholding = PayrollCalculator.Calculate(Request(new Money(gross))).FederalWithholding;

            if (previous is { } earlier)
            {
                Assert.True(
                    withholding >= earlier,
                    $"Withholding fell from {earlier} to {withholding} as pay rose to {gross}.");
            }

            previous = withholding;
        }
    }

    [Fact]
    public void APayslipCanBeComparedAgainstTheEstimate()
    {
        var estimate = PayrollCalculator.Calculate(Request(new Money(690m)));

        var payslip = new Core.Income.Payslip
        {
            Id = Guid.NewGuid(),
            IncomeSourceId = Guid.NewGuid(),
            PayDate = new DateOnly(2026, 3, 6),
            GrossPay = new Money(690m),
            FederalWithholding = estimate.FederalWithholding + new Money(20m),
            SocialSecurity = estimate.SocialSecurity,
            Medicare = estimate.Medicare,
            HoursWorked = 37.5m
        };

        var comparison = Core.Income.IncomeReconciler.Compare(estimate.NetPay, payslip);

        Assert.Equal(new Money(-20m), comparison.Difference);
        Assert.Contains("lower than estimated", comparison.Explanation);
    }

    private static PayrollRequest Request(Money gross) => new()
    {
        GrossPerPeriod = gross,
        PayFrequency = Frequency.Weekly,
        TaxYear = Table
    };
}
