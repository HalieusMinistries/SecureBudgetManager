using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.Tests.Models;

/// <summary>
/// Property-based checks on money and frequency conversion.
///
/// These use a fixed seed so a failure is reproducible: a random test that cannot be replayed is
/// almost useless when it fails once in a hundred runs.
/// </summary>
public sealed class MoneyPropertyTests
{
    private const int Seed = 20260903;
    private const int Cases = 500;

    [Fact]
    public void AllocatingNeverLosesOrInventsMoney()
    {
        var random = new Random(Seed);

        for (var i = 0; i < Cases; i++)
        {
            var amount = RandomMoney(random);
            var parts = random.Next(1, 13);

            var shares = amount.Allocate(parts);

            Assert.Equal(parts, shares.Length);
            Assert.Equal(amount.Round(), Money.Sum(shares).Round());
        }
    }

    [Fact]
    public void AllocatingNegativeAmountsAlsoSumsExactly()
    {
        var random = new Random(Seed + 1);

        for (var i = 0; i < Cases; i++)
        {
            var amount = RandomMoney(random).Negate();
            var parts = random.Next(1, 13);

            Assert.Equal(amount.Round(), Money.Sum(amount.Allocate(parts)).Round());
        }
    }

    [Fact]
    public void AllocatingSharesDifferByAtMostOneCent()
    {
        var random = new Random(Seed + 2);

        for (var i = 0; i < Cases; i++)
        {
            var shares = RandomMoney(random).Allocate(random.Next(2, 8));
            var largest = shares.Max(share => share.Amount);
            var smallest = shares.Min(share => share.Amount);

            Assert.True(
                largest - smallest <= 0.01m,
                $"Shares differed by more than a cent: {smallest} to {largest}.");
        }
    }

    [Fact]
    public void WeightedAllocationSumsExactly()
    {
        var random = new Random(Seed + 3);

        for (var i = 0; i < Cases; i++)
        {
            var amount = RandomMoney(random);
            var count = random.Next(2, 6);
            var weights = Enumerable.Range(0, count)
                .Select(_ => (decimal)random.Next(1, 100))
                .ToList();

            var shares = amount.AllocateByWeight(weights);

            Assert.Equal(amount.Round(), Money.Sum(shares).Round());
        }
    }

    [Fact]
    public void WeightedAllocationRespectsTheWeightOrdering()
    {
        var shares = new Money(1000m).AllocateByWeight([60m, 40m]);

        Assert.Equal(new Money(600m), shares[0]);
        Assert.Equal(new Money(400m), shares[1]);
    }

    [Fact]
    public void WeightedAllocationHandlesAThreeWayThirdSplit()
    {
        // 100 / 3 cannot divide evenly, so the leftover cent must land somewhere and only once.
        var shares = new Money(100m).AllocateByWeight([1m, 1m, 1m]);

        Assert.Equal(new Money(100m), Money.Sum(shares));
        Assert.Equal(2, shares.Count(share => share.Amount == 33.33m));
        Assert.Equal(1, shares.Count(share => share.Amount == 33.34m));
    }

    [Fact]
    public void ConvertingAFrequencyAndBackReturnsTheOriginal()
    {
        var random = new Random(Seed + 4);
        var frequencies = Enum.GetValues<Frequency>().Where(frequency => frequency.IsRecurring()).ToArray();

        for (var i = 0; i < Cases; i++)
        {
            var amount = RandomMoney(random);
            var from = frequencies[random.Next(frequencies.Length)];
            var to = frequencies[random.Next(frequencies.Length)];

            var converted = FrequencyConverter.Convert(amount, from, to);
            var returned = FrequencyConverter.Convert(converted, to, from);

            // Round-tripping is exact to the cent, which is all a budget needs.
            Assert.True(
                Math.Abs(returned.Amount - amount.Amount) < 0.01m,
                $"{amount} converted {from} to {to} and back became {returned}.");
        }
    }

    [Fact]
    public void EveryFrequencyAgreesOnTheAnnualTotal()
    {
        var random = new Random(Seed + 5);

        for (var i = 0; i < 100; i++)
        {
            var annual = RandomMoney(random) * 12m;

            foreach (var frequency in Enum.GetValues<Frequency>().Where(item => item.IsRecurring()))
            {
                var perPeriod = FrequencyConverter.FromAnnual(annual, frequency);
                var backToAnnual = FrequencyConverter.ToAnnual(perPeriod, frequency);

                Assert.True(
                    Math.Abs(backToAnnual.Amount - annual.Amount) < 0.01m,
                    $"{frequency} disagreed: {annual} became {backToAnnual}.");
            }
        }
    }

    [Fact]
    public void WeeklyAndFortnightlyAgreeOnTheAnnualTotal()
    {
        // 52 weekly payments and 26 fortnightly payments cover the same year. A budget that
        // treats a fortnight as "half a month" quietly loses two payments a year.
        var weekly = new Money(100m);
        var annual = FrequencyConverter.ToAnnual(weekly, Frequency.Weekly);

        Assert.Equal(new Money(5200m), annual.Round());
        Assert.Equal(new Money(200m), FrequencyConverter.Convert(weekly, Frequency.Weekly, Frequency.Fortnightly).Round());
    }

    [Fact]
    public void AMonthlyAmountIsNotFourWeeks()
    {
        // 52 weeks divided by 12 months is 4.333, not 4. This is the single most common
        // budgeting error and it understates weekly costs by about 8 percent.
        var monthly = FrequencyConverter.Convert(new Money(100m), Frequency.Weekly, Frequency.Monthly);

        Assert.Equal(new Money(433.33m), monthly.Round());
        Assert.NotEqual(new Money(400m), monthly.Round());
    }

    [Fact]
    public void RoundingUsesBankersRoundingAtTheHalfCent()
    {
        Assert.Equal(0.02m, new Money(0.025m).Round().Amount);
        Assert.Equal(0.04m, new Money(0.035m).Round().Amount);
    }

    [Fact]
    public void VeryLargeAmountsDoNotOverflowOrLoseCents()
    {
        var large = new Money(987_654_321_098.76m);

        Assert.Equal(large.Round(), Money.Sum(large.Allocate(7)).Round());
        Assert.Equal(large, (large * 3m / 3m).Round());
    }

    [Fact]
    public void VerySmallAmountsSurviveAllocation()
    {
        var oneCent = new Money(0.01m);
        var shares = oneCent.Allocate(4);

        Assert.Equal(oneCent, Money.Sum(shares));
        Assert.Equal(1, shares.Count(share => share.Amount == 0.01m));
        Assert.Equal(3, shares.Count(share => share.IsZero));
    }

    [Fact]
    public void DividingByZeroIsRefusedRatherThanReturningInfinity()
    {
        Assert.Throws<DivideByZeroException>(() => new Money(100m) / 0m);
    }

    [Fact]
    public void AllocatingIntoZeroOrNegativePartsIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Money(100m).Allocate(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Money(100m).Allocate(-3));
    }

    [Fact]
    public void ANegativeWeightIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new Money(100m).AllocateByWeight([60m, -40m]));
    }

    [Fact]
    public void AllZeroWeightsAreRefusedRatherThanDividingByZero()
    {
        Assert.Throws<ArgumentException>(() => new Money(100m).AllocateByWeight([0m, 0m]));
    }

    [Fact]
    public void OneOffAmountsHaveNoRecurringEquivalent()
    {
        // A one-off cost has no annual rate. Returning zero forces callers to handle it as a dated
        // event rather than quietly spreading a deposit across twelve months.
        var oneOff = new Money(500m);

        Assert.Equal(Money.Zero, FrequencyConverter.ToAnnual(oneOff, Frequency.OneOff));
        Assert.Equal(Money.Zero, FrequencyConverter.ToMonthly(oneOff, Frequency.OneOff));
        Assert.Throws<ArgumentException>(
            () => FrequencyConverter.Convert(oneOff, Frequency.OneOff, Frequency.Monthly));
    }

    [Fact]
    public void DisplayFormattingKeepsTwoDecimalsAndHandlesNegatives()
    {
        Assert.Equal("$1,234.56", new Money(1234.564m).ToDisplayString());
        Assert.Equal("-$45.00", new Money(-45m).ToDisplayString());
        Assert.Equal("£10.50", new Money(10.5m).ToDisplayString("£"));
    }

    private static Money RandomMoney(Random random) =>
        new(Math.Round((decimal)(random.NextDouble() * 10_000d), 2));
}
