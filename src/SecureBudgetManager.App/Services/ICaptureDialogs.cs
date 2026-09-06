namespace SecureBudgetManager.App.Services;

/// <summary>
/// User-facing prompts for full-page capture. Tests substitute a fake so no dialog is shown.
/// </summary>
public interface ICaptureDialogs
{
    bool ConfirmPrivacyWarning();

    /// <summary>Returns null when the user cancels. Never invents a folder.</summary>
    string? ChoosePngPath(string suggestedFileName);

    bool OfferToOpenContainingFolder(string fileNameOnly);
}
