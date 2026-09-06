using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.App.Services;

public sealed record ChoiceOption<T>(T Value, string Name)
{
    public override string ToString() => Name;
}

public static class FrequencyChoices
{
    public static IReadOnlyList<ChoiceOption<Frequency>> Income { get; } =
    [
        new(Frequency.Weekly, Frequency.Weekly.ToDisplayName()),
        new(Frequency.Fortnightly, Frequency.Fortnightly.ToDisplayName()),
        new(Frequency.FourWeekly, Frequency.FourWeekly.ToDisplayName()),
        new(Frequency.TwiceMonthly, Frequency.TwiceMonthly.ToDisplayName()),
        new(Frequency.Monthly, Frequency.Monthly.ToDisplayName()),
        new(Frequency.Annual, Frequency.Annual.ToDisplayName()),
        new(Frequency.OneOff, Frequency.OneOff.ToDisplayName())
    ];

    public static IReadOnlyList<ChoiceOption<Frequency>> Expense { get; } =
    [
        new(Frequency.Weekly, Frequency.Weekly.ToDisplayName()),
        new(Frequency.Fortnightly, Frequency.Fortnightly.ToDisplayName()),
        new(Frequency.FourWeekly, Frequency.FourWeekly.ToDisplayName()),
        new(Frequency.TwiceMonthly, Frequency.TwiceMonthly.ToDisplayName()),
        new(Frequency.Monthly, Frequency.Monthly.ToDisplayName()),
        new(Frequency.Quarterly, Frequency.Quarterly.ToDisplayName()),
        new(Frequency.SixMonthly, Frequency.SixMonthly.ToDisplayName()),
        new(Frequency.Annual, Frequency.Annual.ToDisplayName())
    ];

    /// <summary>Recurring frequencies, for schedules that are neither income nor a bill.</summary>
    public static IReadOnlyList<ChoiceOption<Frequency>> Recurring { get; } = Expense;
}
