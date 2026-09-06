using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.Core.Income;

/// <summary>
/// Which assumption to use when hours or bonuses vary. Budgeting on the optimistic figure is how
/// a household ends up overdrawn, so cash-flow projections default to conservative.
/// </summary>
public enum IncomeEstimate
{
    Conservative = 0,
    Normal = 1,
    Optimistic = 2
}

/// <summary>
/// Hours that differ week to week. Defaults model a 35 / 37.5 / 40 hour week rather than
/// assuming every week produces identical pay.
/// </summary>
public sealed record VariableHours(decimal Conservative, decimal Normal, decimal Optimistic)
{
    public static VariableHours Standard { get; } = new(35m, 37.5m, 40m);

    public static VariableHours Fixed(decimal hours) => new(hours, hours, hours);

    public static VariableHours None { get; } = new(0m, 0m, 0m);

    public decimal For(IncomeEstimate estimate) => estimate switch
    {
        IncomeEstimate.Conservative => Conservative,
        IncomeEstimate.Normal => Normal,
        IncomeEstimate.Optimistic => Optimistic,
        _ => throw new ArgumentOutOfRangeException(nameof(estimate), estimate, "Unknown estimate.")
    };

    public void Validate()
    {
        if (Conservative < 0m || Normal < 0m || Optimistic < 0m)
        {
            throw new ArgumentException("Hours cannot be negative.");
        }

        if (Conservative > Normal || Normal > Optimistic)
        {
            throw new ArgumentException(
                "Hours must be ordered from conservative through normal to optimistic.");
        }
    }
}

public abstract record IncomeSource
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    /// <summary>The household member who earns it.</summary>
    public required Guid MemberId { get; init; }

    public required Frequency PayFrequency { get; init; }

    /// <summary>A known payday, used to anchor the pay calendar.</summary>
    public required DateOnly AnchorPayDate { get; init; }

    /// <summary>False for reimbursements, which are money in but not taxable income.</summary>
    public bool IsTaxable { get; init; } = true;

    public bool IsActive { get; init; } = true;

    /// <summary>
    /// When false, only <see cref="AnchorPayDate"/> is treated as a known payday. Later dates are
    /// not generated until the household confirms the recurring rule.
    /// </summary>
    public bool PayScheduleConfirmed { get; init; } = true;

    public string? Notes { get; init; }

    /// <summary>How this money should be classified. Reimbursements stay out of wages.</summary>
    public IncomeRole Role { get; init; } = IncomeRole.Wages;

    public DateOnly? StartsOn { get; init; }

    public DateOnly? EndsOn { get; init; }

    /// <summary>Weeks covered by one pay period, derived from the pay frequency.</summary>
    public decimal WeeksPerPeriod => PayFrequency.IsRecurring()
        ? 52m / PayFrequency.PaymentsPerYear()
        : 0m;

    /// <summary>
    /// Paydays that should appear on the cash-flow calendar. An unconfirmed schedule never
    /// invents dates after the known anchor.
    /// </summary>
    public IEnumerable<DateOnly> PayDates(DateOnly from, DateOnly to)
    {
        if (!IsActive || to < from)
        {
            return [];
        }

        var start = StartsOn is not null && StartsOn > from ? StartsOn.Value : from;
        var end = EndsOn is not null && EndsOn < to ? EndsOn.Value : to;
        if (end < start)
        {
            return [];
        }

        if (!PayFrequency.IsRecurring())
        {
            return AnchorPayDate >= start && AnchorPayDate <= end ? [AnchorPayDate] : [];
        }

        if (!PayScheduleConfirmed)
        {
            return AnchorPayDate >= start && AnchorPayDate <= end ? [AnchorPayDate] : [];
        }

        return RecurrenceSchedule.Enumerate(PayFrequency, AnchorPayDate, start, end);
    }

    public abstract Money GrossPerPeriod(IncomeEstimate estimate);

    public Money GrossPerYear(IncomeEstimate estimate) =>
        PayFrequency.IsRecurring()
            ? GrossPerPeriod(estimate) * PayFrequency.PaymentsPerYear()
            : GrossPerPeriod(estimate);

    public virtual void Validate()
    {
        if (Id == Guid.Empty)
        {
            throw new ArgumentException("An income source needs an identifier.");
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new ArgumentException("An income source needs a name.");
        }

        if (MemberId == Guid.Empty)
        {
            throw new ArgumentException("An income source must belong to a household member.");
        }

        if (StartsOn is not null && EndsOn is not null && EndsOn < StartsOn)
        {
            throw new ArgumentException("An income source cannot end before it starts.");
        }
    }
}

public enum IncomeRole
{
    Wages = 0,
    SelfEmployment = 1,
    BenefitOrGrant = 2,
    OneTime = 3,
    Reimbursement = 4
}

/// <summary>
/// Hourly work, where the number of hours is the main source of variability.
/// </summary>
public sealed record HourlyIncome : IncomeSource
{
    public required Money HourlyRate { get; init; }

    public VariableHours WeeklyHours { get; init; } = VariableHours.Standard;

    public VariableHours WeeklyOvertimeHours { get; init; } = VariableHours.None;

    /// <summary>Overtime pay multiplier, typically 1.5.</summary>
    public decimal OvertimeMultiplier { get; init; } = 1.5m;

    /// <summary>Paid leave hours in the period, paid at the base rate.</summary>
    public decimal PaidLeaveHoursPerPeriod { get; init; }

    /// <summary>Unpaid leave hours in the period, deducted from base hours.</summary>
    public decimal UnpaidLeaveHoursPerPeriod { get; init; }

    public override Money GrossPerPeriod(IncomeEstimate estimate)
    {
        var weeks = WeeksPerPeriod;
        var baseHours = (WeeklyHours.For(estimate) * weeks) + PaidLeaveHoursPerPeriod - UnpaidLeaveHoursPerPeriod;

        if (baseHours < 0m)
        {
            baseHours = 0m;
        }

        var overtimeHours = WeeklyOvertimeHours.For(estimate) * weeks;

        return (HourlyRate * baseHours) + (HourlyRate * overtimeHours * OvertimeMultiplier);
    }

    public override void Validate()
    {
        base.Validate();

        if (HourlyRate.IsNegative)
        {
            throw new ArgumentException("An hourly rate cannot be negative.");
        }

        if (OvertimeMultiplier < 1m)
        {
            throw new ArgumentException("An overtime multiplier below 1 would pay less than the base rate.");
        }

        if (PaidLeaveHoursPerPeriod < 0m || UnpaidLeaveHoursPerPeriod < 0m)
        {
            throw new ArgumentException("Leave hours cannot be negative.");
        }

        WeeklyHours.Validate();
        WeeklyOvertimeHours.Validate();

        if (!PayFrequency.IsRecurring())
        {
            throw new ArgumentException("Hourly work needs a recurring pay schedule.");
        }
    }
}

public sealed record SalaryIncome : IncomeSource
{
    public required Money AnnualSalary { get; init; }

    public override Money GrossPerPeriod(IncomeEstimate estimate)
    {
        _ = estimate;
        return PayFrequency.IsRecurring()
            ? AnnualSalary / PayFrequency.PaymentsPerYear()
            : AnnualSalary;
    }

    public override void Validate()
    {
        base.Validate();

        if (AnnualSalary.IsNegative)
        {
            throw new ArgumentException("A salary cannot be negative.");
        }

        if (!PayFrequency.IsRecurring())
        {
            throw new ArgumentException("A salary needs a recurring pay schedule.");
        }
    }
}

/// <summary>
/// Bonuses, commission, side work, irregular income and reimbursements.
/// Set <see cref="IncomeSource.IsTaxable"/> to false for mileage and expense reimbursements so they
/// are not treated as taxable earnings.
/// </summary>
public sealed record VariableIncome : IncomeSource
{
    /// <summary>Amount per pay period under each assumption. Conservative is often zero.</summary>
    public required VariableHours AmountPerPeriod { get; init; }

    public override Money GrossPerPeriod(IncomeEstimate estimate) => new(AmountPerPeriod.For(estimate));

    public override void Validate()
    {
        base.Validate();
        AmountPerPeriod.Validate();
    }
}

/// <summary>
/// Mileage reimbursement, which is money in but not taxable earnings.
/// </summary>
public sealed record MileageReimbursement : IncomeSource
{
    public required decimal MilesPerPeriod { get; init; }

    public required Money RatePerMile { get; init; }

    public override Money GrossPerPeriod(IncomeEstimate estimate)
    {
        _ = estimate;
        return RatePerMile * MilesPerPeriod;
    }

    public override void Validate()
    {
        base.Validate();

        if (MilesPerPeriod < 0m)
        {
            throw new ArgumentException("Mileage cannot be negative.");
        }

        if (RatePerMile.IsNegative)
        {
            throw new ArgumentException("A mileage rate cannot be negative.");
        }

        if (IsTaxable)
        {
            throw new ArgumentException("Mileage reimbursement must be recorded as non-taxable.");
        }
    }
}

/// <summary>
/// An actual payslip, used to compare estimates against reality.
/// </summary>
public sealed record Payslip
{
    public required Guid Id { get; init; }

    public required Guid IncomeSourceId { get; init; }

    public required DateOnly PayDate { get; init; }

    public required Money GrossPay { get; init; }

    public Money PreTaxDeductions { get; init; } = Money.Zero;

    public Money FederalWithholding { get; init; } = Money.Zero;

    public Money StateWithholding { get; init; } = Money.Zero;

    public Money SocialSecurity { get; init; } = Money.Zero;

    public Money Medicare { get; init; } = Money.Zero;

    public Money PostTaxDeductions { get; init; } = Money.Zero;

    /// <summary>Non-taxable reimbursements paid alongside wages.</summary>
    public Money NonTaxableReimbursements { get; init; } = Money.Zero;

    public decimal HoursWorked { get; init; }

    public decimal OvertimeHoursWorked { get; init; }

    /// <summary>Tax year that applied when this slip was recorded. Later rules do not rewrite it.</summary>
    public int? TaxYear { get; init; }

    public string? RuleSetVersion { get; init; }

    public Money TotalWithholding =>
        FederalWithholding + StateWithholding + SocialSecurity + Medicare;

    public Money NetPay =>
        GrossPay - PreTaxDeductions - TotalWithholding - PostTaxDeductions + NonTaxableReimbursements;

    public void Validate()
    {
        if (Id == Guid.Empty)
        {
            throw new ArgumentException("A payslip needs an identifier.");
        }

        if (GrossPay.IsNegative)
        {
            throw new ArgumentException("Gross pay cannot be negative.");
        }

        if (HoursWorked < 0m || OvertimeHoursWorked < 0m)
        {
            throw new ArgumentException("Hours worked cannot be negative.");
        }
    }
}

public sealed record IncomeComparison(
    Money Expected,
    Money Actual,
    Money Difference,
    decimal PercentageDifference,
    string Explanation);

public static class IncomeReconciler
{
    /// <summary>
    /// Compares an estimate against a real payslip and explains the gap in plain language.
    /// </summary>
    public static IncomeComparison Compare(Money expectedNet, Payslip actual)
    {
        ArgumentNullException.ThrowIfNull(actual);

        var actualNet = actual.NetPay.Round();
        var expected = expectedNet.Round();
        var difference = (actualNet - expected).Round();

        var percentage = expected.IsZero
            ? 0m
            : Math.Round(difference.Amount / Math.Abs(expected.Amount) * 100m, 1);

        var explanation = difference.IsZero
            ? "The payslip matched the estimate."
            : difference.IsNegative
                ? $"The payslip was {difference.Abs().ToDisplayString()} lower than estimated ({percentage:0.0}%). " +
                  "Check hours, overtime and withholding."
                : $"The payslip was {difference.ToDisplayString()} higher than estimated ({percentage:0.0}%). " +
                  "Check overtime, bonuses and reimbursements.";

        return new IncomeComparison(expected, actualNet, difference, percentage, explanation);
    }
}
