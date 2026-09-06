using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.Tests.Time;

/// <summary>
/// Schedules decide when money actually moves, so the edge cases here are the ones that make a
/// cash-flow projection wrong: month ends, leap years, five-payday months and windows that start
/// mid-cycle.
/// </summary>
public sealed class RecurrenceScheduleTests
{
    [Fact]
    public void WeeklyPayFallsOnTheSameWeekday()
    {
        var dates = RecurrenceSchedule.Enumerate(
            Frequency.Weekly,
            new DateOnly(2026, 1, 2),
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 2, 1)).ToList();

        Assert.All(dates, date => Assert.Equal(DayOfWeek.Friday, date.DayOfWeek));
        Assert.Equal(new DateOnly(2026, 1, 2), dates[0]);
        Assert.Equal(5, dates.Count);
    }

    [Fact]
    public void AFiveFridayMonthProducesFivePaydays()
    {
        // January 2026 has five Fridays. A budget that assumes four paydays a month is wrong
        // roughly four times a year, and always in the household's favour until it isn't.
        var dates = RecurrenceSchedule.Enumerate(
            Frequency.Weekly,
            new DateOnly(2026, 1, 2),
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 31)).ToList();

        Assert.Equal(5, dates.Count);
    }

    [Fact]
    public void AWindowStartingMidCycleKeepsTheFortnightlyPhase()
    {
        var anchor = new DateOnly(2026, 1, 9);

        var dates = RecurrenceSchedule.Enumerate(
            Frequency.Fortnightly,
            anchor,
            new DateOnly(2026, 3, 1),
            new DateOnly(2026, 4, 1)).ToList();

        // Every date must still be a whole number of fortnights from the anchor.
        Assert.All(dates, date => Assert.Equal(0, (date.DayNumber - anchor.DayNumber) % 14));
        Assert.NotEmpty(dates);
    }

    [Fact]
    public void FortnightlyProducesTwentySixPaymentsAYear()
    {
        var dates = RecurrenceSchedule.Enumerate(
            Frequency.Fortnightly,
            new DateOnly(2026, 1, 2),
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31)).ToList();

        Assert.Equal(26, dates.Count);
    }

    [Fact]
    public void AMonthEndBillIsClampedRatherThanRolledIntoTheNextMonth()
    {
        var dates = RecurrenceSchedule.Enumerate(
            Frequency.Monthly,
            new DateOnly(2026, 1, 31),
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 5, 1)).ToList();

        Assert.Equal(new DateOnly(2026, 1, 31), dates[0]);
        Assert.Equal(new DateOnly(2026, 2, 28), dates[1]);
        Assert.Equal(new DateOnly(2026, 3, 31), dates[2]);
        Assert.Equal(new DateOnly(2026, 4, 30), dates[3]);

        // Each month must appear exactly once: rolling forward would skip February entirely.
        Assert.Equal(dates.Count, dates.Select(date => (date.Year, date.Month)).Distinct().Count());
    }

    [Fact]
    public void AMonthEndBillLandsOnTheTwentyNinthInALeapYear()
    {
        var dates = RecurrenceSchedule.Enumerate(
            Frequency.Monthly,
            new DateOnly(2028, 1, 31),
            new DateOnly(2028, 2, 1),
            new DateOnly(2028, 3, 1)).ToList();

        Assert.Equal(new DateOnly(2028, 2, 29), dates[0]);
    }

    [Fact]
    public void ClampingDoesNotPermanentlyMoveTheBillEarlier()
    {
        // After February clamps a 31st bill to the 28th, March must return to the 31st.
        var dates = RecurrenceSchedule.Enumerate(
            Frequency.Monthly,
            new DateOnly(2026, 1, 31),
            new DateOnly(2026, 3, 1),
            new DateOnly(2026, 3, 31)).ToList();

        Assert.Equal(new DateOnly(2026, 3, 31), Assert.Single(dates));
    }

    [Fact]
    public void TwiceMonthlyProducesTwentyFourDatesAYear()
    {
        var dates = RecurrenceSchedule.Enumerate(
            Frequency.TwiceMonthly,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31)).ToList();

        Assert.Equal(24, dates.Count);
        Assert.All(dates, date => Assert.True(date.Day is 1 or 15));
    }

    [Fact]
    public void TwiceMonthlyDatesAreInOrder()
    {
        var dates = RecurrenceSchedule.Enumerate(
            Frequency.TwiceMonthly,
            new DateOnly(2026, 1, 15),
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 6, 30)).ToList();

        Assert.Equal(dates.OrderBy(date => date), dates);
    }

    [Fact]
    public void QuarterlyStepsThreeMonthsAtATime()
    {
        var dates = RecurrenceSchedule.Enumerate(
            Frequency.Quarterly,
            new DateOnly(2026, 2, 10),
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31)).ToList();

        Assert.Equal(
            [
                new DateOnly(2026, 2, 10),
                new DateOnly(2026, 5, 10),
                new DateOnly(2026, 8, 10),
                new DateOnly(2026, 11, 10)
            ],
            dates);
    }

    [Fact]
    public void AnAnnualBillAppearsOnceAYear()
    {
        var dates = RecurrenceSchedule.Enumerate(
            Frequency.Annual,
            new DateOnly(2026, 8, 14),
            new DateOnly(2026, 1, 1),
            new DateOnly(2028, 12, 31)).ToList();

        Assert.Equal(3, dates.Count);
        Assert.All(dates, date => Assert.Equal(8, date.Month));
    }

    [Fact]
    public void AOneOffOnlyAppearsIfItFallsInTheWindow()
    {
        var inside = RecurrenceSchedule.Enumerate(
            Frequency.OneOff,
            new DateOnly(2026, 3, 15),
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31));

        var outside = RecurrenceSchedule.Enumerate(
            Frequency.OneOff,
            new DateOnly(2025, 3, 15),
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31));

        Assert.Single(inside);
        Assert.Empty(outside);
    }

    [Fact]
    public void AnAnchorInTheFutureIsNotProjectedBackwards()
    {
        var dates = RecurrenceSchedule.Enumerate(
            Frequency.Monthly,
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31)).ToList();

        Assert.Equal(new DateOnly(2026, 6, 1), dates[0]);
        Assert.All(dates, date => Assert.True(date >= new DateOnly(2026, 6, 1)));
    }

    [Fact]
    public void ASingleDayWindowFindsADateOnThatDay()
    {
        var dates = RecurrenceSchedule.Enumerate(
            Frequency.Weekly,
            new DateOnly(2026, 1, 2),
            new DateOnly(2026, 1, 9),
            new DateOnly(2026, 1, 9));

        Assert.Equal(new DateOnly(2026, 1, 9), Assert.Single(dates));
    }

    [Fact]
    public void AnInvertedWindowIsRefused()
    {
        Assert.Throws<ArgumentException>(() => RecurrenceSchedule.Enumerate(
            Frequency.Weekly,
            new DateOnly(2026, 1, 2),
            new DateOnly(2026, 2, 1),
            new DateOnly(2026, 1, 1)).ToList());
    }

    [Fact]
    public void NextOccurrenceFindsTheUpcomingDate()
    {
        var next = RecurrenceSchedule.NextOccurrence(
            Frequency.Monthly,
            new DateOnly(2026, 1, 15),
            new DateOnly(2026, 3, 20));

        Assert.Equal(new DateOnly(2026, 4, 15), next);
    }

    [Fact]
    public void NextOccurrenceIncludesToday()
    {
        var next = RecurrenceSchedule.NextOccurrence(
            Frequency.Monthly,
            new DateOnly(2026, 1, 15),
            new DateOnly(2026, 3, 15));

        Assert.Equal(new DateOnly(2026, 3, 15), next);
    }

    [Fact]
    public void CountingOccurrencesMatchesEnumeration()
    {
        foreach (var frequency in Enum.GetValues<Frequency>())
        {
            var anchor = new DateOnly(2026, 1, 5);
            var from = new DateOnly(2026, 1, 1);
            var to = new DateOnly(2026, 12, 31);

            Assert.Equal(
                RecurrenceSchedule.Enumerate(frequency, anchor, from, to).Count(),
                FrequencyConverter.CountOccurrences(frequency, anchor, from, to));
        }
    }

    [Theory]
    [InlineData(Frequency.Weekly, 52)]
    [InlineData(Frequency.Fortnightly, 26)]
    [InlineData(Frequency.TwiceMonthly, 24)]
    [InlineData(Frequency.Monthly, 12)]
    [InlineData(Frequency.Quarterly, 4)]
    [InlineData(Frequency.Annual, 1)]
    public void PaymentsPerYearMatchesTheDatesActuallyGenerated(Frequency frequency, int expected)
    {
        Assert.Equal(expected, frequency.PaymentsPerYear());

        // A window of 364 days holds exactly one year of every supported cycle. A 365-day window
        // would hold 53 weekly dates, which is why "52 weeks" and "one year" are not the same span.
        var anchor = new DateOnly(2026, 1, 5);
        var generated = RecurrenceSchedule.Enumerate(
            frequency,
            anchor,
            anchor,
            anchor.AddDays(363)).Count();

        Assert.Equal(expected, generated);
    }
}
