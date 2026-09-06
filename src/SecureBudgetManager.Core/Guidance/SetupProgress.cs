using SecureBudgetManager.Core.Expenses;
using SecureBudgetManager.Core.Household;
using SecureBudgetManager.Core.Income;
using SecureBudgetManager.Core.Storage;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.Core.Guidance;

public enum SetupStepKind
{
    Household = 1,
    Income = 2,
    Payroll = 3,
    Benefits = 4,
    Expenses = 5,
    GroceryPlan = 6,
    Debt = 7,
    Savings = 8,
    AllocationRules = 9,
    BillsAndReservations = 10
}

public enum SetupStepStatus
{
    Complete = 0,
    Incomplete = 1,
    Optional = 2,
    NeedsReview = 3
}

public sealed record SetupStepRow(
    SetupStepKind Kind,
    int Number,
    string Title,
    SetupStepStatus Status,
    string WhyNeeded,
    string Unlocks,
    string Prerequisite,
    string ContinueLabel);

/// <summary>
/// Dependency-ordered first-run progress. Later pages stay reachable; missing information is
/// explained rather than invented.
/// </summary>
public static class SetupProgress
{
    public static IReadOnlyList<SetupStepRow> Steps(BudgetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return
        [
            Step(
                SetupStepKind.Household,
                1,
                "Household",
                HouseholdStatus(document),
                "Names and who lives here are needed before income, bills or personal allocations can be assigned.",
                "Income, payroll and personal bill assignment become usable.",
                string.Empty,
                "Complete household"),
            Step(
                SetupStepKind.Income,
                2,
                "Income",
                IncomeStatus(document),
                "At least one active income is needed for safe-to-spend and salary-based suggestions.",
                "Payroll, grocery suggestions and This Week income timing become usable.",
                document.Members.Count == 0 ? "Add household members first." : string.Empty,
                "Add income"),
            Step(
                SetupStepKind.Payroll,
                3,
                "Payroll",
                PayrollStatus(document),
                "Taxes and retirement deductions change net income used to protect essentials.",
                "Net income and conservative safe-to-spend become more accurate.",
                ActiveIncome(document).Count == 0 ? "Add income first." : string.Empty,
                "Review payroll"),
            Step(
                SetupStepKind.Benefits,
                4,
                "Benefits",
                BenefitsStatus(document),
                "Benefit premiums are deducted from the same pay as wages and must not be guessed.",
                "Net income after benefits can be shown separately for each person.",
                ActiveIncome(document).Count == 0 ? "Add income first." : string.Empty,
                "Review benefits"),
            Step(
                SetupStepKind.Expenses,
                5,
                "Expenses",
                ExpenseStatus(document),
                "Recurring costs and covered items are required before reservations and This Week can be complete.",
                "Bills, covered-cost treatment and suggested starting amounts become usable.",
                document.Members.Count == 0 ? "Add household members first." : string.Empty,
                "Add expenses"),
            Step(
                SetupStepKind.GroceryPlan,
                6,
                "Grocery Plan",
                GroceryStatus(document),
                "Essential food is planned by category. Household composition and an income baseline make salary-based suggestions useful.",
                "Bills & Reservations can include grocery cash still required.",
                MissingGroceryPrerequisites(document),
                "Set grocery plan"),
            Step(
                SetupStepKind.Debt,
                7,
                "Debt",
                DebtStatus(document),
                "Minimum payments are safety-tier obligations and must use real amounts.",
                "Bills & Reservations can reserve debt minimums.",
                string.Empty,
                "Review debt"),
            Step(
                SetupStepKind.Savings,
                8,
                "Savings",
                SavingsStatus(document),
                "A safety buffer and required sinking funds protect upcoming bills.",
                "This Week can keep reserved money out of safe-to-spend.",
                string.Empty,
                "Review savings"),
            Step(
                SetupStepKind.AllocationRules,
                9,
                "Allocation Rules",
                AllocationStatus(document),
                "These rules decide how a paycheque is divided without pooling income automatically.",
                "Bills can be assigned without silently charging one person.",
                ActiveIncome(document).Count == 0 ? "Add income first." : string.Empty,
                "Review allocation rules"),
            Step(
                SetupStepKind.BillsAndReservations,
                10,
                "Bills & Reservations",
                BillsStatus(document),
                "Due dates and assignments turn expenses into a usable weekly plan.",
                "This Week can show bills requiring attention and unassigned status.",
                MissingBillPrerequisites(document),
                "Assign bills")
        ];
    }

    public static SetupStepKind? NextIncompleteStep(BudgetDocument document) =>
        Steps(document)
            .Where(step => step.Status is SetupStepStatus.Incomplete or SetupStepStatus.NeedsReview)
            .Select(step => (SetupStepKind?)step.Kind)
            .FirstOrDefault();

    public static bool IsMinimumOperational(BudgetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return HouseholdStatus(document) == SetupStepStatus.Complete
               && IncomeStatus(document) == SetupStepStatus.Complete
               && HasConfirmedBalance(document)
               && ExpenseStatus(document) is SetupStepStatus.Complete or SetupStepStatus.NeedsReview
               && GroceryStatus(document) is SetupStepStatus.Complete or SetupStepStatus.NeedsReview
               && DebtStatus(document) is not SetupStepStatus.Incomplete
               && SavingsStatus(document) is not SetupStepStatus.Incomplete
               && BillsStatus(document) is not SetupStepStatus.Incomplete;
    }

    public static string Describe(BudgetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (IsMinimumOperational(document))
        {
            return "Minimum operational setup is complete. This Week can show available money, next income and safe-to-spend.";
        }

        var next = Steps(document).FirstOrDefault(step =>
            step.Status is SetupStepStatus.Incomplete or SetupStepStatus.NeedsReview);
        if (next is null)
        {
            return "Setup is complete enough to use This Week.";
        }

        return $"Next: {next.Title}. {next.WhyNeeded}";
    }

    public static bool HasConfirmedBalance(BudgetDocument document) =>
        document.Accounts.Count > 0;

    private static SetupStepRow Step(
        SetupStepKind kind,
        int number,
        string title,
        SetupStepStatus status,
        string why,
        string unlocks,
        string prerequisite,
        string continueLabel) =>
        new(kind, number, title, status, why, unlocks, prerequisite, continueLabel);

    private static IReadOnlyList<IncomeSource> ActiveIncome(BudgetDocument document) =>
        document.IncomeSources.Where(source => source.IsActive).ToList();

    private static SetupStepStatus HouseholdStatus(BudgetDocument document) =>
        document.Members.Any(member => !member.IsArchived)
            ? SetupStepStatus.Complete
            : SetupStepStatus.Incomplete;

    private static SetupStepStatus IncomeStatus(BudgetDocument document) =>
        ActiveIncome(document).Count > 0 ? SetupStepStatus.Complete : SetupStepStatus.Incomplete;

    private static SetupStepStatus PayrollStatus(BudgetDocument document)
    {
        if (ActiveIncome(document).Count == 0)
        {
            return SetupStepStatus.Incomplete;
        }

        return document.PayrollProfiles.Count > 0
            ? SetupStepStatus.Complete
            : SetupStepStatus.NeedsReview;
    }

    private static SetupStepStatus BenefitsStatus(BudgetDocument document)
    {
        if (ActiveIncome(document).Count == 0)
        {
            return SetupStepStatus.Incomplete;
        }

        return document.Benefits.Count > 0 ? SetupStepStatus.Complete : SetupStepStatus.Optional;
    }

    private static SetupStepStatus ExpenseStatus(BudgetDocument document)
    {
        var essentials = document.Expenses
            .Where(expense => expense.Necessity == ExpenseNecessity.Essential && !expense.IsArchived)
            .ToList();
        return essentials.Count == 0 ? SetupStepStatus.Incomplete : SetupStepStatus.Complete;
    }

    private static SetupStepStatus GroceryStatus(BudgetDocument document)
    {
        var current = document.GroceryPlanOf(GroceryPlanKind.Current);
        if (current is null)
        {
            return SetupStepStatus.Incomplete;
        }

        return current.Categories.Any(category => category.IsEssential)
            ? SetupStepStatus.Complete
            : SetupStepStatus.NeedsReview;
    }

    private static SetupStepStatus DebtStatus(BudgetDocument document)
    {
        if (document.Debts.Count == 0)
        {
            return SetupStepStatus.Optional;
        }

        return document.Debts.All(debt => !debt.MinimumPayment.IsNegative)
            ? SetupStepStatus.Complete
            : SetupStepStatus.Incomplete;
    }

    private static SetupStepStatus SavingsStatus(BudgetDocument document)
    {
        if (!document.Preferences.MinimumBreathingRoom.IsZero
            || !document.Preferences.MinimumBalanceReserve.IsZero
            || document.Funds.Count > 0)
        {
            return SetupStepStatus.Complete;
        }

        return SetupStepStatus.Incomplete;
    }

    private static SetupStepStatus AllocationStatus(BudgetDocument document) =>
        ActiveIncome(document).Count == 0 ? SetupStepStatus.Incomplete : SetupStepStatus.Complete;

    private static SetupStepStatus BillsStatus(BudgetDocument document)
    {
        var outgoing = document.Expenses
            .Where(expense =>
                !expense.IsArchived
                && expense.Necessity == ExpenseNecessity.Essential
                && ExpenseCoverage.CreatesHouseholdOutflow(expense))
            .ToList();
        if (outgoing.Count == 0)
        {
            return SetupStepStatus.Incomplete;
        }

        var missingDates = outgoing.Any(expense =>
            expense.DueDateUnknown && expense.Frequency.IsRecurring());
        var unassigned = outgoing.Any(expense => expense.Assignment == BillAssignment.Unassigned);
        return missingDates || unassigned ? SetupStepStatus.NeedsReview : SetupStepStatus.Complete;
    }

    private static string MissingGroceryPrerequisites(BudgetDocument document)
    {
        var missing = new List<string>();
        if (document.Members.Count == 0)
        {
            missing.Add("Complete household");
        }

        if (ActiveIncome(document).Count == 0)
        {
            missing.Add("Add income");
        }

        return missing.Count == 0
            ? string.Empty
            : "Useful salary-based suggestions need: " + string.Join(" and ", missing) + ".";
    }

    private static string MissingBillPrerequisites(BudgetDocument document)
    {
        var missing = new List<string>();
        if (ExpenseStatus(document) == SetupStepStatus.Incomplete)
        {
            missing.Add("essential expenses");
        }

        if (GroceryStatus(document) == SetupStepStatus.Incomplete)
        {
            missing.Add("a grocery plan");
        }

        if (document.Debts.Count > 0 && DebtStatus(document) == SetupStepStatus.Incomplete)
        {
            missing.Add("debt minimums");
        }

        return missing.Count == 0
            ? string.Empty
            : "A complete result needs " + string.Join(", ", missing) + ".";
    }
}
