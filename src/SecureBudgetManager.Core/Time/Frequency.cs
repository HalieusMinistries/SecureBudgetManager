namespace SecureBudgetManager.Core.Time;

/// <summary>
/// How often an income or expense recurs. Deliberately explicit about fortnightly versus
/// twice-monthly, because they produce different numbers of payments per year (26 versus 24)
/// and mixing them is a common budgeting error.
/// </summary>
public enum Frequency
{
    OneOff = 0,
    Weekly = 1,
    Fortnightly = 2,
    TwiceMonthly = 3,
    Monthly = 4,
    Quarterly = 5,
    Annual = 6,

    /// <summary>
    /// Twice a year. Common for vehicle insurance, which is why it needs its own schedule rather
    /// than being approximated as a quarterly or annual bill.
    /// </summary>
    SixMonthly = 7,

    /// <summary>
    /// Every four weeks. Thirteen payments a year — never treated as a month.
    /// </summary>
    FourWeekly = 8
}

public static class FrequencyExtensions
{
    /// <summary>
    /// Payments per year. Weekly and fortnightly use 52 and 26, which is why a "monthly" budget
    /// built from weekly pay silently loses the extra cheques in a five-payday month.
    /// </summary>
    public static decimal PaymentsPerYear(this Frequency frequency) => frequency switch
    {
        Frequency.Weekly => 52m,
        Frequency.Fortnightly => 26m,
        Frequency.FourWeekly => 13m,
        Frequency.TwiceMonthly => 24m,
        Frequency.Monthly => 12m,
        Frequency.Quarterly => 4m,
        Frequency.SixMonthly => 2m,
        Frequency.Annual => 1m,
        Frequency.OneOff => 0m,
        _ => throw new ArgumentOutOfRangeException(nameof(frequency), frequency, "Unknown frequency.")
    };

    public static string ToDisplayName(this Frequency frequency) => frequency switch
    {
        Frequency.OneOff => "One-off",
        Frequency.Weekly => "Weekly",
        Frequency.Fortnightly => "Fortnightly",
        Frequency.FourWeekly => "Every four weeks",
        Frequency.TwiceMonthly => "Twice monthly",
        Frequency.Monthly => "Monthly",
        Frequency.Quarterly => "Quarterly",
        Frequency.SixMonthly => "Six-monthly",
        Frequency.Annual => "Annual",
        _ => throw new ArgumentOutOfRangeException(nameof(frequency), frequency, "Unknown frequency.")
    };

    public static bool IsRecurring(this Frequency frequency) => frequency != Frequency.OneOff;
}
