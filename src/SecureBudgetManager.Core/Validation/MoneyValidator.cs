namespace SecureBudgetManager.Core.Validation;

public static class MoneyValidator
{
    public const int MaxDecimalPlaces = 2;

    public static AmountValidationResult ValidateEnteredAmount(decimal amount)
    {
        if (amount < 0m)
        {
            return AmountValidationResult.Failure("Amount cannot be negative.");
        }

        if (GetScale(amount) > MaxDecimalPlaces)
        {
            return AmountValidationResult.Failure("Amount cannot have more than two decimal places.");
        }

        return AmountValidationResult.Success();
    }

    public static bool IsValidEnteredAmount(decimal amount) => ValidateEnteredAmount(amount).IsValid;

    private static int GetScale(decimal value)
    {
        var bits = decimal.GetBits(value);
        return (bits[3] >> 16) & 0x7F;
    }
}
