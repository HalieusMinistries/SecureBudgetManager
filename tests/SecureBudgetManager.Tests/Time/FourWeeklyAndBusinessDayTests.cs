using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.Tests.Time;

public sealed class FourWeeklyAndBusinessDayTests
{
    [Fact]
    public void FourWeeklyUsesThirteenPaymentsAYearNotTwelve()
    {
        Assert.Equal(13m, Frequency.FourWeekly.PaymentsPerYear());
        var monthly = FrequencyConverter.ToMonthly(new Money(100m), Frequency.FourWeekly);
        Assert.Equal(new Money(108.33m), monthly.Round());
        Assert.NotEqual(new Money(400m), FrequencyConverter.ToMonthly(new Money(100m), Frequency.Weekly).Round());
    }

    [Fact]
    public void FourWeeklyOccurrencesStayTwentyEightDaysApart()
    {
        var dates = RecurrenceSchedule.Enumerate(
            Frequency.FourWeekly,
            new DateOnly(2026, 9, 4),
            new DateOnly(2026, 9, 4),
            new DateOnly(2026, 12, 31)).ToList();

        Assert.Equal(new DateOnly(2026, 9, 4), dates[0]);
        Assert.Equal(new DateOnly(2026, 10, 2), dates[1]);
        Assert.All(dates.Zip(dates.Skip(1)), pair => Assert.Equal(28, pair.Second.DayNumber - pair.First.DayNumber));
    }

    [Fact]
    public void WeekendDueDatesMoveToTheNextWeekdayWhenAsked()
    {
        var saturday = new DateOnly(2026, 9, 5);
        Assert.Equal(DayOfWeek.Saturday, saturday.DayOfWeek);
        Assert.Equal(new DateOnly(2026, 9, 8), BusinessDayCalendar.Adjust(saturday, DueDateAdjustment.NextWeekday));
        Assert.Equal(new DateOnly(2026, 9, 4), BusinessDayCalendar.Adjust(saturday, DueDateAdjustment.PreviousWeekday));
        Assert.Equal(saturday, BusinessDayCalendar.Adjust(saturday, DueDateAdjustment.None));
    }

    [Fact]
    public void IndependenceDayObservationIsRecognised()
    {
        Assert.True(BusinessDayCalendar.IsObservedFederalHoliday(new DateOnly(2026, 7, 3)));
    }
}
