namespace SecureBudgetManager.Core.Time;

/// <summary>
/// One pay cycle, identified by the date the money actually arrives.
/// </summary>
public sealed record PayPeriod(DateOnly Start, DateOnly End, DateOnly PayDate)
{
    public int LengthInDays => End.DayNumber - Start.DayNumber + 1;

    public bool Contains(DateOnly date) => date >= Start && date <= End;

    public void Validate()
    {
        if (End < Start)
        {
            throw new ArgumentException("A pay period cannot end before it starts.");
        }
    }
}

public static class PayPeriodCalendar
{
    /// <summary>
    /// Builds the pay periods that overlap a date range, based on the pay frequency and a known payday.
    /// </summary>
    public static IReadOnlyList<PayPeriod> Build(
        Frequency payFrequency,
        DateOnly knownPayDate,
        DateOnly from,
        DateOnly to)
    {
        if (!payFrequency.IsRecurring())
        {
            throw new ArgumentException("A pay schedule must be recurring.", nameof(payFrequency));
        }

        if (to < from)
        {
            throw new ArgumentException("The end of the range cannot precede the start.", nameof(to));
        }

        // Widen the window so the period containing 'from' is included even when its payday is earlier.
        var payDates = RecurrenceSchedule
            .Enumerate(payFrequency, knownPayDate, from.AddDays(-45), to.AddDays(45))
            .ToList();

        var periods = new List<PayPeriod>();

        for (var i = 0; i < payDates.Count; i++)
        {
            var payDate = payDates[i];
            var start = i == 0 ? payDate.AddDays(-DefaultLength(payFrequency) + 1) : payDates[i - 1].AddDays(1);
            var end = payDate;

            var period = new PayPeriod(start, end, payDate);
            if (period.End >= from && period.Start <= to)
            {
                periods.Add(period);
            }
        }

        return periods;
    }

    /// <summary>
    /// How many paydays fall in a calendar month. Weekly pay produces five-payday months
    /// several times a year, which is the difference between a tight month and a comfortable one.
    /// </summary>
    public static int CountPayDatesInMonth(Frequency payFrequency, DateOnly knownPayDate, int year, int month)
    {
        var from = new DateOnly(year, month, 1);
        var to = new DateOnly(year, month, DateTime.DaysInMonth(year, month));

        return RecurrenceSchedule.Enumerate(payFrequency, knownPayDate, from, to).Count();
    }

    private static int DefaultLength(Frequency payFrequency) => payFrequency switch
    {
        Frequency.Weekly => 7,
        Frequency.Fortnightly => 14,
        Frequency.FourWeekly => 28,
        Frequency.TwiceMonthly => 15,
        Frequency.Monthly => 30,
        Frequency.Quarterly => 91,
        Frequency.SixMonthly => 182,
        Frequency.Annual => 365,
        _ => 7
    };
}
