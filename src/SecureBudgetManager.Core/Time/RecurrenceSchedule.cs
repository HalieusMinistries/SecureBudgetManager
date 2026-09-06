namespace SecureBudgetManager.Core.Time;

/// <summary>
/// Generates the actual dates on which a schedule falls due.
///
/// The anchor is the first occurrence, not merely a phase reference. Nothing is ever generated
/// before it, so a car bought in March cannot produce payments in January — which would quietly
/// corrupt every affordability verdict that depends on the projection.
///
/// Month-end is handled by clamping rather than rolling forward: a bill anchored on the 31st
/// falls on 28 or 29 February, not on 3 March. Rolling forward would drift the bill into the
/// following month and eventually skip a month entirely.
/// </summary>
public static class RecurrenceSchedule
{
    /// <summary>The second monthly date used by twice-monthly schedules, alongside the anchor day.</summary>
    public const int TwiceMonthlySecondDay = 15;

    public static IEnumerable<DateOnly> Enumerate(Frequency frequency, DateOnly anchor, DateOnly from, DateOnly to)
    {
        if (to < from)
        {
            throw new ArgumentException("The end of the range cannot precede the start.", nameof(to));
        }

        // The window never opens before the first occurrence.
        var start = from < anchor ? anchor : from;

        if (to < start)
        {
            return [];
        }

        return frequency switch
        {
            Frequency.OneOff => EnumerateOneOff(anchor, start, to),
            Frequency.Weekly => EnumerateByDays(anchor, 7, start, to),
            Frequency.Fortnightly => EnumerateByDays(anchor, 14, start, to),
            Frequency.FourWeekly => EnumerateByDays(anchor, 28, start, to),
            Frequency.TwiceMonthly => EnumerateTwiceMonthly(anchor, start, to),
            Frequency.Monthly => EnumerateByMonths(anchor, 1, start, to),
            Frequency.Quarterly => EnumerateByMonths(anchor, 3, start, to),
            Frequency.SixMonthly => EnumerateByMonths(anchor, 6, start, to),
            Frequency.Annual => EnumerateByMonths(anchor, 12, start, to),
            _ => throw new ArgumentOutOfRangeException(nameof(frequency), frequency, "Unknown frequency.")
        };
    }

    public static DateOnly? NextOccurrence(Frequency frequency, DateOnly anchor, DateOnly onOrAfter)
    {
        // A year and a day is enough to find the next date for every supported frequency.
        var horizon = onOrAfter.AddDays(400);

        foreach (var occurrence in Enumerate(frequency, anchor, onOrAfter, horizon))
        {
            return occurrence;
        }

        return null;
    }

    private static IEnumerable<DateOnly> EnumerateOneOff(DateOnly anchor, DateOnly from, DateOnly to)
    {
        if (anchor >= from && anchor <= to)
        {
            yield return anchor;
        }
    }

    private static IEnumerable<DateOnly> EnumerateByDays(DateOnly anchor, int stepDays, DateOnly from, DateOnly to)
    {
        // Step backwards or forwards from the anchor onto the first date inside the range,
        // so the phase of a fortnightly cycle is preserved regardless of where the window starts.
        var offset = from.DayNumber - anchor.DayNumber;
        var periods = (int)Math.Floor(offset / (double)stepDays);
        var current = anchor.AddDays(periods * stepDays);

        while (current < from)
        {
            current = current.AddDays(stepDays);
        }

        while (current <= to)
        {
            yield return current;
            current = current.AddDays(stepDays);
        }
    }

    private static IEnumerable<DateOnly> EnumerateByMonths(DateOnly anchor, int stepMonths, DateOnly from, DateOnly to)
    {
        var anchorDay = anchor.Day;
        var monthsApart = ((from.Year - anchor.Year) * 12) + (from.Month - anchor.Month);
        var periods = (int)Math.Floor(monthsApart / (double)stepMonths) - 1;

        while (true)
        {
            var candidate = AddMonthsClamped(anchor, periods * stepMonths, anchorDay);

            if (candidate > to)
            {
                yield break;
            }

            if (candidate >= from)
            {
                yield return candidate;
            }

            periods++;
        }
    }

    private static IEnumerable<DateOnly> EnumerateTwiceMonthly(DateOnly anchor, DateOnly from, DateOnly to)
    {
        var firstDay = anchor.Day;
        var secondDay = firstDay == TwiceMonthlySecondDay ? 1 : TwiceMonthlySecondDay;
        var earlier = Math.Min(firstDay, secondDay);
        var later = Math.Max(firstDay, secondDay);

        var cursor = new DateOnly(from.Year, from.Month, 1).AddMonths(-1);
        var stop = new DateOnly(to.Year, to.Month, 1).AddMonths(1);

        while (cursor <= stop)
        {
            foreach (var day in (int[])[earlier, later])
            {
                var candidate = ClampToMonth(cursor.Year, cursor.Month, day);
                if (candidate >= from && candidate >= anchor && candidate <= to)
                {
                    yield return candidate;
                }
            }

            cursor = cursor.AddMonths(1);
        }
    }

    private static DateOnly AddMonthsClamped(DateOnly anchor, int months, int preferredDay)
    {
        var shifted = new DateOnly(anchor.Year, anchor.Month, 1).AddMonths(months);
        return ClampToMonth(shifted.Year, shifted.Month, preferredDay);
    }

    private static DateOnly ClampToMonth(int year, int month, int preferredDay)
    {
        var daysInMonth = DateTime.DaysInMonth(year, month);
        return new DateOnly(year, month, Math.Min(preferredDay, daysInMonth));
    }
}
