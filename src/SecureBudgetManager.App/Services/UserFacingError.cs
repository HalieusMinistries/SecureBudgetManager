using SecureBudgetManager.Core.Security;

namespace SecureBudgetManager.App.Services;

/// <summary>
/// Maps exceptions to short messages that never include database, cryptographic or SQL detail.
/// </summary>
public static class UserFacingError
{
    public static string From(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            ArgumentException argument => string.IsNullOrWhiteSpace(argument.Message)
                ? "That entry is not valid."
                : argument.Message,
            VaultException vault => vault.Message,
            OperationCanceledException => "The action was cancelled.",
            InvalidOperationException invalid when IsStorageClosed(invalid) =>
                "The workspace is locked. Unlock it before saving.",
            _ => "The household data could not be saved. The local database may be unavailable."
        };
    }

    private static bool IsStorageClosed(InvalidOperationException exception) =>
        exception.Message.Contains("not open", StringComparison.OrdinalIgnoreCase)
        || exception.Message.Contains("locked", StringComparison.OrdinalIgnoreCase);
}
