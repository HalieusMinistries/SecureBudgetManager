using SecureBudgetManager.Core.Benefits;
using SecureBudgetManager.Core.Debt;
using SecureBudgetManager.Core.Expenses;
using SecureBudgetManager.Core.Goals;
using SecureBudgetManager.Core.Guidance;
using SecureBudgetManager.Core.Household;
using SecureBudgetManager.Core.Income;
using SecureBudgetManager.Core.International;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Planning;
using SecureBudgetManager.Core.Products;
using SecureBudgetManager.Core.Savings;
using SecureBudgetManager.Core.Tax;

namespace SecureBudgetManager.Core.Storage;

/// <summary>
/// The whole household dataset in memory.
///
/// A household budget is small — hundreds of rows, not millions — so the application loads it
/// once and saves it as a single transaction. That removes a large class of partial-write bugs:
/// either the whole change is on disk or none of it is.
/// </summary>
public sealed record BudgetDocument
{
    public string HouseholdName { get; init; } = "Our household";

    public HouseholdPreferences Preferences { get; init; } = new();

    public IReadOnlyList<HouseholdMember> Members { get; init; } = [];

    public IReadOnlyList<BankAccount> Accounts { get; init; } = [];

    public IReadOnlyList<IncomeSource> IncomeSources { get; init; } = [];

    public IReadOnlyList<Payslip> Payslips { get; init; } = [];

    public IReadOnlyList<PayrollProfile> PayrollProfiles { get; init; } = [];

    public IReadOnlyList<ExpenseItem> Expenses { get; init; } = [];

    public IReadOnlyList<ExpenseTransaction> Transactions { get; init; } = [];

    public IReadOnlyList<BenefitPlan> Benefits { get; init; } = [];

    public IReadOnlyList<DebtAccount> Debts { get; init; } = [];

    public IReadOnlyList<SavingsFund> Funds { get; init; } = [];

    public IReadOnlyList<Goal> Goals { get; init; } = [];

    public IReadOnlyList<Scenario> Scenarios { get; init; } = [];

    /// <summary>The household's agreed rules for turning a paycheque into allocations.</summary>
    public AllocationRules Rules { get; init; } = new();

    /// <summary>Which spending categories sit at which level of the household needs hierarchy.</summary>
    public NeedsHierarchy Hierarchy { get; init; } = NeedsHierarchy.Default;

    /// <summary>The current, fallback and emergency-minimum grocery plans.</summary>
    public IReadOnlyList<GroceryPlan> GroceryPlans { get; init; } = [];

    /// <summary>Local cost references, each with its own source and review date.</summary>
    public IReadOnlyList<CostGuidanceRecord> CostGuidance { get; init; } = [];

    /// <summary>Money already set aside towards named future obligations.</summary>
    public IReadOnlyList<ObligationReserve> Reserves { get; init; } = [];

    /// <summary>Deliberate movements between two people's personal balances.</summary>
    public IReadOnlyList<PersonalTransfer> Transfers { get; init; } = [];

    /// <summary>Which costs a reimbursement is intended to repay.</summary>
    public IReadOnlyList<ReimbursementOffset> ReimbursementOffsets { get; init; } = [];

    /// <summary>Where the household lives, used to match local cost guidance.</summary>
    public CostLocality Locality { get; init; } = CostLocality.UnitedStates;

    public IReadOnlyList<Product> Products { get; init; } = [];

    public IReadOnlyList<PriceObservation> PriceObservations { get; init; } = [];

    public IReadOnlyList<SalesTaxRule> SalesTaxRules { get; init; } = [];

    public IReadOnlyList<ExchangeRateQuote> ExchangeRates { get; init; } = [];

    public IReadOnlyList<InternationalTransfer> InternationalTransfers { get; init; } = [];

    public IReadOnlyList<RecurringSupportCommitment> SupportCommitments { get; init; } = [];

    public IReadOnlyList<ForeignAccount> ForeignAccounts { get; init; } = [];

    public IReadOnlyList<SupportingDocumentRef> SupportingDocuments { get; init; } = [];

    public string? ImportedGuidancePackVersion { get; init; }

    /// <summary>
    /// Household questions that must remain visible after records exist. These are never treated
    /// as answered merely because a related row was created.
    /// </summary>
    public IReadOnlyList<string> OutstandingQuestions { get; init; } = [];

    public GroceryPlan? GroceryPlanOf(GroceryPlanKind kind) =>
        GroceryPlans.FirstOrDefault(plan => plan.Kind == kind);

    /// <summary>Members the household has explicitly marked as eligible for a personal allowance.</summary>
    public IReadOnlyList<HouseholdMember> DiscretionaryEligibleMembers =>
        Members.Where(member => member.IsDiscretionaryEligible && !member.IsArchived).ToList();

    public HouseholdComposition Composition => new(
        Members.Count(member => !member.IsDependant && !member.IsArchived),
        Members.Count(member => member.IsDependant && !member.IsArchived));

    public bool IsEmpty =>
        Members.Count == 0 && IncomeSources.Count == 0 && Expenses.Count == 0;

    public Core.Household.Household ToHousehold() => new()
    {
        Name = HouseholdName,
        Members = Members,
        Preferences = Preferences
    };

    public BankAccount? PrimaryAccount =>
        Accounts.FirstOrDefault(account => account.IsPrimary) ?? Accounts.FirstOrDefault();

    public Money TotalBalance => Money.Sum(Accounts.Select(account => account.CurrentBalance));

    public Money MonthlyEssentialSpending => Money.Sum(Expenses
        .Where(expense => !expense.IsPaused && !expense.IsArchived && expense.Necessity == ExpenseNecessity.Essential)
        .Select(expense => expense.MonthlyCost)).Round();

    public Money MonthlyOptionalSpending => Money.Sum(Expenses
        .Where(expense => !expense.IsPaused && !expense.IsArchived && expense.Necessity == ExpenseNecessity.Optional)
        .Select(expense => expense.MonthlyCost)).Round();

    public Money MonthlyDebtPayments => Money.Sum(Debts.Select(debt => debt.MinimumPayment)).Round();

    public SavingsFund? EmergencyFund =>
        Funds.FirstOrDefault(fund => fund.Purpose == FundPurpose.EmergencyFund);

    public HouseholdMember? FindMember(Guid id) =>
        Members.FirstOrDefault(member => member.Id == id);

    public string MemberName(Guid? id) => id is null
        ? "Household"
        : FindMember(id.Value)?.Name ?? "Unknown";

    /// <summary>
    /// Validates the whole document. Called before every save so an inconsistent dataset never
    /// reaches disk.
    /// </summary>
    public void Validate()
    {
        Preferences.Validate();

        foreach (var member in Members)
        {
            member.Validate();
        }

        if (Members.Select(member => member.Id).Distinct().Count() != Members.Count)
        {
            throw new ArgumentException("Household members must have distinct identifiers.");
        }

        var memberIds = Members.Select(member => member.Id).ToHashSet();

        foreach (var account in Accounts)
        {
            account.Validate();
            RequireKnownMember(account.OwnerMemberId, memberIds, $"Account \"{account.Name}\"");
        }

        foreach (var source in IncomeSources)
        {
            source.Validate();

            if (!memberIds.Contains(source.MemberId))
            {
                throw new ArgumentException(
                    $"Income source \"{source.Name}\" refers to a household member that no longer exists.");
            }
        }

        var sourceIds = IncomeSources.Select(source => source.Id).ToHashSet();

        foreach (var payslip in Payslips)
        {
            payslip.Validate();

            if (!sourceIds.Contains(payslip.IncomeSourceId))
            {
                throw new ArgumentException("A payslip refers to an income source that no longer exists.");
            }
        }

        // Two payslips for the same source on the same date would double-count income.
        var duplicatePayslip = Payslips
            .GroupBy(payslip => (payslip.IncomeSourceId, payslip.PayDate))
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicatePayslip is not null)
        {
            throw new ArgumentException(
                $"There is more than one payslip dated {duplicatePayslip.Key.PayDate:yyyy-MM-dd} " +
                "for the same income source.");
        }

        foreach (var profile in PayrollProfiles)
        {
            profile.Validate();
            RequireKnownMember(profile.MemberId, memberIds, "A payroll profile");
        }

        foreach (var expense in Expenses)
        {
            expense.Validate();

            if (expense.Split is { } split)
            {
                foreach (var participant in split.Participants)
                {
                    RequireKnownMember(participant, memberIds, $"Expense \"{expense.Name}\"");
                }
            }
        }

        var expenseIds = Expenses.Select(expense => expense.Id).ToHashSet();

        foreach (var transaction in Transactions)
        {
            transaction.Validate();

            if (transaction.ExpenseItemId is { } itemId && !expenseIds.Contains(itemId))
            {
                throw new ArgumentException(
                    $"Transaction \"{transaction.Description}\" refers to an expense that no longer exists.");
            }
        }

        foreach (var benefit in Benefits)
        {
            benefit.Validate();
            RequireKnownMember(benefit.MemberId, memberIds, $"Benefit \"{benefit.Name}\"");
        }

        foreach (var debt in Debts)
        {
            debt.Validate();
            RequireKnownMember(debt.OwnerMemberId, memberIds, $"Debt \"{debt.Name}\"");
        }

        foreach (var fund in Funds)
        {
            fund.Validate();
            RequireKnownMember(fund.OwnerMemberId, memberIds, $"Fund \"{fund.Name}\"");
        }

        foreach (var goal in Goals)
        {
            goal.Validate();
            RequireKnownMember(goal.OwnerMemberId, memberIds, $"Goal \"{goal.Name}\"");
        }

        foreach (var scenario in Scenarios)
        {
            scenario.Validate();
        }

        Rules.Validate();
        Hierarchy.Validate();

        foreach (var memberId in Rules.CustomDiscretionaryPercents.Keys
                     .Concat(Rules.SharedContributionOverrides.Keys))
        {
            RequireKnownMember(memberId, memberIds, "An allocation rule");
        }

        RequireKnownMember(Rules.RoundingRemainderMemberId, memberIds, "The rounding remainder setting");

        foreach (var plan in GroceryPlans)
        {
            plan.Validate();
        }

        var duplicatePlan = GroceryPlans
            .GroupBy(plan => plan.Kind)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicatePlan is not null)
        {
            throw new ArgumentException(
                $"There is more than one {duplicatePlan.Key.ToDisplayName().ToLowerInvariant()}.");
        }

        foreach (var record in CostGuidance)
        {
            record.Validate();
        }

        foreach (var reserve in Reserves)
        {
            reserve.Validate();
        }

        var duplicateReserve = Reserves
            .GroupBy(reserve => reserve.ObligationId)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateReserve is not null)
        {
            throw new ArgumentException("The same obligation has more than one reserve recorded against it.");
        }

        foreach (var transfer in Transfers)
        {
            transfer.Validate();
            RequireKnownMember(transfer.FromMemberId, memberIds, "A transfer");
            RequireKnownMember(transfer.ToMemberId, memberIds, "A transfer");
        }

        foreach (var offset in ReimbursementOffsets)
        {
            offset.Validate();

            if (!sourceIds.Contains(offset.IncomeSourceId))
            {
                throw new ArgumentException(
                    "A reimbursement offset refers to an income source that no longer exists.");
            }
        }

        foreach (var transaction in Transactions)
        {
            RequireKnownMember(transaction.SpentByMemberId, memberIds, "A transaction");
        }

        foreach (var product in Products)
        {
            product.Validate();
        }

        foreach (var observation in PriceObservations)
        {
            observation.Validate();
        }

        foreach (var rule in SalesTaxRules)
        {
            rule.Validate();
        }

        foreach (var quote in ExchangeRates)
        {
            quote.Validate();
        }

        foreach (var transfer in InternationalTransfers)
        {
            transfer.Validate();
            RequireKnownMember(transfer.SendingAccountOwnerMemberId, memberIds, "An international transfer");
            RequireKnownMember(transfer.ReceivingAccountOwnerMemberId, memberIds, "An international transfer");
        }

        foreach (var commitment in SupportCommitments)
        {
            commitment.Validate();
        }

        foreach (var account in ForeignAccounts)
        {
            account.Validate();
            RequireKnownMember(account.OwnerMemberId, memberIds, "A foreign account");
        }

        foreach (var documentRef in SupportingDocuments)
        {
            documentRef.Validate();
        }
    }

    private static void RequireKnownMember(Guid? memberId, HashSet<Guid> knownIds, string subject)
    {
        if (memberId is { } id && id != Guid.Empty && !knownIds.Contains(id))
        {
            throw new ArgumentException($"{subject} refers to a household member that no longer exists.");
        }
    }
}
