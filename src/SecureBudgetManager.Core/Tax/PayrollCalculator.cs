using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.Core.Tax;

/// <summary>
/// How a deduction affects taxable wages. The distinction matters: a 401(k) contribution reduces
/// income tax but not Social Security or Medicare, whereas a Section 125 health premium reduces both.
/// </summary>
public enum DeductionTaxTreatment
{
    /// <summary>Taken after all tax. Reduces net pay only.</summary>
    PostTax = 0,

    /// <summary>Reduces income-tax wages only, not FICA. Traditional 401(k) behaves this way.</summary>
    PreTaxIncomeTaxOnly = 1,

    /// <summary>Reduces income-tax and FICA wages. Cafeteria-plan health premiums behave this way.</summary>
    PreTaxIncludingFica = 2
}

public sealed record PayrollDeduction
{
    public required string Name { get; init; }

    public required Money AmountPerPeriod { get; init; }

    public required DeductionTaxTreatment TaxTreatment { get; init; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new ArgumentException("A deduction needs a name.");
        }

        if (AmountPerPeriod.IsNegative)
        {
            throw new ArgumentException("A deduction cannot be negative.");
        }
    }
}

/// <summary>W-4 entries that drive the federal withholding estimate.</summary>
public sealed record W4Settings
{
    public FilingStatus FilingStatus { get; init; } = FilingStatus.Single;

    public int QualifyingChildren { get; init; }

    public int OtherDependants { get; init; }

    /// <summary>Step 4(a): other income not from jobs.</summary>
    public Money OtherAnnualIncome { get; init; } = Money.Zero;

    /// <summary>Step 4(b): deductions beyond the standard deduction.</summary>
    public Money AnnualDeductions { get; init; } = Money.Zero;

    /// <summary>Step 4(c): extra withholding per pay period.</summary>
    public Money ExtraWithholdingPerPeriod { get; init; } = Money.Zero;

    /// <summary>Step 2: two jobs or a working spouse.</summary>
    public bool MultipleJobsChecked { get; init; }

    /// <summary>
    /// A non-resident alien generally cannot claim the standard deduction, and an amount is added
    /// to wages for withholding. See IRS Notice 1392.
    /// </summary>
    public bool IsNonResidentAlien { get; init; }

    public void Validate()
    {
        if (QualifyingChildren < 0 || OtherDependants < 0)
        {
            throw new ArgumentException("Dependant counts cannot be negative.");
        }

        if (OtherAnnualIncome.IsNegative || AnnualDeductions.IsNegative || ExtraWithholdingPerPeriod.IsNegative)
        {
            throw new ArgumentException("W-4 amounts cannot be negative.");
        }
    }
}

/// <summary>Retirement contributions and the employer match.</summary>
public sealed record RetirementPlan
{
    /// <summary>Employee contribution as a percentage of gross pay.</summary>
    public decimal EmployeeContributionPercent { get; init; }

    /// <summary>Employer match as a percentage of gross pay, up to <see cref="EmployerMatchLimitPercent"/>.</summary>
    public decimal EmployerMatchPercent { get; init; }

    /// <summary>Maximum percentage of gross pay the employer will match.</summary>
    public decimal EmployerMatchLimitPercent { get; init; }

    /// <summary>Years of service before the employer match is fully owned.</summary>
    public int VestingYears { get; init; }

    public bool IsRoth { get; init; }

    public Money EmployeeContribution(Money grossPerPeriod) =>
        grossPerPeriod * (EmployeeContributionPercent / 100m);

    public Money EmployerContribution(Money grossPerPeriod)
    {
        var matchedPercent = Math.Min(EmployeeContributionPercent, EmployerMatchLimitPercent);
        var effectivePercent = Math.Min(matchedPercent, EmployerMatchPercent);
        return grossPerPeriod * (effectivePercent / 100m);
    }

    public decimal VestedFraction(decimal yearsOfService)
    {
        if (VestingYears <= 0)
        {
            return 1m;
        }

        return Math.Clamp(yearsOfService / VestingYears, 0m, 1m);
    }

    public void Validate()
    {
        if (EmployeeContributionPercent is < 0m or > 100m)
        {
            throw new ArgumentException("The employee contribution must be between 0 and 100 percent.");
        }

        if (EmployerMatchPercent < 0m || EmployerMatchLimitPercent < 0m)
        {
            throw new ArgumentException("Employer match figures cannot be negative.");
        }

        if (VestingYears < 0)
        {
            throw new ArgumentException("Vesting years cannot be negative.");
        }
    }
}

public sealed record PayrollResult
{
    public required Money GrossPay { get; init; }

    public required Money PreTaxDeductions { get; init; }

    public required Money RetirementContribution { get; init; }

    public required Money EmployerRetirementContribution { get; init; }

    public required Money FederalWithholding { get; init; }

    public required Money StateWithholding { get; init; }

    public required Money SocialSecurity { get; init; }

    public required Money Medicare { get; init; }

    public required Money PostTaxDeductions { get; init; }

    public required Money NonTaxableReimbursements { get; init; }

    public required IReadOnlyList<string> Warnings { get; init; }

    public string? StateRuleVersion { get; init; }

    public DateOnly? StateRuleEffectiveDate { get; init; }

    public string? StateRuleSource { get; init; }

    public string? StateRuleReviewStatus { get; init; }

    public Money TotalTax => FederalWithholding + StateWithholding + SocialSecurity + Medicare;

    public Money NetPay =>
        (GrossPay - PreTaxDeductions - RetirementContribution - TotalTax - PostTaxDeductions
         + NonTaxableReimbursements).Round();

    public decimal EffectiveTaxRate =>
        GrossPay.IsZero ? 0m : Math.Round(TotalTax.Amount / GrossPay.Amount * 100m, 2);
}

public sealed record PayrollRequest
{
    public required Money GrossPerPeriod { get; init; }

    public required Frequency PayFrequency { get; init; }

    public required TaxYearTable TaxYear { get; init; }

    public W4Settings W4 { get; init; } = new();

    public RetirementPlan Retirement { get; init; } = new();

    public IReadOnlyList<PayrollDeduction> Deductions { get; init; } = [];

    /// <summary>Optional override. Utah withholding does not use this headline rate.</summary>
    public decimal StateFlatRate { get; init; }

    public bool UsesUtahWithholding { get; init; }

    public DateOnly? PayrollPeriodStart { get; init; }

    public UsTaxResidency TaxResidency { get; init; } = UsTaxResidency.NotYetDetermined;

    public Money NonTaxableReimbursementsPerPeriod { get; init; } = Money.Zero;

    /// <summary>Year-to-date taxable wages, so the Social Security wage base can be respected.</summary>
    public Money YearToDateTaxableWages { get; init; } = Money.Zero;
}

/// <summary>
/// Estimates per-period withholding using the IRS annualised percentage method.
///
/// This is an estimate for budgeting. Real payslips differ because employers use published
/// wage-bracket tables, mid-year changes and rounding rules this cannot reproduce.
/// </summary>
public static class PayrollCalculator
{
    public static PayrollResult Calculate(PayrollRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.PayFrequency.IsRecurring())
        {
            throw new ArgumentException("Payroll needs a recurring pay frequency.", nameof(request));
        }

        request.W4.Validate();
        request.Retirement.Validate();
        request.TaxYear.Validate();

        foreach (var deduction in request.Deductions)
        {
            deduction.Validate();
        }

        if (request.StateFlatRate is < 0m or > 1m)
        {
            throw new ArgumentException("The state rate must be expressed as a fraction between 0 and 1.");
        }

        var warnings = new List<string> { TaxYearLibrary.NotTaxAdviceWarning };
        if (!request.TaxYear.IsVerified)
        {
            warnings.Add(TaxYearLibrary.UnverifiedTableWarning);
        }

        var periodsPerYear = request.PayFrequency.PaymentsPerYear();
        var gross = request.GrossPerPeriod;

        var retirement = request.Retirement.EmployeeContribution(gross);
        var employerRetirement = request.Retirement.EmployerContribution(gross);

        var preTaxFicaExempt = Money.Sum(request.Deductions
            .Where(deduction => deduction.TaxTreatment == DeductionTaxTreatment.PreTaxIncludingFica)
            .Select(deduction => deduction.AmountPerPeriod));

        var preTaxIncomeOnly = Money.Sum(request.Deductions
            .Where(deduction => deduction.TaxTreatment == DeductionTaxTreatment.PreTaxIncomeTaxOnly)
            .Select(deduction => deduction.AmountPerPeriod));

        var postTax = Money.Sum(request.Deductions
            .Where(deduction => deduction.TaxTreatment == DeductionTaxTreatment.PostTax)
            .Select(deduction => deduction.AmountPerPeriod));

        // A traditional 401(k) reduces income-tax wages but not FICA wages. A Roth reduces neither.
        var retirementReducesIncomeTax = request.Retirement.IsRoth ? Money.Zero : retirement;

        var incomeTaxWages = gross - preTaxFicaExempt - preTaxIncomeOnly - retirementReducesIncomeTax;
        var ficaWages = gross - preTaxFicaExempt;

        if (incomeTaxWages.IsNegative)
        {
            incomeTaxWages = Money.Zero;
            warnings.Add("Pre-tax deductions exceed gross pay, so the withholding estimate was floored at zero.");
        }

        if (request.TaxResidency.IsUncertain())
        {
            warnings.Add(
                $"United States tax residency is {request.TaxResidency.ToDisplayName()}. " +
                "Federal withholding, treaty claims and some credits are affected. The estimate is labelled as such.");
        }

        var federal = EstimateFederalWithholding(incomeTaxWages, periodsPerYear, request, warnings);
        var (state, stateMeta) = EstimateStateWithholding(incomeTaxWages, request, warnings);
        var (socialSecurity, medicare) = CalculateFica(ficaWages, request);

        return new PayrollResult
        {
            GrossPay = gross.Round(),
            PreTaxDeductions = (preTaxFicaExempt + preTaxIncomeOnly).Round(),
            RetirementContribution = retirement.Round(),
            EmployerRetirementContribution = employerRetirement.Round(),
            FederalWithholding = federal,
            StateWithholding = state,
            SocialSecurity = socialSecurity,
            Medicare = medicare,
            PostTaxDeductions = postTax.Round(),
            NonTaxableReimbursements = request.NonTaxableReimbursementsPerPeriod.Round(),
            Warnings = warnings,
            StateRuleVersion = stateMeta.Version,
            StateRuleEffectiveDate = stateMeta.Effective,
            StateRuleSource = stateMeta.Source,
            StateRuleReviewStatus = stateMeta.Review
        };
    }

    private static (Money Amount, (string? Version, DateOnly? Effective, string? Source, string? Review) Meta)
        EstimateStateWithholding(Money incomeTaxWages, PayrollRequest request, List<string> warnings)
    {
        if (!request.UsesUtahWithholding)
        {
            if (request.StateFlatRate is 0.0455m)
            {
                warnings.Add(
                    "A 4.55% state rate is recorded. That rate is not the 2025 (4.50%) or 2026 (4.45%) " +
                    "Utah statutory rate and is not used for Utah Publication 14 withholding.");
            }

            return ((incomeTaxWages * request.StateFlatRate).Round(), (null, null, null, null));
        }

        var payDate = request.PayrollPeriodStart ?? DateOnly.FromDateTime(DateTime.Today);

        if (!UtahWithholdingLibrary.TryGet(payDate, request.PayFrequency, out var schedule, out var warning)
            || schedule is null)
        {
            warnings.Add(warning);
            return (Money.Zero, (null, null, null, UtahWithholdingLibrary.Unavailable));
        }

        if (schedule.Rate == 0.0455m)
        {
            throw new InvalidOperationException("A 4.55% Utah withholding schedule must not be applied.");
        }

        var result = UtahWithholdingLibrary.Calculate(
            incomeTaxWages,
            UtahWithholdingLibrary.StatusFrom(request.W4.FilingStatus),
            schedule);

        warnings.Add(warning);
        warnings.Add(
            $"Estimated Utah withholding {result.Withholding.ToDisplayString()} using {schedule.Version}, " +
            $"tax year {schedule.TaxYear}, effective {schedule.EffectiveFrom:yyyy-MM-dd}, " +
            $"{schedule.OfficialSource}. Review by {schedule.ReviewDate:yyyy-MM-dd}.");

        return (result.Withholding, (
            schedule.Version,
            schedule.EffectiveFrom,
            schedule.OfficialSource,
            payDate > schedule.ReviewDate ? "Review required" : "Within review date"));
    }

    private static Money EstimateFederalWithholding(
        Money incomeTaxWagesPerPeriod,
        decimal periodsPerYear,
        PayrollRequest request,
        List<string> warnings)
    {
        var table = request.TaxYear;
        var w4 = request.W4;

        var annualWages = incomeTaxWagesPerPeriod * periodsPerYear;
        annualWages += w4.OtherAnnualIncome;

        Money annualTaxable;

        if (w4.IsNonResidentAlien)
        {
            // An NRA cannot take the standard deduction; an amount is added to wages instead.
            annualTaxable = annualWages + table.NonResidentAlienWageAddition - w4.AnnualDeductions;
            warnings.Add(
                "Non-resident alien withholding is applied: the standard deduction is not used and " +
                "an additional amount is added to wages. Check IRS Notice 1392 for your situation.");
        }
        else
        {
            var deduction = table.StandardDeduction[w4.FilingStatus] + w4.AnnualDeductions;
            annualTaxable = annualWages - deduction;
        }

        if (annualTaxable.IsNegative)
        {
            annualTaxable = Money.Zero;
        }

        var annualTax = table.CalculateFederalTax(annualTaxable, w4.FilingStatus);

        // Dependant credits reduce withholding directly.
        var credits = (table.CreditPerQualifyingChild * w4.QualifyingChildren)
                      + (table.CreditPerOtherDependant * w4.OtherDependants);

        if (w4.IsNonResidentAlien && (w4.QualifyingChildren > 0 || w4.OtherDependants > 0))
        {
            warnings.Add(
                "Dependant credits were applied, but a non-resident alien is often not eligible. " +
                "Confirm your eligibility before relying on this figure.");
        }

        annualTax -= credits;

        if (annualTax.IsNegative)
        {
            annualTax = Money.Zero;
        }

        if (w4.MultipleJobsChecked)
        {
            warnings.Add(
                "The multiple-jobs box is checked. Real withholding uses a separate IRS table for this, " +
                "so treat the estimate as approximate.");
        }

        var perPeriod = annualTax / periodsPerYear;
        return (perPeriod + w4.ExtraWithholdingPerPeriod).Round();
    }

    private static (Money SocialSecurity, Money Medicare) CalculateFica(
        Money ficaWagesPerPeriod,
        PayrollRequest request)
    {
        var table = request.TaxYear;
        var ytd = request.YearToDateTaxableWages;

        // Social Security stops once the wage base is reached.
        var remainingBase = table.SocialSecurityWageBase - ytd;
        var socialSecurityWages = remainingBase <= Money.Zero
            ? Money.Zero
            : Money.Min(ficaWagesPerPeriod, remainingBase);

        var socialSecurity = (socialSecurityWages * table.SocialSecurityRate).Round();
        var medicare = (ficaWagesPerPeriod * table.MedicareRate).Round();

        // The additional Medicare rate applies to wages above the threshold.
        var wagesAboveThreshold = ytd + ficaWagesPerPeriod - table.AdditionalMedicareThreshold;
        if (wagesAboveThreshold > Money.Zero)
        {
            var subjectToAdditional = Money.Min(wagesAboveThreshold, ficaWagesPerPeriod);
            medicare += (subjectToAdditional * table.AdditionalMedicareRate).Round();
        }

        return (socialSecurity, medicare.Round());
    }

    /// <summary>Projects a full year from one representative pay period.</summary>
    public static AnnualTaxProjection Project(PayrollRequest request)
    {
        var period = Calculate(request);
        var periodsPerYear = request.PayFrequency.PaymentsPerYear();

        return new AnnualTaxProjection
        {
            Year = request.TaxYear.Year,
            GrossIncome = (period.GrossPay * periodsPerYear).Round(),
            FederalWithholding = (period.FederalWithholding * periodsPerYear).Round(),
            StateWithholding = (period.StateWithholding * periodsPerYear).Round(),
            SocialSecurity = (period.SocialSecurity * periodsPerYear).Round(),
            Medicare = (period.Medicare * periodsPerYear).Round(),
            RetirementContribution = (period.RetirementContribution * periodsPerYear).Round(),
            EmployerRetirementContribution = (period.EmployerRetirementContribution * periodsPerYear).Round(),
            NetIncome = (period.NetPay * periodsPerYear).Round(),
            Warnings = period.Warnings
        };
    }
}

public sealed record AnnualTaxProjection
{
    public required int Year { get; init; }

    public required Money GrossIncome { get; init; }

    public required Money FederalWithholding { get; init; }

    public required Money StateWithholding { get; init; }

    public required Money SocialSecurity { get; init; }

    public required Money Medicare { get; init; }

    public required Money RetirementContribution { get; init; }

    public required Money EmployerRetirementContribution { get; init; }

    public required Money NetIncome { get; init; }

    public required IReadOnlyList<string> Warnings { get; init; }

    public Money TotalTax => FederalWithholding + StateWithholding + SocialSecurity + Medicare;
}
