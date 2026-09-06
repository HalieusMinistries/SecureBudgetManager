namespace SecureBudgetManager.Core.Help;

public sealed record GlossaryEntry(string Term, string Explanation, string Category);

/// <summary>
/// Neutral, educational definitions for American household-finance terms. This is not legal,
/// tax, immigration or investment advice.
/// </summary>
public static class FinanceGlossary
{
    public static IReadOnlyList<GlossaryEntry> All { get; } =
    [
        new("Gross pay", "The amount earned before tax and deductions are taken out.", "Pay"),
        new("Net pay", "The amount left after tax and deductions. This is what usually arrives in the bank.", "Pay"),
        new("Withholding", "Tax the employer sends to the government from each payslip, based on the W-4 and local rules.", "Tax"),
        new("W-4", "The federal form that tells an employer how to estimate income-tax withholding. It is not a tax return.", "Tax"),
        new("W-2", "The year-end statement of wages and withholding. It is issued after the tax year, not used to plan next week's spending.", "Tax"),
        new("Resident and non-resident-alien payroll treatment", "US payroll can apply different withholding rules. This programme uses only the treatment the household records. It does not decide immigration or tax residency.", "Tax"),
        new("Social Security", "A federal payroll tax on wages, with an annual wage cap that changes by tax year.", "Tax"),
        new("Medicare", "A federal payroll tax on wages. Additional Medicare tax can apply above a yearly threshold.", "Tax"),
        new("Utah withholding", "Utah uses versioned Publication 14 schedules, not a single permanent percentage.", "Tax"),
        new("Pre-tax deduction", "An amount taken from pay before income tax is calculated, such as many 401(k) contributions or health premiums.", "Payroll"),
        new("Post-tax deduction", "An amount taken after tax, such as a Roth contribution or a repayment.", "Payroll"),
        new("Reimbursement", "Money paid back for a cost already incurred. It is not wages and is not treated as ordinary disposable income.", "Pay"),
        new("Weekly versus fortnightly versus semi-monthly", "Weekly is 52 pays a year, fortnightly is 26, and semi-monthly (twice monthly) is 24. A month is never treated as four weeks.", "Time"),
        new("Average monthly amount", "A recurring amount converted through the annual total (for example weekly × 52 ÷ 12).", "Time"),
        new("Bill reservation", "Money set aside from this paycheque for a bill that is not due yet, so later weeks are not left short.", "Allocation"),
        new("Envelope", "A named pot of money for one purpose, such as groceries or a personal allowance.", "Allocation"),
        new("Sinking fund", "Savings built in advance for a known future cost, such as tyres or an insurance renewal.", "Savings"),
        new("Emergency fund", "Protected savings for unexpected essential costs. The planning default is a number of months of essentials, not leftover cash.", "Savings"),
        new("Safety buffer", "A minimum bank balance the household chooses not to spend.", "Allocation"),
        new("Safe-to-spend", "What remains after bills, reservations, essentials, protected savings and the safety buffer. Overspending here harms a later need.", "Allocation"),
        new("Essential versus discretionary", "Essentials keep the household housed, fed, safe and able to work. Discretionary spending is optional and is reduced first in a shortfall.", "Needs"),
        new("APR", "The yearly interest rate on a debt. 0% means no interest is charged while that rate applies.", "Debt"),
        new("Minimum payment", "The smallest payment the lender requires. Paying only this usually takes longer and can cost more interest.", "Debt"),
        new("Employer match", "Money the employer adds to a retirement plan when the employee contributes. It is not take-home pay.", "Payroll"),
        new("Vesting", "How long a person must stay before employer retirement contributions become theirs to keep.", "Payroll"),
        new("Exchange rate", "How many units of one currency equal one unit of another, at a stated time and source. Rates are never permanently hard-coded.", "Transfers"),
        new("Transfer fee", "What a provider, bank or intermediary charges to send money. Fees are recorded separately from the amount received.", "Transfers"),
        new("Current versus fallback grocery plan", "The current plan includes assistance that is available now. The fallback plan is what the household would need if that assistance stopped.", "Groceries"),
        new("Guidance source and confidence", "Every suggested figure names its source, effective date and how sure it is. Derived estimates are labelled as derived.", "Guidance"),
        new("Estimated versus actual figures", "Estimates are plans. Actuals are what happened. They are compared, never silently replaced.", "Actuals")
    ];
}
