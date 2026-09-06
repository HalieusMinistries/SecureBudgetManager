using SecureBudgetManager.Core.Income;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Tax;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.Tests.Tax;

public sealed class TaxRuleVersionTests
{
    [Fact]
    public void UtahStatutoryRatesAreYearSpecific()
    {
        Assert.Equal(0.0450m, UtahWithholdingLibrary.StatutoryRate(2025));
        Assert.Equal(0.0445m, UtahWithholdingLibrary.StatutoryRate(2026));
        Assert.Equal(0m, UtahWithholdingLibrary.StatutoryRate(2027));
    }

    [Fact]
    public void PayDateSelectsTheRevised2026WithholdingSchedule()
    {
        Assert.True(UtahWithholdingLibrary.TryGet(new DateOnly(2026, 6, 1), Frequency.Weekly, out var june, out _));
        Assert.Equal("UT-WH-2026-06", june!.Version);
        Assert.Equal(0.0445m, june.Rate);
        Assert.Equal(new DateOnly(2026, 6, 1), june.EffectiveFrom);

        Assert.True(UtahWithholdingLibrary.TryGet(new DateOnly(2026, 5, 31), Frequency.Weekly, out var may, out _));
        Assert.Equal("UT-WH-2025-01", may!.Version);
        Assert.Equal(0.0450m, may.Rate);
    }

    [Fact]
    public void TwentyTwentySixPayrollDoesNotUseFourPointFiveFive()
    {
        var result = PayrollCalculator.Calculate(new PayrollRequest
        {
            GrossPerPeriod = new Money(400m),
            PayFrequency = Frequency.Weekly,
            TaxYear = TaxYearLibrary.Default2026,
            UsesUtahWithholding = true,
            PayrollPeriodStart = new DateOnly(2026, 7, 3)
        });

        Assert.NotEqual(new Money(18.20m), result.StateWithholding);
        Assert.Equal("UT-WH-2026-06", result.StateRuleVersion);
        Assert.DoesNotContain("4.55", result.StateRuleSource ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains(result.Warnings, warning => warning.Contains("4.45", StringComparison.Ordinal) || warning.Contains("Publication 14", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Publication14WeeklySingleExampleUsesTheFormulaNotAHeadlineRate()
    {
        var schedule = UtahWithholdingLibrary.Weekly2026Revised;
        var calculated = UtahWithholdingLibrary.Calculate(new Money(400m), UtahWithholdingStatus.Single, schedule);

        Assert.Equal(0.0445m, schedule.Rate);
        Assert.Equal(new Money(17.80m), calculated.GrossTax);
        Assert.True(calculated.Withholding < new Money(400m * 0.0445m));
        Assert.Contains("Publication 14", calculated.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void UnsupportedFutureYearsDoNotFallBackSilently()
    {
        Assert.False(TaxYearLibrary.TryGetYear(2027, out var table, out var warning));
        Assert.Null(table);
        Assert.Equal(TaxYearLibrary.Unavailable, warning);
        Assert.Throws<InvalidOperationException>(() => TaxYearLibrary.ForYear(2027));
    }

    [Fact]
    public void FederalAndSocialSecurityTablesAreSelectedByTaxYear()
    {
        Assert.True(TaxYearLibrary.TryGetYear(2025, out var twentyFive, out _));
        Assert.True(TaxYearLibrary.TryGetYear(2026, out var twentySix, out _));
        Assert.Equal(new Money(176_100m), twentyFive!.SocialSecurityWageBase);
        Assert.Equal(new Money(184_500m), twentySix!.SocialSecurityWageBase);
        Assert.Equal(new Money(15_000m), twentyFive.NonResidentAlienWageAddition);
        Assert.Equal(new Money(16_100m), twentySix.NonResidentAlienWageAddition);
        Assert.Equal(new Money(15_000m), twentyFive.StandardDeduction[FilingStatus.Single]);
        Assert.Equal(new Money(16_100m), twentySix.StandardDeduction[FilingStatus.Single]);
    }

    [Fact]
    public void HistoricalPayslipKeepsRecordedTaxDollars()
    {
        var payslip = new Payslip
        {
            Id = Guid.NewGuid(),
            IncomeSourceId = Guid.NewGuid(),
            PayDate = new DateOnly(2025, 12, 19),
            GrossPay = new Money(800m),
            FederalWithholding = new Money(70m),
            StateWithholding = new Money(36m),
            TaxYear = 2025,
            RuleSetVersion = "historical-2025"
        };

        var laterEstimate = PayrollCalculator.Calculate(new PayrollRequest
        {
            GrossPerPeriod = new Money(800m),
            PayFrequency = Frequency.Weekly,
            TaxYear = TaxYearLibrary.Default2026,
            UsesUtahWithholding = true,
            PayrollPeriodStart = new DateOnly(2026, 7, 3)
        });

        Assert.Equal(new Money(70m), payslip.FederalWithholding);
        Assert.Equal(2025, payslip.TaxYear);
        Assert.Equal("historical-2025", payslip.RuleSetVersion);
        Assert.NotEqual(payslip.StateWithholding, laterEstimate.StateWithholding);
    }

    [Fact]
    public void UncertainTaxResidencyIsPreservedOnTheEstimate()
    {
        var result = PayrollCalculator.Calculate(new PayrollRequest
        {
            GrossPerPeriod = new Money(500m),
            PayFrequency = Frequency.Weekly,
            TaxYear = TaxYearLibrary.Default2026,
            TaxResidency = UsTaxResidency.NotYetDetermined
        });

        Assert.Contains(result.Warnings, warning => warning.Contains("not yet determined", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RecordedFourPointFiveFiveRateIsWarnedAndNotTreatedAsUtah2026()
    {
        var result = PayrollCalculator.Calculate(new PayrollRequest
        {
            GrossPerPeriod = new Money(400m),
            PayFrequency = Frequency.Weekly,
            TaxYear = TaxYearLibrary.Default2026,
            StateFlatRate = 0.0455m,
            UsesUtahWithholding = false,
            PayrollPeriodStart = new DateOnly(2026, 7, 3)
        });

        Assert.Contains(result.Warnings, warning => warning.Contains("4.55%", StringComparison.Ordinal));
        Assert.Equal((new Money(400m) * 0.0455m).Round(), result.StateWithholding);
    }
}
