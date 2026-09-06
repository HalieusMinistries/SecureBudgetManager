using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.Core.Tax;

/// <summary>Utah Publication 14 withholding marital status. Head of household uses Single.</summary>
public enum UtahWithholdingStatus
{
    Single = 0,
    Married = 1
}

/// <summary>
/// One versioned Utah withholding schedule. This is the Publication 14 formula, not a
/// headline flat-rate multiplication.
/// </summary>
public sealed record UtahWithholdingSchedule
{
    public required string Version { get; init; }

    public required int TaxYear { get; init; }

    public required DateOnly EffectiveFrom { get; init; }

    public DateOnly? EffectiveTo { get; init; }

    public required decimal Rate { get; init; }

    public required decimal CreditReductionRate { get; init; }

    public required Money SingleBaseAllowance { get; init; }

    public required Money MarriedBaseAllowance { get; init; }

    public required Money SingleWageExemption { get; init; }

    public required Money MarriedWageExemption { get; init; }

    public required Frequency Period { get; init; }

    public required string OfficialSource { get; init; }

    public required DateOnly PublicationDate { get; init; }

    public required DateOnly ReviewDate { get; init; }

    public bool AppliesTo(DateOnly payrollPeriodStart) =>
        payrollPeriodStart >= EffectiveFrom
        && (EffectiveTo is null || payrollPeriodStart <= EffectiveTo);
}

public sealed record UtahWithholdingResult
{
    public required Money TaxableWages { get; init; }

    public required Money GrossTax { get; init; }

    public required Money WithholdingAllowance { get; init; }

    public required Money Withholding { get; init; }

    public required UtahWithholdingSchedule Schedule { get; init; }

    public required string Explanation { get; init; }
}

public static class UtahWithholdingLibrary
{
    public const string Unavailable = "Tax rules unavailable or require review";

    /// <summary>
    /// Publication 14 Rev. 4/26, effective for pay periods beginning on or after 1 June 2026.
    /// Weekly Single: rate 4.45%, base $9, exemption $180.
    /// </summary>
    public static UtahWithholdingSchedule Weekly2026Revised { get; } = Weekly(
        version: "UT-WH-2026-06",
        year: 2026,
        from: new DateOnly(2026, 6, 1),
        until: null,
        rate: 0.0445m,
        singleBase: 9m,
        marriedBase: 19m,
        singleExempt: 180m,
        marriedExempt: 360m,
        source: "Utah State Tax Commission Publication 14, Withholding Tax Guide, Rev. 4/26",
        published: new DateOnly(2026, 4, 1),
        review: new DateOnly(2027, 4, 1));

    public static UtahWithholdingSchedule Biweekly2026Revised { get; } = FromWeeklyScale(
        Weekly2026Revised,
        Frequency.Fortnightly,
        singleBase: 19m,
        marriedBase: 37m,
        singleExempt: 360m,
        marriedExempt: 719m);

    public static UtahWithholdingSchedule Semimonthly2026Revised { get; } = FromWeeklyScale(
        Weekly2026Revised,
        Frequency.TwiceMonthly,
        singleBase: 20m,
        marriedBase: 40m,
        singleExempt: 390m,
        marriedExempt: 779m);

    public static UtahWithholdingSchedule Monthly2026Revised { get; } = FromWeeklyScale(
        Weekly2026Revised,
        Frequency.Monthly,
        singleBase: 40m,
        marriedBase: 81m,
        singleExempt: 779m,
        marriedExempt: 1558m);

    /// <summary>
    /// 2025 Publication 14 parameters as restated by NFC/USTC when the 2026 revision was issued:
    /// 4.50% rate, annual single base $450 / exemption $9,107.
    /// Converted to a weekly schedule (÷ 52) for payroll-period use. Applies through 31 May 2026.
    /// </summary>
    public static UtahWithholdingSchedule Weekly2025 { get; } = Weekly(
        version: "UT-WH-2025-01",
        year: 2025,
        from: new DateOnly(2025, 1, 1),
        until: new DateOnly(2026, 5, 31),
        rate: 0.0450m,
        singleBase: 8.65m,
        marriedBase: 17.31m,
        singleExempt: 175.13m,
        marriedExempt: 350.25m,
        source: "Utah 2025 withholding formula (4.50%) as published before Publication 14 Rev. 4/26; " +
                "annual single base $450 and exemption $9,107 restated by USDA NFC bulletin 1782763921",
        published: new DateOnly(2025, 1, 1),
        review: new DateOnly(2026, 6, 1));

    public static IReadOnlyList<UtahWithholdingSchedule> BuiltIn { get; } =
    [
        Weekly2026Revised,
        Biweekly2026Revised,
        Semimonthly2026Revised,
        Monthly2026Revised,
        Weekly2025
    ];

    public static decimal StatutoryRate(int taxYear) => taxYear switch
    {
        2025 => 0.0450m,
        2026 => 0.0445m,
        _ => 0m
    };

    public static bool TryGet(
        DateOnly payrollPeriodStart,
        Frequency frequency,
        out UtahWithholdingSchedule? schedule,
        out string warning)
    {
        var period = Normalize(frequency);
        var match = BuiltIn
            .Where(item => item.Period == period && item.AppliesTo(payrollPeriodStart))
            .OrderByDescending(item => item.EffectiveFrom)
            .FirstOrDefault();

        if (match is not null)
        {
            schedule = match;
            warning = "Estimated Utah withholding using the Publication 14 formula. This is not tax advice.";
            return true;
        }

        schedule = null;
        warning = Unavailable;
        return false;
    }

    public static UtahWithholdingResult Calculate(
        Money taxableWages,
        UtahWithholdingStatus status,
        UtahWithholdingSchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        var wages = Money.Max(Money.Zero, taxableWages.Round());
        var gross = (wages * schedule.Rate).Round();
        var baseAllowance = status == UtahWithholdingStatus.Married
            ? schedule.MarriedBaseAllowance
            : schedule.SingleBaseAllowance;
        var exemption = status == UtahWithholdingStatus.Married
            ? schedule.MarriedWageExemption
            : schedule.SingleWageExemption;

        var excess = Money.Max(Money.Zero, (wages - exemption).Round());
        var reduction = (excess * schedule.CreditReductionRate).Round();
        var allowance = Money.Max(Money.Zero, (baseAllowance - reduction).Round());
        var withholding = Money.Max(Money.Zero, (gross - allowance).Round());

        return new UtahWithholdingResult
        {
            TaxableWages = wages,
            GrossTax = gross,
            WithholdingAllowance = allowance,
            Withholding = withholding,
            Schedule = schedule,
            Explanation =
                $"Publication 14 {schedule.Version}: line 2 = {wages.ToDisplayString()} × {schedule.Rate:P2} " +
                $"= {gross.ToDisplayString()}; allowance = max(0, {baseAllowance.ToDisplayString()} − " +
                $"{schedule.CreditReductionRate:P1} × max(0, wages − {exemption.ToDisplayString()})) " +
                $"= {allowance.ToDisplayString()}; withholding = {withholding.ToDisplayString()}."
        };
    }

    public static UtahWithholdingStatus StatusFrom(FilingStatus filingStatus) =>
        filingStatus == FilingStatus.MarriedFilingJointly
            ? UtahWithholdingStatus.Married
            : UtahWithholdingStatus.Single;

    private static Frequency Normalize(Frequency frequency) => frequency switch
    {
        Frequency.Weekly => Frequency.Weekly,
        Frequency.Fortnightly => Frequency.Fortnightly,
        Frequency.TwiceMonthly => Frequency.TwiceMonthly,
        Frequency.Monthly => Frequency.Monthly,
        _ => Frequency.Weekly
    };

    private static UtahWithholdingSchedule Weekly(
        string version,
        int year,
        DateOnly from,
        DateOnly? until,
        decimal rate,
        decimal singleBase,
        decimal marriedBase,
        decimal singleExempt,
        decimal marriedExempt,
        string source,
        DateOnly published,
        DateOnly review) =>
        new()
        {
            Version = version,
            TaxYear = year,
            EffectiveFrom = from,
            EffectiveTo = until,
            Rate = rate,
            CreditReductionRate = 0.013m,
            SingleBaseAllowance = new Money(singleBase),
            MarriedBaseAllowance = new Money(marriedBase),
            SingleWageExemption = new Money(singleExempt),
            MarriedWageExemption = new Money(marriedExempt),
            Period = Frequency.Weekly,
            OfficialSource = source,
            PublicationDate = published,
            ReviewDate = review
        };

    private static UtahWithholdingSchedule FromWeeklyScale(
        UtahWithholdingSchedule weekly,
        Frequency period,
        decimal singleBase,
        decimal marriedBase,
        decimal singleExempt,
        decimal marriedExempt) =>
        weekly with
        {
            Period = period,
            SingleBaseAllowance = new Money(singleBase),
            MarriedBaseAllowance = new Money(marriedBase),
            SingleWageExemption = new Money(singleExempt),
            MarriedWageExemption = new Money(marriedExempt)
        };
}
