using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.Core.Tax;

/// <summary>
/// One member's tax and payroll settings, kept together so a payroll estimate can be rebuilt from
/// stored data without re-entering the W-4 each time.
/// </summary>
public sealed record PayrollProfile
{
    public required Guid MemberId { get; init; }

    /// <summary>Which versioned tax-year table applies. Rates are never hard-coded permanently.</summary>
    public int TaxYear { get; init; } = 2025;

    public W4Settings W4 { get; init; } = new();

    public RetirementPlan Retirement { get; init; } = new();

    public IReadOnlyList<PayrollDeduction> Deductions { get; init; } = [];

    /// <summary>Optional non-Utah headline rate. Utah withholding uses Publication 14 instead.</summary>
    public decimal StateFlatRate { get; init; }

    public bool UsesUtahWithholding { get; init; }

    public UsTaxResidency TaxResidency { get; init; } = UsTaxResidency.NotYetDetermined;

    /// <summary>Rule-set version stored with the profile when the estimate was last built.</summary>
    public string? WithholdingRuleVersion { get; init; }

    public decimal YearsOfService { get; init; }

    /// <summary>False when W-4 or tax-residency details still need confirmation.</summary>
    public bool W4IsComplete { get; init; } = true;

    public string? Notes { get; init; }

    public Money VestedEmployerBalance(Money employerBalance) =>
        employerBalance * Retirement.VestedFraction(YearsOfService);

    public PayrollRequest ToRequest(
        Money grossPerPeriod,
        Frequency payFrequency,
        TaxYearTable table,
        Money nonTaxableReimbursements,
        Money yearToDateTaxableWages) => new()
    {
        GrossPerPeriod = grossPerPeriod,
        PayFrequency = payFrequency,
        TaxYear = table,
        W4 = W4,
        Retirement = Retirement,
        Deductions = Deductions,
        StateFlatRate = StateFlatRate,
        UsesUtahWithholding = UsesUtahWithholding,
        TaxResidency = TaxResidency,
        NonTaxableReimbursementsPerPeriod = nonTaxableReimbursements,
        YearToDateTaxableWages = yearToDateTaxableWages
    };

    public PayrollRequest ToRequest(
        Money grossPerPeriod,
        Frequency payFrequency,
        TaxYearTable table,
        Money nonTaxableReimbursements,
        Money yearToDateTaxableWages,
        DateOnly payrollPeriodStart) =>
        ToRequest(grossPerPeriod, payFrequency, table, nonTaxableReimbursements, yearToDateTaxableWages) with
        {
            PayrollPeriodStart = payrollPeriodStart
        };

    public void Validate()
    {
        if (MemberId == Guid.Empty)
        {
            throw new ArgumentException("A payroll profile must belong to a household member.");
        }

        W4.Validate();
        Retirement.Validate();

        if (StateFlatRate is < 0m or > 1m)
        {
            throw new ArgumentException("The state rate must be a fraction between 0 and 1.");
        }

        if (YearsOfService < 0m)
        {
            throw new ArgumentException("Years of service cannot be negative.");
        }

        foreach (var deduction in Deductions)
        {
            deduction.Validate();
        }
    }
}
