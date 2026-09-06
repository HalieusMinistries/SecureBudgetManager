namespace SecureBudgetManager.App.Services;

/// <summary>
/// Non-financial UI flags only. Never stores household names, amounts or file paths.
/// </summary>
public interface IUiPreferenceStore
{
    bool FullPageCaptureWarningAcknowledged { get; }

    int LockTimeoutMinutes { get; }

    void AcknowledgeFullPageCaptureWarning();

    void SetLockTimeoutMinutes(int minutes);
}
