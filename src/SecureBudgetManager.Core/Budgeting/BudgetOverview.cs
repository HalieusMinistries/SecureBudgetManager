using SecureBudgetManager.Core.Benefits;
using SecureBudgetManager.Core.CashFlow;
using SecureBudgetManager.Core.Expenses;
using SecureBudgetManager.Core.Income;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Storage;
using SecureBudgetManager.Core.Tax;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.Core.Budgeting;

/// <summary>
/// The dashboard reporting period. Average monthly is always annual divided by 12, never weekly times four.
/// </summary>
public enum DisplayPeriod
{
    Weekly = 0,
    AverageMonthly = 1,
    Annual = 2
}

public sealed record MetricDisplay
{
    public required string Label { get; init; }

    /// <summary>Null when the figure is missing or not meaningful as a number.</summary>
    public Money? Amount { get; init; }

    public required string Text { get; init; }

    public bool IsEstimated { get; init; }

    public bool IsUnavailable { get; init; }

    public static MetricDisplay Value(string label, Money amount, bool estimated = false) => new()
    {
        Label = label,
        Amount = amount.Round(),
        Text = amount.Round().ToDisplayString(),
        IsEstimated = estimated,
        IsUnavailable = false
    };

    public static MetricDisplay Unavailable(string label, string text) => new()
    {
        Label = label,
        Amount = null,
        Text = text,
        IsEstimated = false,
        IsUnavailable = true
    };
}

public sealed record AttentionItem(string Message);

public sealed record BudgetOverview
{
    public required DisplayPeriod Period { get; init; }

    public required string PeriodLabel { get; init; }

    public required MetricDisplay GrossIncome { get; init; }

    public required MetricDisplay PayrollDeductions { get; init; }

    public required MetricDisplay EstimatedTaxes { get; init; }

    public required MetricDisplay RecurringExpenses { get; init; }

    public required MetricDisplay TakeHomePay { get; init; }

    public required MetricDisplay NetCashFlow { get; init; }

    public required MetricDisplay SafeToSpend { get; init; }

    public required MetricDisplay LowestProjectedBalance { get; init; }

    public required IReadOnlyList<AttentionItem> Attention { get; init; }

    public bool HasAttention => Attention.Count > 0;
}

/// <summary>
/// Builds dashboard figures from a persisted household document using the existing money, time,
/// payroll and cash-flow engines. View models must not reimplement these conversions.
/// </summary>
public static class BudgetOverviewCalculator
{
    public static string PeriodLabel(DisplayPeriod period) => period switch
    {
        DisplayPeriod.Weekly => "Weekly",
        DisplayPeriod.AverageMonthly => "Average monthly",
        DisplayPeriod.Annual => "Annual",
        _ => throw new ArgumentOutOfRangeException(nameof(period), period, "Unknown display period.")
    };

    public static Money ToPeriod(Money annualAmount, DisplayPeriod period)
    {
        return period switch
        {
            DisplayPeriod.Weekly => FrequencyConverter.FromAnnual(annualAmount, Frequency.Weekly).Round(),
            DisplayPeriod.AverageMonthly => FrequencyConverter.FromAnnual(annualAmount, Frequency.Monthly).Round(),
            DisplayPeriod.Annual => annualAmount.Round(),
            _ => throw new ArgumentOutOfRangeException(nameof(period), period, "Unknown display period.")
        };
    }

    public static BudgetOverview Locked() => Empty(
        DisplayPeriod.AverageMonthly,
        "Open the household database to see household figures.");

    public static BudgetOverview Empty(DisplayPeriod period, string reason) => new()
    {
        Period = period,
        PeriodLabel = PeriodLabel(period),
        GrossIncome = MetricDisplay.Unavailable("Expected gross income", reason),
        PayrollDeductions = MetricDisplay.Unavailable("Estimated payroll deductions", reason),
        EstimatedTaxes = MetricDisplay.Unavailable("Estimated taxes", reason),
        RecurringExpenses = MetricDisplay.Unavailable("Recurring household expenses", reason),
        TakeHomePay = MetricDisplay.Unavailable("Expected take-home pay", reason),
        NetCashFlow = MetricDisplay.Unavailable("Net cash flow", reason),
        SafeToSpend = MetricDisplay.Unavailable("Safe to spend", reason),
        LowestProjectedBalance = MetricDisplay.Unavailable("Lowest projected balance", reason),
        Attention = []
    };

    public static BudgetOverview Build(
        BudgetDocument? document,
        DisplayPeriod period,
        DateOnly today)
    {
        if (document is null)
        {
            return Locked();
        }

        var attention = new List<AttentionItem>();
        var takeHome = TakeHomeCalculator.From(document, IncomeEstimate.Normal, today);

        if (document.Members.Count == 0)
        {
            attention.Add(new AttentionItem("Add at least one household member before recording income."));
        }

        if (document.IncomeSources.Count == 0)
        {
            attention.Add(new AttentionItem("No income has been recorded yet."));
        }

        if (document.Expenses.Count == 0)
        {
            attention.Add(new AttentionItem("No recurring expenses have been recorded yet."));
        }

        foreach (var item in takeHome.Attention)
        {
            attention.Add(item);
        }

        var gross = !takeHome.HasRegularWages && takeHome.AnnualReimbursements > Money.Zero
            ? MetricDisplay.Unavailable("Regular taxable wages", "None — reimbursements recorded separately")
            : DisplayIncome(
                "Regular taxable wages",
                takeHome.HasRegularWages,
                takeHome.AnnualRegularGross,
                period,
                takeHome.GrossIsEstimated);

        var deductions = DisplayOptional(
            "Estimated payroll deductions",
            takeHome.HasGrossIncome,
            takeHome.HasPayrollDetails,
            takeHome.AnnualEmployeeDeductions,
            period,
            estimated: true);

        var taxes = DisplayOptional(
            "Estimated taxes",
            takeHome.HasTaxableIncome,
            takeHome.HasPayrollDetails,
            takeHome.AnnualTax,
            period,
            estimated: true);

        var expensesAnnual = Money.Sum(document.Expenses
            .Where(expense => !expense.IsPaused)
            .Select(expense => expense.AnnualCost)).Round();

        var expenses = document.Expenses.Count == 0
            ? MetricDisplay.Unavailable("Recurring household expenses", "No records yet")
            : MetricDisplay.Value(
                "Recurring household expenses",
                ToPeriod(expensesAnnual, period),
                estimated: document.Expenses.Any(expense =>
                    !expense.IsPaused && expense.Variability == ExpenseVariability.Variable));

        var takeHomeMetric = DisplayIncome(
            "Expected take-home pay",
            takeHome.HasGrossIncome,
            takeHome.AnnualTakeHome,
            period,
            takeHome.TakeHomeIsEstimated);

        MetricDisplay net;
        if (!takeHome.HasGrossIncome && document.Expenses.Count == 0)
        {
            net = MetricDisplay.Unavailable("Net cash flow", "No records yet");
        }
        else if (takeHomeMetric.IsUnavailable || expenses.IsUnavailable && document.Expenses.Count > 0)
        {
            net = MetricDisplay.Unavailable("Net cash flow", "Insufficient information");
        }
        else
        {
            var expenseAmount = document.Expenses.Count == 0 ? Money.Zero : ToPeriod(expensesAnnual, period);
            var incomeAmount = takeHomeMetric.Amount ?? Money.Zero;
            var netAmount = (incomeAmount - expenseAmount).Round();
            net = MetricDisplay.Value(
                period == DisplayPeriod.AverageMonthly
                    ? "Forecast monthly surplus — not available today"
                    : "Forecast surplus — not available today",
                netAmount,
                takeHomeMetric.IsEstimated || expenses.IsEstimated);

            if (netAmount.IsNegative)
            {
                attention.Add(new AttentionItem("Expected spending exceeds expected take-home pay for this period."));
            }
        }

        var (safe, lowest) = ProjectCash(document, today, takeHome);

        return new BudgetOverview
        {
            Period = period,
            PeriodLabel = PeriodLabel(period),
            GrossIncome = gross,
            PayrollDeductions = deductions,
            EstimatedTaxes = taxes,
            RecurringExpenses = expenses,
            TakeHomePay = takeHomeMetric,
            NetCashFlow = net,
            SafeToSpend = safe,
            LowestProjectedBalance = lowest,
            Attention = attention
        };
    }

    private static MetricDisplay DisplayIncome(
        string label,
        bool hasRecords,
        Money annual,
        DisplayPeriod period,
        bool estimated)
    {
        if (!hasRecords)
        {
            return MetricDisplay.Unavailable(label, "No records yet");
        }

        return MetricDisplay.Value(label, ToPeriod(annual, period), estimated);
    }

    private static MetricDisplay DisplayOptional(
        string label,
        bool hasIncome,
        bool hasDetails,
        Money annual,
        DisplayPeriod period,
        bool estimated)
    {
        if (!hasIncome)
        {
            return MetricDisplay.Unavailable(label, "No records yet");
        }

        if (!hasDetails)
        {
            return MetricDisplay.Unavailable(label, "Insufficient information");
        }

        return MetricDisplay.Value(label, ToPeriod(annual, period), estimated);
    }

    private static (MetricDisplay Safe, MetricDisplay Lowest) ProjectCash(
        BudgetDocument document,
        DateOnly today,
        TakeHomeSnapshot takeHome)
    {
        if (document.Accounts.Count == 0)
        {
            const string missingBalance = "Insufficient information";
            return (
                MetricDisplay.Unavailable("Safe to spend", missingBalance),
                MetricDisplay.Unavailable("Lowest projected balance", missingBalance));
        }

        if (!takeHome.HasGrossIncome && document.Expenses.All(expense => expense.IsPaused))
        {
            return (
                MetricDisplay.Unavailable("Safe to spend", "No records yet"),
                MetricDisplay.Unavailable("Lowest projected balance", "No records yet"));
        }

        var horizon = today.AddDays(90);
        var netBySource = takeHome.NetPayPerPeriod;

        var projection = CashFlowProjector.Project(new CashFlowInputs
        {
            From = today,
            To = horizon,
            StartingBalance = document.TotalBalance,
            IncomeSources = document.IncomeSources,
            NetPayPerPeriod = netBySource,
            Expenses = document.Expenses,
            Payslips = document.Payslips,
            Assumption = IncomeEstimate.Conservative
        });

        var safe = SafeToSpendCalculator.Calculate(
            projection,
            today,
            document.Preferences.MinimumBalanceReserve);

        return (
            MetricDisplay.Value("Projected remainder after dated bills", safe.Amount, estimated: true),
            MetricDisplay.Value("Lowest projected balance", projection.LowestBalance, estimated: true));
    }
}

public sealed record TakeHomeSnapshot
{
    public required Money AnnualGross { get; init; }

    public required Money AnnualRegularGross { get; init; }

    public required Money AnnualReimbursements { get; init; }

    public required Money AnnualOneTime { get; init; }

    public required Money AnnualTakeHome { get; init; }

    public required Money AnnualTax { get; init; }

    public required Money AnnualEmployeeDeductions { get; init; }

    public required Money AnnualEmployerMatch { get; init; }

    public required bool HasGrossIncome { get; init; }

    public required bool HasRegularWages { get; init; }

    public required bool HasTaxableIncome { get; init; }

    public required bool HasPayrollDetails { get; init; }

    public required bool GrossIsEstimated { get; init; }

    public required bool TakeHomeIsEstimated { get; init; }

    public required IReadOnlyDictionary<Guid, Money> NetPayPerPeriod { get; init; }

    public required IReadOnlyList<AttentionItem> Attention { get; init; }
}

/// <summary>
/// Per-member take-home using <see cref="PayrollCalculator"/> once against combined taxable earnings.
/// Employer match is calculated and exposed but never added to take-home pay.
/// </summary>
public static class TakeHomeCalculator
{
    public static TakeHomeSnapshot From(
        BudgetDocument document,
        IncomeEstimate estimate,
        DateOnly? today = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        var attention = new List<AttentionItem>();
        var netBySource = new Dictionary<Guid, Money>();

        var annualGross = Money.Zero;
        var annualRegularGross = Money.Zero;
        var annualReimbursements = Money.Zero;
        var annualOneTime = Money.Zero;
        var annualTakeHome = Money.Zero;
        var annualTax = Money.Zero;
        var annualDeductions = Money.Zero;
        var annualEmployerMatch = Money.Zero;
        var hasGross = false;
        var hasRegular = false;
        var hasTaxable = false;
        var hasPayroll = false;
        var grossEstimated = false;
        var takeHomeEstimated = false;

        var active = document.IncomeSources.Where(source => source.IsActive).ToList();

        foreach (var source in active)
        {
            hasGross = true;
            if (source.IsOneTimeIncome)
            {
                annualOneTime += source.GrossPerPeriod(estimate).Round();
                attention.Add(new AttentionItem(
                    $"{source.Name} is one-time expected income and is not averaged into regular monthly wages."));
                continue;
            }

            if (source.IsReimbursementIncome)
            {
                annualReimbursements += source.GrossPerYear(estimate).Round();
                attention.Add(new AttentionItem(
                    $"{source.Name} is a reimbursement and is kept separate from regular taxable wages."));
                continue;
            }

            if (source.IsRegularWage)
            {
                hasRegular = true;
                annualRegularGross += source.GrossPerYear(estimate).Round();
                annualGross += source.GrossPerYear(estimate).Round();
            }
            else
            {
                annualGross += source.GrossPerYear(estimate).Round();
            }

            if (IsVariable(source))
            {
                grossEstimated = true;
                takeHomeEstimated = true;
            }
        }

        foreach (var group in active.Where(source => !source.IsOneTimeIncome).GroupBy(source => source.MemberId))
        {
            var taxable = group.Where(source => source.IsTaxable && source.IsRegularWage).ToList();
            var reimbursements = group.Where(source => source.IsReimbursementIncome).ToList();

            var reimbursementAnnual = Money.Sum(reimbursements.Select(source => source.GrossPerYear(estimate)));
            foreach (var source in reimbursements)
            {
                netBySource[source.Id] = source.GrossPerPeriod(estimate).Round();
            }

            if (taxable.Count == 0)
            {
                annualTakeHome += reimbursementAnnual;
                continue;
            }

            hasTaxable = true;
            var taxableAnnual = Money.Sum(taxable.Select(source => source.GrossPerYear(estimate)));
            var payFrequency = taxable[0].PayFrequency;
            var profile = document.PayrollProfiles.FirstOrDefault(item => item.MemberId == group.Key);

            if (profile is null)
            {
                attention.Add(new AttentionItem(
                    "Taxable income is recorded without payroll details, so take-home still uses gross pay."));
                takeHomeEstimated = true;
                annualTakeHome += taxableAnnual + reimbursementAnnual;

                foreach (var source in taxable)
                {
                    netBySource[source.Id] = source.GrossPerPeriod(estimate).Round();
                }

                continue;
            }

            hasPayroll = true;
            takeHomeEstimated = true;

            foreach (var benefit in document.Benefits.Where(plan => plan.MemberId == group.Key && !plan.IsConfirmed))
            {
                attention.Add(new AttentionItem(
                    $"{benefit.Name} of {benefit.EmployeePremiumPerPeriod.ToDisplayString()} per " +
                    $"{benefit.PremiumFrequency.ToDisplayName()} is awaiting payslip confirmation " +
                    "and is included in this forecast scenario."));
            }

            if (!TaxYearLibrary.TryGetYear(profile.TaxYear, out var table, out _) || table is null)
            {
                continue;
            }
            var grossPerPeriod = FrequencyConverter.FromAnnual(taxableAnnual, payFrequency);
            var reimbursementPerPeriod = FrequencyConverter.FromAnnual(reimbursementAnnual, payFrequency);
            var deductions = MergeDeductions(profile, document.Benefits, group.Key, payFrequency, today);

            var result = PayrollCalculator.Calculate(profile.ToRequest(
                grossPerPeriod,
                payFrequency,
                table,
                reimbursementPerPeriod,
                Money.Zero) with { Deductions = deductions });

            var periods = payFrequency.PaymentsPerYear();
            annualTax += result.TotalTax * periods;
            annualDeductions += (result.PreTaxDeductions + result.RetirementContribution + result.PostTaxDeductions) * periods;
            annualEmployerMatch += result.EmployerRetirementContribution * periods;
            annualTakeHome += result.NetPay * periods;

            // Split net across taxable sources in proportion to gross so cash-flow keeps a per-source figure.
            var shares = taxableAnnual.IsZero
                ? taxable.Select(_ => Money.Zero).ToArray()
                : result.NetPay.AllocateByWeight(taxable.Select(source => source.GrossPerPeriod(estimate).Amount).ToList());

            for (var i = 0; i < taxable.Count; i++)
            {
                netBySource[taxable[i].Id] = shares[i];
            }
        }

        return new TakeHomeSnapshot
        {
            AnnualGross = annualGross.Round(),
            AnnualRegularGross = annualRegularGross.Round(),
            AnnualReimbursements = annualReimbursements.Round(),
            AnnualOneTime = annualOneTime.Round(),
            AnnualTakeHome = annualTakeHome.Round(),
            AnnualTax = annualTax.Round(),
            AnnualEmployeeDeductions = annualDeductions.Round(),
            AnnualEmployerMatch = annualEmployerMatch.Round(),
            HasGrossIncome = hasGross,
            HasRegularWages = hasRegular,
            HasTaxableIncome = hasTaxable,
            HasPayrollDetails = hasPayroll,
            GrossIsEstimated = grossEstimated,
            TakeHomeIsEstimated = takeHomeEstimated,
            NetPayPerPeriod = netBySource,
            Attention = attention
        };
    }

    public static IReadOnlyList<PayrollDeduction> MergeDeductions(
        PayrollProfile profile,
        IReadOnlyList<BenefitPlan> benefits,
        Guid memberId,
        Frequency payFrequency,
        DateOnly? today)
    {
        var merged = profile.Deductions.ToList();

        foreach (var benefit in benefits.Where(plan => plan.MemberId == memberId))
        {
            if (today is not null && benefit.IsConfirmed && !benefit.AppliesOn(today.Value))
            {
                continue;
            }

            var perPay = FrequencyConverter.Convert(
                benefit.EmployeePremiumPerPeriod,
                benefit.PremiumFrequency,
                payFrequency);

            merged.Add(new PayrollDeduction
            {
                Name = benefit.Name,
                AmountPerPeriod = perPay,
                TaxTreatment = benefit.TaxTreatment
            });
        }

        return merged;
    }

    private static bool IsVariable(IncomeSource source) => source switch
    {
        HourlyIncome hourly => hourly.WeeklyHours.Conservative != hourly.WeeklyHours.Optimistic
            || hourly.WeeklyOvertimeHours.Conservative != hourly.WeeklyOvertimeHours.Optimistic,
        VariableIncome variable => variable.AmountPerPeriod.Conservative != variable.AmountPerPeriod.Optimistic,
        _ => false
    };
}
