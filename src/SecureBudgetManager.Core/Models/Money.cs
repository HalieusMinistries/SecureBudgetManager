using System.Globalization;

namespace SecureBudgetManager.Core.Models;

/// <summary>
/// A household money value stored as a decimal so cents are never lost to binary floating point.
/// Currency conversion and online rates are intentionally unsupported.
///
/// Rounding rule: money is rounded to two decimal places using banker's rounding
/// (<see cref="MidpointRounding.ToEven"/>) only at the point a value is presented or stored as a
/// settled amount. Intermediate arithmetic keeps full decimal precision.
/// </summary>
public readonly record struct Money : IComparable<Money>, IFormattable
{
    public const int DecimalPlaces = 2;

    public static Money Zero { get; } = new(0m);

    public Money(decimal amount)
    {
        Amount = amount;
    }

    public decimal Amount { get; }

    public bool IsZero => Amount == 0m;

    public bool IsNegative => Amount < 0m;

    public static Money FromDecimal(decimal amount) => new(amount);

    /// <summary>Rounds to cents. Use when settling a value for display or storage.</summary>
    public Money Round() => new(Math.Round(Amount, DecimalPlaces, MidpointRounding.ToEven));

    public Money Abs() => new(Math.Abs(Amount));

    public Money Negate() => new(-Amount);

    /// <summary>
    /// Splits an amount into <paramref name="parts"/> shares that sum exactly back to the original,
    /// distributing any leftover cent one at a time rather than silently losing or inventing money.
    /// </summary>
    public Money[] Allocate(int parts)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(parts);

        var total = Round();
        var totalCents = (long)decimal.Round(total.Amount * 100m, 0, MidpointRounding.ToEven);
        var baseCents = totalCents / parts;
        var remainder = Math.Abs(totalCents % parts);
        var step = totalCents < 0 ? -1L : 1L;

        var shares = new Money[parts];
        for (var i = 0; i < parts; i++)
        {
            var cents = baseCents + (i < remainder ? step : 0L);
            shares[i] = new Money(cents / 100m);
        }

        return shares;
    }

    /// <summary>
    /// Splits an amount by weights (for example 60/40 bill sharing) so the shares sum exactly to the original.
    /// </summary>
    public Money[] AllocateByWeight(IReadOnlyList<decimal> weights)
    {
        ArgumentNullException.ThrowIfNull(weights);

        if (weights.Count == 0)
        {
            throw new ArgumentException("At least one weight is required.", nameof(weights));
        }

        if (weights.Any(weight => weight < 0m))
        {
            throw new ArgumentException("Weights cannot be negative.", nameof(weights));
        }

        var totalWeight = weights.Sum();
        if (totalWeight == 0m)
        {
            throw new ArgumentException("At least one weight must be greater than zero.", nameof(weights));
        }

        var totalCents = (long)decimal.Round(Round().Amount * 100m, 0, MidpointRounding.ToEven);
        var shares = new long[weights.Count];
        var allocated = 0L;

        for (var i = 0; i < weights.Count; i++)
        {
            shares[i] = (long)decimal.Truncate(totalCents * weights[i] / totalWeight);
            allocated += shares[i];
        }

        // Hand the unallocated cents to the largest weights first.
        var order = Enumerable.Range(0, weights.Count)
            .OrderByDescending(index => weights[index])
            .ToArray();

        var leftover = totalCents - allocated;
        var step = leftover < 0 ? -1L : 1L;

        for (var i = 0; leftover != 0 && i < order.Length; i++)
        {
            shares[order[i]] += step;
            leftover -= step;

            if (i == order.Length - 1 && leftover != 0)
            {
                i = -1;
            }
        }

        return shares.Select(cents => new Money(cents / 100m)).ToArray();
    }

    public static Money operator +(Money left, Money right) => new(left.Amount + right.Amount);

    public static Money operator -(Money left, Money right) => new(left.Amount - right.Amount);

    public static Money operator -(Money value) => value.Negate();

    public static Money operator *(Money left, decimal factor) => new(left.Amount * factor);

    public static Money operator *(decimal factor, Money right) => new(right.Amount * factor);

    public static Money operator /(Money left, decimal divisor)
    {
        if (divisor == 0m)
        {
            throw new DivideByZeroException("Money cannot be divided by zero.");
        }

        return new Money(left.Amount / divisor);
    }

    public static bool operator >(Money left, Money right) => left.Amount > right.Amount;

    public static bool operator <(Money left, Money right) => left.Amount < right.Amount;

    public static bool operator >=(Money left, Money right) => left.Amount >= right.Amount;

    public static bool operator <=(Money left, Money right) => left.Amount <= right.Amount;

    public static Money Min(Money left, Money right) => left <= right ? left : right;

    public static Money Max(Money left, Money right) => left >= right ? left : right;

    public static Money Sum(IEnumerable<Money> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var total = 0m;
        foreach (var value in values)
        {
            total += value.Amount;
        }

        return new Money(total);
    }

    public int CompareTo(Money other) => Amount.CompareTo(other.Amount);

    public override string ToString() => Amount.ToString("0.00", CultureInfo.InvariantCulture);

    public string ToString(string? format, IFormatProvider? formatProvider) =>
        Amount.ToString(format ?? "0.00", formatProvider ?? CultureInfo.InvariantCulture);

    /// <summary>Formats for display with a currency symbol, rounded to cents.</summary>
    public string ToDisplayString(string currencySymbol = "$")
    {
        var rounded = Round().Amount;
        return rounded < 0m
            ? $"-{currencySymbol}{Math.Abs(rounded).ToString("N2", CultureInfo.InvariantCulture)}"
            : $"{currencySymbol}{rounded.ToString("N2", CultureInfo.InvariantCulture)}";
    }
}
