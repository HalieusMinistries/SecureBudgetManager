namespace SecureBudgetManager.Core.Validation;

public sealed record AmountValidationResult(bool IsValid, string? ErrorMessage)
{
    public static AmountValidationResult Success() => new(true, null);

    public static AmountValidationResult Failure(string errorMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new(false, errorMessage);
    }
}
