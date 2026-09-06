using SecureBudgetManager.Core.Models;

namespace SecureBudgetManager.Core.Time;

/// <summary>
/// Converts amounts between recurrence frequencies through an annual total.
///
/// Conversions always route through the annual figure rather than approximating
/// "monthly = weekly x 4", which understates a weekly bill by about 8 percent a year.
/// </summary>
public static class FrequencyConverter
{
    public static Money ToAnnual(Money amount, Frequency frequency)
    {
        if (frequency == Frequency.OneOff)
        {
            // A one-off cost has no annual rate; callers must handle it as a dated event.
            return Money.Zero;
        }

        return amount * frequency.PaymentsPerYear();
    }

    /// <summary>Splits an annual total into the amount due each period.</summary>
    public static Money FromAnnual(Money annualAmount, Frequency frequency)
    {
        if (frequency == Frequency.OneOff)
        {
            return annualAmount;
        }

        return annualAmount / frequency.PaymentsPerYear();
    }

    public static Money Convert(Money amount, Frequency from, Frequency to)
    {
        if (from == to)
        {
            return amount;
        }

        if (from == Frequency.OneOff || to == Frequency.OneOff)
        {
            throw new ArgumentException(
                "A one-off amount cannot be converted to or from a recurring frequency.",
                nameof(from));
        }

        return ToAnnual(amount, from) / to.PaymentsPerYear();
    }

    public static Money ToMonthly(Money amount, Frequency frequency) =>
        frequency == Frequency.OneOff ? Money.Zero : Convert(amount, frequency, Frequency.Monthly);

    public static Money ToWeekly(Money amount, Frequency frequency) =>
        frequency == Frequency.OneOff ? Money.Zero : Convert(amount, frequency, Frequency.Weekly);

    /// <summary>
    /// The number of times a frequency falls due within a date range, counted by actual dates
    /// rather than by dividing an annual rate. This is what makes a five-payday month visible.
    /// </summary>
    public static int CountOccurrences(Frequency frequency, DateOnly anchor, DateOnly from, DateOnly to)
    {
        if (to < from)
        {
            throw new ArgumentException("The end of the range cannot precede the start.", nameof(to));
        }

        return RecurrenceSchedule.Enumerate(frequency, anchor, from, to).Count();
    }
}
