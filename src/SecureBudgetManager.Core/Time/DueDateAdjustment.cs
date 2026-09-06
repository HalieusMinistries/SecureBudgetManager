namespace SecureBudgetManager.Core.Time;

/// <summary>
/// How a due date moves when it falls on a Saturday, Sunday or observed US federal holiday.
/// </summary>
public enum DueDateAdjustment
{
    None = 0,
    NextWeekday = 1,
    PreviousWeekday = 2
}

/// <summary>
/// Weekend and US federal-holiday calendar used only to shift due dates. Tax and payday rules
/// keep their own calendars.
/// </summary>
public static class BusinessDayCalendar
{
    public static DateOnly Adjust(DateOnly date, DueDateAdjustment adjustment)
    {
        if (adjustment == DueDateAdjustment.None)
        {
            return date;
        }

        var step = adjustment == DueDateAdjustment.NextWeekday ? 1 : -1;
        var current = date;
        while (IsNonBusinessDay(current))
        {
            current = current.AddDays(step);
        }

        return current;
    }

    public static bool IsNonBusinessDay(DateOnly date) =>
        date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || IsObservedFederalHoliday(date);

    public static bool IsObservedFederalHoliday(DateOnly date)
    {
        foreach (var holiday in FederalHolidays(date.Year))
        {
            if (Observe(holiday) == date)
            {
                return true;
            }
        }

        return false;
    }

    public static IReadOnlyList<DateOnly> FederalHolidays(int year) =>
    [
        new(year, 1, 1),
        NthWeekday(year, 1, DayOfWeek.Monday, 3),
        NthWeekday(year, 2, DayOfWeek.Monday, 3),
        LastWeekday(year, 5, DayOfWeek.Monday),
        new(year, 6, 19),
        new(year, 7, 4),
        NthWeekday(year, 9, DayOfWeek.Monday, 1),
        NthWeekday(year, 10, DayOfWeek.Monday, 2),
        new(year, 11, 11),
        NthWeekday(year, 11, DayOfWeek.Thursday, 4),
        new(year, 12, 25)
    ];

    private static DateOnly Observe(DateOnly holiday) => holiday.DayOfWeek switch
    {
        DayOfWeek.Saturday => holiday.AddDays(-1),
        DayOfWeek.Sunday => holiday.AddDays(1),
        _ => holiday
    };

    private static DateOnly NthWeekday(int year, int month, DayOfWeek weekday, int occurrence)
    {
        var date = new DateOnly(year, month, 1);
        while (date.DayOfWeek != weekday)
        {
            date = date.AddDays(1);
        }

        return date.AddDays(7 * (occurrence - 1));
    }

    private static DateOnly LastWeekday(int year, int month, DayOfWeek weekday)
    {
        var date = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        while (date.DayOfWeek != weekday)
        {
            date = date.AddDays(-1);
        }

        return date;
    }
}
