namespace SecureBudgetManager.App.Interaction;

public enum EditorSaveStatus
{
    Saving = 0,
    Saved = 1,
    ValidationFailed = 2,
    NotSaved = 3
}

/// <summary>
/// One save outcome shared by every overlay editor so success, validation and persistence
/// failures are reported the same way.
/// </summary>
public sealed class EditorSaveResult
{
    public static IReadOnlyDictionary<string, string> NoFieldErrors { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public required EditorSaveStatus Status { get; init; }

    public required string Message { get; init; }

    public string? FocusField { get; init; }

    public IReadOnlyDictionary<string, string> FieldErrors { get; init; } = NoFieldErrors;

    public bool IsSuccess => Status == EditorSaveStatus.Saved;

    public bool KeepEditorOpen => Status is EditorSaveStatus.ValidationFailed or EditorSaveStatus.NotSaved;

    public static EditorSaveResult Saved(string confirmation) => new()
    {
        Status = EditorSaveStatus.Saved,
        Message = confirmation
    };

    public static EditorSaveResult Validation(
        string summary,
        string? focusField = null,
        string? fieldMessage = null)
    {
        IReadOnlyDictionary<string, string> fields = NoFieldErrors;
        if (!string.IsNullOrWhiteSpace(focusField) && !string.IsNullOrWhiteSpace(fieldMessage))
        {
            fields = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [focusField] = fieldMessage
            };
        }

        return new EditorSaveResult
        {
            Status = EditorSaveStatus.ValidationFailed,
            Message = summary,
            FocusField = focusField,
            FieldErrors = fields
        };
    }

    public static EditorSaveResult NotSaved(string? reason = null)
    {
        var detail = string.IsNullOrWhiteSpace(reason)
            ? "This information was not saved."
            : reason.StartsWith("This information was not saved.", StringComparison.Ordinal)
                ? reason
                : $"This information was not saved. {reason}";

        return new EditorSaveResult
        {
            Status = EditorSaveStatus.NotSaved,
            Message = detail
        };
    }

    public static EditorSaveResult AlreadySaving() =>
        NotSaved("A save is already in progress.");
}
