using System.Globalization;
using SecureBudgetManager.Core.Models;

namespace SecureBudgetManager.App.Services;

public static class AmountParsing
{
    public static bool TryParseDecimal(string? text, out decimal value)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            value = 0m;
            return false;
        }

        var trimmed = text.Trim().TrimStart('$');

        if (decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.CurrentCulture, out value))
        {
            return true;
        }

        return decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    public static bool TryParseMoney(string? text, out Money money)
    {
        if (!TryParseDecimal(text, out var amount))
        {
            money = Money.Zero;
            return false;
        }

        money = new Money(amount);
        return true;
    }

    public static string Format(Money money) => money.Round().Amount.ToString("0.00", CultureInfo.CurrentCulture);

    public static string Format(decimal value) => value.ToString("0.##", CultureInfo.CurrentCulture);
}
