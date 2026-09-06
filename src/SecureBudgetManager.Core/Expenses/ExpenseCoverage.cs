using SecureBudgetManager.Core.Models;

namespace SecureBudgetManager.Core.Expenses;

/// <summary>
/// Why a known cost creates no household outflow. The record stays in the catalogue so the
/// household can explain it and change it later.
/// </summary>
public enum HouseholdCostCoverage
{
    HouseholdPays = 0,
    IncludedInAnotherPayment = 1,
    EmployerCovered = 2,
    Reimbursed = 3,
    NotApplicable = 4,
    Inactive = 5
}

public enum SuggestionAmountKind
{
    ActualFixed = 0,
    EstimatedFixed = 1,
    GuidanceBased = 2,
    IncomeBased = 3,
    UserDefined = 4,
    Unknown = 5,
    IncludedElsewhere = 6,
    EmployerCovered = 7,
    NotApplicable = 8
}

public static class HouseholdCostCoverageExtensions
{
    public static string ToDisplayName(this HouseholdCostCoverage coverage) => coverage switch
    {
        HouseholdCostCoverage.HouseholdPays => "Household pays",
        HouseholdCostCoverage.IncludedInAnotherPayment => "Included in another payment",
        HouseholdCostCoverage.EmployerCovered => "Employer-covered",
        HouseholdCostCoverage.Reimbursed => "Reimbursed",
        HouseholdCostCoverage.NotApplicable => "Not applicable",
        HouseholdCostCoverage.Inactive => "Inactive",
        _ => throw new ArgumentOutOfRangeException(nameof(coverage), coverage, "Unknown coverage.")
    };

    public static bool CreatesHouseholdOutflow(this HouseholdCostCoverage coverage) =>
        coverage == HouseholdCostCoverage.HouseholdPays;
}

public static class SuggestionAmountKindExtensions
{
    public static string ToDisplayName(this SuggestionAmountKind kind) => kind switch
    {
        SuggestionAmountKind.ActualFixed => "Actual fixed amount",
        SuggestionAmountKind.EstimatedFixed => "Estimated fixed amount",
        SuggestionAmountKind.GuidanceBased => "Guidance-based suggestion",
        SuggestionAmountKind.IncomeBased => "Income-based suggestion",
        SuggestionAmountKind.UserDefined => "User-defined amount",
        SuggestionAmountKind.Unknown => "Unknown",
        SuggestionAmountKind.IncludedElsewhere => "Included elsewhere",
        SuggestionAmountKind.EmployerCovered => "Employer-covered",
        SuggestionAmountKind.NotApplicable => "Not applicable",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown suggestion kind.")
    };
}

/// <summary>
/// Resolves whether an expense currently creates a household cash outflow.
/// Confirmed coverage always wins. Unconfirmed parking and work-toll catalogue names stay
/// conceptual zeros until someone records a separately charged version.
/// </summary>
public static class ExpenseCoverage
{
    public static HouseholdCostCoverage Effective(ExpenseItem expense)
    {
        ArgumentNullException.ThrowIfNull(expense);

        if (expense.CoverageConfirmed)
        {
            return expense.Coverage;
        }

        if (LooksLikeIncludedParking(expense.Name))
        {
            return HouseholdCostCoverage.IncludedInAnotherPayment;
        }

        if (LooksLikeEmployerTolls(expense.Name))
        {
            return HouseholdCostCoverage.EmployerCovered;
        }

        return expense.Coverage;
    }

    public static bool CreatesHouseholdOutflow(ExpenseItem expense) =>
        Effective(expense).CreatesHouseholdOutflow();

    public static bool AppearsAsOutstanding(ExpenseItem expense) =>
        CreatesHouseholdOutflow(expense) && !expense.IsPaused && !expense.IsArchived;

    public static string Explanation(ExpenseItem expense)
    {
        ArgumentNullException.ThrowIfNull(expense);

        if (!string.IsNullOrWhiteSpace(expense.CoveredByExplanation))
        {
            return expense.CoveredByExplanation.Trim();
        }

        return Effective(expense) switch
        {
            HouseholdCostCoverage.IncludedInAnotherPayment => "Included in rent",
            HouseholdCostCoverage.EmployerCovered => "Employer-covered",
            HouseholdCostCoverage.Reimbursed => "Reimbursed",
            HouseholdCostCoverage.NotApplicable => "Not applicable",
            HouseholdCostCoverage.Inactive => "Inactive",
            _ => string.Empty
        };
    }

    public static Money HouseholdResponsibility(ExpenseItem expense) =>
        CreatesHouseholdOutflow(expense) ? expense.ExpectedAmount : Money.Zero;

    public static bool LooksLikeIncludedParking(string name)
    {
        var trimmed = name.Trim();
        return trimmed.Equals("Parking", StringComparison.OrdinalIgnoreCase)
               || trimmed.Equals("Parking (rent)", StringComparison.OrdinalIgnoreCase);
    }

    public static bool LooksLikeEmployerTolls(string name)
    {
        var trimmed = name.Trim();
        return trimmed.Equals("Tolls", StringComparison.OrdinalIgnoreCase)
               || trimmed.Equals("Work tolls", StringComparison.OrdinalIgnoreCase)
               || trimmed.Equals("Tolls (work)", StringComparison.OrdinalIgnoreCase);
    }
}
