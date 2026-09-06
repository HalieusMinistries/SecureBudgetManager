using SecureBudgetManager.Core.Budgeting;
using SecureBudgetManager.Core.Expenses;
using SecureBudgetManager.Core.Household;
using SecureBudgetManager.Core.Income;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Storage;
using SecureBudgetManager.Core.Tax;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.Core.Guidance;

/// <summary>
/// A reimbursement tied to the cost it repays, so it is not mistaken for spending money.
/// When no offset is recorded, the whole reimbursement is treated as offsetting: assuming it is
/// free income is the error that actually costs a household money.
/// </summary>
public sealed record ReimbursementOffset
{
    public required Guid Id { get; init; }

    public required Guid IncomeSourceId { get; init; }

    /// <summary>The expense, fund or debt this reimbursement is meant to cover. Optional.</summary>
    public Guid? ObligationId { get; init; }

    public ObligationKind Kind { get; init; } = ObligationKind.Expense;

    /// <summary>
    /// How much of each reimbursement actually offsets a cost. Anything above this is ordinary
    /// money. Recorded explicitly so the household decides, not the programme.
    /// </summary>
    public Money OffsetPerPeriod { get; init; } = Money.Zero;

    public string? Notes { get; init; }

    public void Validate()
    {
        if (Id == Guid.Empty)
        {
            throw new ArgumentException("A reimbursement offset needs an identifier.");
        }

        if (IncomeSourceId == Guid.Empty)
        {
            throw new ArgumentException("A reimbursement offset must point at an income source.");
        }

        if (OffsetPerPeriod.IsNegative)
        {
            throw new ArgumentException("A reimbursement offset cannot be negative.");
        }
    }
}

/// <summary>
/// One earner's income for a single pay period, broken down far enough to answer the two
/// questions the household actually has: what did this person genuinely bring in, and how much of
/// it is theirs to decide about.
/// </summary>
public sealed record EarnerIncome
{
    public required Guid MemberId { get; init; }

    public required string MemberName { get; init; }

    public required bool IsDiscretionaryEligible { get; init; }

    public required Frequency PayFrequency { get; init; }

    public Money GrossIncome { get; init; } = Money.Zero;

    public Money Taxes { get; init; } = Money.Zero;

    /// <summary>Pre-tax, retirement and post-tax deductions, excluding benefit premiums.</summary>
    public Money PayrollDeductions { get; init; } = Money.Zero;

    public Money BenefitDeductions { get; init; } = Money.Zero;

    public Money NetPay { get; init; } = Money.Zero;

    /// <summary>Non-taxable reimbursements received, before deciding what they offset.</summary>
    public Money Reimbursements { get; init; } = Money.Zero;

    /// <summary>The part of the reimbursement that repays a cost and is therefore not spendable.</summary>
    public Money ReimbursementsOffsettingCosts { get; init; } = Money.Zero;

    /// <summary>Obligations this person pays alone, before any shared household allocation.</summary>
    public Money IndividualObligations { get; init; } = Money.Zero;

    /// <summary>
    /// What remains once taxes, deductions, benefits, strictly individual obligations and
    /// offsetting reimbursements are removed. This is the figure shared costs are divided by.
    /// </summary>
    public Money UsableNetIncome { get; init; } = Money.Zero;

    /// <summary>True when the figures come from a projection rather than a recorded payslip.</summary>
    public bool IsEstimated { get; init; } = true;

    public bool HasActualPay { get; init; }

    public IReadOnlyList<string> MissingInformation { get; init; } = [];

    public string Basis => HasActualPay
        ? "Actual pay from a recorded payslip"
        : "Estimated from the income and payroll records";
}

public static class EarnerIncomeCalculator
{
    /// <summary>
    /// Builds a per-earner breakdown for every household member with active income.
    ///
    /// The tax arithmetic is not repeated here: it runs through the same
    /// <see cref="PayrollCalculator"/> and the same merged benefit deductions the dashboard uses,
    /// so a member's figures can never disagree with the household totals.
    /// </summary>
    public static IReadOnlyList<EarnerIncome> ForHousehold(
        BudgetDocument document,
        IncomeBasis basis,
        Frequency allocationFrequency,
        DateOnly today,
        PayPeriod? period = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        var estimate = basis.ToEstimate();

        return document.Members
            .Select(member => For(document, member, basis, estimate, allocationFrequency, today, period))
            .Where(income => income is not null)
            .Select(income => income!)
            .ToList();
    }

    private static EarnerIncome? For(
        BudgetDocument document,
        HouseholdMember member,
        IncomeBasis basis,
        IncomeEstimate estimate,
        Frequency allocationFrequency,
        DateOnly today,
        PayPeriod? period)
    {
        var sources = document.IncomeSources
            .Where(source => source.IsActive && source.MemberId == member.Id)
            .ToList();

        if (sources.Count == 0)
        {
            return null;
        }

        var missing = new List<string>();
        var taxable = sources.Where(source => source.IsTaxable).ToList();
        var reimbursementSources = sources.Where(source => !source.IsTaxable).ToList();

        var payFrequency = taxable.Count > 0
            ? taxable[0].PayFrequency
            : sources[0].PayFrequency;

        var taxableAnnual = Money.Sum(taxable.Select(source => source.GrossPerYear(estimate)));
        var reimbursementAnnual = Money.Sum(reimbursementSources.Select(source => source.GrossPerYear(estimate)));

        var grossPerPeriod = FrequencyConverter.FromAnnual(taxableAnnual, payFrequency);
        var reimbursementPerPeriod = FrequencyConverter.FromAnnual(reimbursementAnnual, payFrequency);

        var gross = grossPerPeriod;
        var taxes = Money.Zero;
        var payrollDeductions = Money.Zero;
        var benefitDeductions = Money.Zero;
        var net = grossPerPeriod + reimbursementPerPeriod;
        var estimated = true;

        var profile = document.PayrollProfiles.FirstOrDefault(item => item.MemberId == member.Id);

        if (taxable.Count > 0 && profile is null)
        {
            missing.Add(
                $"{member.Name} has taxable income but no payroll profile, so take-home pay is shown " +
                "before tax and will be too high.");
        }
        else if (profile is not null && taxable.Count > 0)
        {
            if (!TaxYearLibrary.TryGetYear(profile.TaxYear, out var table, out var taxWarning) || table is null)
            {
                missing.Add($"{member.Name}: {taxWarning}");
                table = null;
            }

            if (table is null)
            {
                // Leave gross as the pre-tax figure rather than inventing a later year's rates.
            }
            else
            {
            var merged = TakeHomeCalculator.MergeDeductions(
                profile,
                document.Benefits,
                member.Id,
                payFrequency,
                today);

            var result = PayrollCalculator.Calculate(profile.ToRequest(
                grossPerPeriod,
                payFrequency,
                table,
                reimbursementPerPeriod,
                Money.Zero) with
            { Deductions = merged });

            gross = result.GrossPay;
            taxes = result.TotalTax.Round();
            net = result.NetPay;

            benefitDeductions = Money.Sum(document.Benefits
                .Where(benefit => benefit.MemberId == member.Id)
                .Where(benefit => !benefit.IsConfirmed || benefit.AppliesOn(today))
                .Select(benefit => FrequencyConverter.Convert(
                    benefit.EmployeePremiumPerPeriod,
                    benefit.PremiumFrequency,
                    payFrequency)))
                .Round();

            foreach (var pending in document.Benefits.Where(benefit =>
                benefit.MemberId == member.Id && !benefit.IsConfirmed))
            {
                missing.Add(
                    $"{pending.Name} of {pending.EmployeePremiumPerPeriod.ToDisplayString()} per " +
                    $"{pending.PremiumFrequency.ToDisplayName()} is awaiting payslip confirmation " +
                    "and is included in this forecast scenario.");
            }

            payrollDeductions = Money.Max(
                Money.Zero,
                (result.PreTaxDeductions + result.RetirementContribution + result.PostTaxDeductions
                 - benefitDeductions).Round());
            }
        }

        var actual = basis == IncomeBasis.Actual
            ? FindActualPay(document, sources, period, today)
            : null;

        if (actual is { } payslip)
        {
            gross = payslip.GrossPay;
            taxes = payslip.TotalWithholding.Round();
            payrollDeductions = (payslip.PreTaxDeductions + payslip.PostTaxDeductions).Round();
            benefitDeductions = Money.Zero;
            reimbursementPerPeriod = payslip.NonTaxableReimbursements;
            net = payslip.NetPay;
            estimated = false;
        }
        else if (basis == IncomeBasis.Actual)
        {
            missing.Add(
                $"No payslip has been recorded for {member.Name} in this pay period, so a conservative " +
                "estimate is being used instead.");
        }

        if (profile is not null && !profile.W4IsComplete)
        {
            missing.Add(
                $"{member.Name}'s W-4 details and United States tax-residency treatment are incomplete.");
        }

        if (IsMissingHours(sources))
        {
            missing.Add($"{member.Name}'s expected weekly hours have not been entered.");
        }

        var offsetting = OffsettingReimbursements(document, reimbursementSources, reimbursementPerPeriod);
        var individual = IndividualObligations(document, member, payFrequency, missing);

        var usable = Money.Max(
            Money.Zero,
            (net - offsetting - individual).Round());

        // Everything is expressed in the household's allocation period so shares are comparable.
        Money ToAllocation(Money amount) => payFrequency == allocationFrequency
            ? amount.Round()
            : FrequencyConverter.Convert(amount, payFrequency, allocationFrequency).Round();

        return new EarnerIncome
        {
            MemberId = member.Id,
            MemberName = member.Name,
            IsDiscretionaryEligible = member.IsDiscretionaryEligible,
            PayFrequency = payFrequency,
            GrossIncome = ToAllocation(gross),
            Taxes = ToAllocation(taxes),
            PayrollDeductions = ToAllocation(payrollDeductions),
            BenefitDeductions = ToAllocation(benefitDeductions),
            NetPay = ToAllocation(net),
            Reimbursements = ToAllocation(reimbursementPerPeriod),
            ReimbursementsOffsettingCosts = ToAllocation(offsetting),
            IndividualObligations = ToAllocation(individual),
            UsableNetIncome = ToAllocation(usable),
            IsEstimated = estimated,
            HasActualPay = actual is not null,
            MissingInformation = missing.Distinct(StringComparer.Ordinal).ToList()
        };
    }

    private static Payslip? FindActualPay(
        BudgetDocument document,
        IReadOnlyList<IncomeSource> sources,
        PayPeriod? period,
        DateOnly today)
    {
        var sourceIds = sources.Select(source => source.Id).ToHashSet();

        return document.Payslips
            .Where(payslip => sourceIds.Contains(payslip.IncomeSourceId))
            .Where(payslip => period is null
                ? payslip.PayDate <= today
                : period.Contains(payslip.PayDate))
            .OrderByDescending(payslip => payslip.PayDate)
            .FirstOrDefault();
    }

    private static bool IsMissingHours(IReadOnlyList<IncomeSource> sources) =>
        sources.OfType<HourlyIncome>().Any(hourly =>
            hourly.WeeklyHours.Conservative == 0m
            && hourly.WeeklyHours.Normal == 0m
            && hourly.WeeklyHours.Optimistic == 0m);

    private static Money OffsettingReimbursements(
        BudgetDocument document,
        IReadOnlyList<IncomeSource> reimbursementSources,
        Money reimbursementPerPeriod)
    {
        if (reimbursementSources.Count == 0 || reimbursementPerPeriod.IsZero)
        {
            return Money.Zero;
        }

        var recorded = document.ReimbursementOffsets
            .Where(offset => reimbursementSources.Any(source => source.Id == offset.IncomeSourceId))
            .ToList();

        if (recorded.Count == 0)
        {
            // Nothing has been decided, so the whole amount is assumed to repay a cost.
            return reimbursementPerPeriod.Round();
        }

        var stated = Money.Sum(recorded.Select(offset => offset.OffsetPerPeriod)).Round();

        return Money.Min(stated, reimbursementPerPeriod.Round());
    }

    private static Money IndividualObligations(
        BudgetDocument document,
        HouseholdMember member,
        Frequency payFrequency,
        List<string> missing)
    {
        var total = Money.Zero;

        foreach (var expense in document.Expenses.Where(item => !item.IsPaused && !item.IsArchived))
        {
            if (BillAssignmentPlanner.IsUnassigned(expense)
                || expense.Assignment == BillAssignment.SharedAccount
                || !expense.Frequency.IsRecurring())
            {
                continue;
            }

            var converted = FrequencyConverter.Convert(expense.ExpectedAmount, expense.Frequency, payFrequency);
            var shares = BillAssignmentPlanner.Shares(expense, converted);
            if (shares.TryGetValue(member.Id, out var share))
            {
                total += share;
            }
            else if (expense.Assignment == BillAssignment.MemberPaysAll
                     && expense.Ownership == Ownership.Individual
                     && (expense.Split?.Participants.Count ?? 0) == 0)
            {
                missing.Add(
                    $"\"{expense.Name}\" is marked as an individual cost but nobody is recorded as " +
                    "responsible for it, so it remains unassigned.");
            }
        }

        foreach (var debt in document.Debts.Where(debt => debt.OwnerMemberId == member.Id))
        {
            total += FrequencyConverter.Convert(debt.MinimumPayment, Frequency.Monthly, payFrequency);
        }

        return total.Round();
    }
}
