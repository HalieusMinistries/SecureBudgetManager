using SecureBudgetManager.Core.Models;

namespace SecureBudgetManager.Core.Planning;

public sealed record LoanTerms
{
    public required Money Principal { get; init; }

    /// <summary>Annual percentage rate, as a percentage (for example 9.5 for 9.5%).</summary>
    public required decimal AnnualPercentageRate { get; init; }

    public required int TermMonths { get; init; }

    public void Validate()
    {
        if (Principal.IsNegative)
        {
            throw new ArgumentException("A loan principal cannot be negative.");
        }

        if (AnnualPercentageRate < 0m)
        {
            throw new ArgumentException("An APR cannot be negative.");
        }

        if (TermMonths <= 0)
        {
            throw new ArgumentException("A loan term must be at least one month.");
        }
    }
}

public sealed record LoanSchedule
{
    public required Money MonthlyPayment { get; init; }

    public required Money TotalPaid { get; init; }

    public required Money TotalInterest { get; init; }

    public required int TermMonths { get; init; }

    public required IReadOnlyList<LoanPeriod> Periods { get; init; }

    /// <summary>
    /// False when the payment never clears the balance, which happens whenever the payment does
    /// not cover the monthly interest. Callers must check this before quoting a term.
    /// </summary>
    public bool IsFullyRepaid { get; init; } = true;

    /// <summary>Outstanding balance after a number of months have been paid.</summary>
    public Money BalanceAfter(int monthsPaid)
    {
        if (monthsPaid <= 0)
        {
            return Periods.Count == 0 ? Money.Zero : Periods[0].OpeningBalance;
        }

        return monthsPaid >= Periods.Count ? Money.Zero : Periods[monthsPaid - 1].ClosingBalance;
    }
}

public sealed record LoanPeriod(
    int MonthNumber,
    Money OpeningBalance,
    Money Payment,
    Money Interest,
    Money Principal,
    Money ClosingBalance);

/// <summary>
/// Amortisation using the standard annuity formula, computed in decimal so a long schedule does
/// not drift by cents.
/// </summary>
public static class LoanMath
{
    public static LoanSchedule BuildSchedule(LoanTerms terms, Money extraMonthlyPayment)
    {
        ArgumentNullException.ThrowIfNull(terms);
        terms.Validate();

        if (extraMonthlyPayment.IsNegative)
        {
            throw new ArgumentException("An extra payment cannot be negative.", nameof(extraMonthlyPayment));
        }

        var scheduledPayment = MonthlyPayment(terms);
        var payment = scheduledPayment + extraMonthlyPayment;
        var monthlyRate = terms.AnnualPercentageRate / 100m / 12m;

        var periods = new List<LoanPeriod>();
        var balance = terms.Principal;
        var totalInterest = Money.Zero;
        var totalPaid = Money.Zero;
        var month = 0;

        // Guard against a payment too small to ever clear the balance.
        var maximumMonths = terms.TermMonths * 4 + 12;

        while (balance > Money.Zero && month < maximumMonths)
        {
            month++;

            var interest = (balance * monthlyRate).Round();
            var payoffAmount = (balance + interest).Round();
            var thisPayment = Money.Min(payment, payoffAmount);

            // The rounded payment leaves a few cents outstanding after the final month. Lenders
            // absorb that in the last payment; without this the schedule grows a phantom extra
            // period and reports a 61-month term on a 60-month loan.
            if (month == terms.TermMonths && payoffAmount - thisPayment < payment)
            {
                thisPayment = payoffAmount;
            }

            var principalPaid = (thisPayment - interest).Round();

            if (principalPaid <= Money.Zero && month > 1)
            {
                // The payment does not cover the interest, so the balance will never fall.
                // Stop and report it rather than looping or quoting a fictitious term.
                return new LoanSchedule
                {
                    MonthlyPayment = scheduledPayment,
                    TotalPaid = totalPaid.Round(),
                    TotalInterest = totalInterest.Round(),
                    TermMonths = periods.Count,
                    Periods = periods,
                    IsFullyRepaid = false
                };
            }

            var closing = (balance - principalPaid).Round();

            periods.Add(new LoanPeriod(month, balance, thisPayment, interest, principalPaid, closing));

            totalInterest += interest;
            totalPaid += thisPayment;
            balance = closing;
        }

        return new LoanSchedule
        {
            MonthlyPayment = scheduledPayment,
            TotalPaid = totalPaid.Round(),
            TotalInterest = totalInterest.Round(),
            TermMonths = periods.Count,
            Periods = periods,
            IsFullyRepaid = balance <= Money.Zero
        };
    }

    public static LoanSchedule BuildSchedule(LoanTerms terms) => BuildSchedule(terms, Money.Zero);

    public static Money MonthlyPayment(LoanTerms terms)
    {
        ArgumentNullException.ThrowIfNull(terms);
        terms.Validate();

        if (terms.Principal.IsZero)
        {
            return Money.Zero;
        }

        var monthlyRate = terms.AnnualPercentageRate / 100m / 12m;

        // A zero-interest loan is simply the principal spread over the term.
        if (monthlyRate == 0m)
        {
            return (terms.Principal / terms.TermMonths).Round();
        }

        var growth = PowDecimal(1m + monthlyRate, terms.TermMonths);
        var payment = terms.Principal.Amount * monthlyRate * growth / (growth - 1m);

        return new Money(payment).Round();
    }

    /// <summary>
    /// Solves for the principal a given monthly payment can support, which answers
    /// "what car can we actually finance at this payment?" rather than the reverse.
    /// </summary>
    public static Money PrincipalForPayment(Money monthlyPayment, decimal annualPercentageRate, int termMonths)
    {
        if (monthlyPayment.IsNegative)
        {
            throw new ArgumentException("A payment cannot be negative.", nameof(monthlyPayment));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(termMonths);

        var monthlyRate = annualPercentageRate / 100m / 12m;

        if (monthlyRate == 0m)
        {
            return (monthlyPayment * termMonths).Round();
        }

        var growth = PowDecimal(1m + monthlyRate, termMonths);
        var principal = monthlyPayment.Amount * (growth - 1m) / (monthlyRate * growth);

        return new Money(principal).Round();
    }

    /// <summary>
    /// Declining-balance depreciation, which reflects how vehicles actually lose value:
    /// steeply at first, then more slowly.
    /// </summary>
    public static Money DepreciatedValue(Money purchasePrice, decimal annualDepreciationPercent, decimal years)
    {
        if (purchasePrice.IsNegative)
        {
            throw new ArgumentException("A purchase price cannot be negative.", nameof(purchasePrice));
        }

        if (annualDepreciationPercent is < 0m or > 100m)
        {
            throw new ArgumentException(
                "Annual depreciation must be between 0 and 100 percent.",
                nameof(annualDepreciationPercent));
        }

        if (years <= 0m)
        {
            return purchasePrice;
        }

        var retained = 1m - (annualDepreciationPercent / 100m);
        var value = purchasePrice.Amount;
        var wholeYears = (int)Math.Floor(years);

        for (var i = 0; i < wholeYears; i++)
        {
            value *= retained;
        }

        // Apportion the final partial year linearly.
        var fraction = years - wholeYears;
        if (fraction > 0m)
        {
            value *= 1m - ((1m - retained) * fraction);
        }

        return new Money(value).Round();
    }

    private static decimal PowDecimal(decimal value, int exponent)
    {
        var result = 1m;
        for (var i = 0; i < exponent; i++)
        {
            result *= value;
        }

        return result;
    }
}
