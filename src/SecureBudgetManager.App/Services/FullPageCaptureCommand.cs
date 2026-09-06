using System.IO;
using Microsoft.Extensions.Logging;
using SecureBudgetManager.Core.Abstractions;
using SecureBudgetManager.Core.Capture;
using SecureBudgetManager.Core.Security;

namespace SecureBudgetManager.App.Services;

/// <summary>
/// Orchestrates warning, save dialog and WPF rendering. Never writes budget data.
/// </summary>
public sealed class FullPageCaptureCommand
{
    private readonly IVaultService _vault;
    private readonly ICaptureVisualLocator _locator;
    private readonly IFullPageCaptureService _capture;
    private readonly ICaptureDialogs _dialogs;
    private readonly IUiPreferenceStore _preferences;
    private readonly IFolderLauncher _folders;
    private readonly TimeProvider _clock;
    private readonly ILogger<FullPageCaptureCommand> _logger;

    public FullPageCaptureCommand(
        IVaultService vault,
        ICaptureVisualLocator locator,
        IFullPageCaptureService capture,
        ICaptureDialogs dialogs,
        IUiPreferenceStore preferences,
        IFolderLauncher folders,
        TimeProvider clock,
        ILogger<FullPageCaptureCommand> logger)
    {
        _vault = vault;
        _locator = locator;
        _capture = capture;
        _dialogs = dialogs;
        _preferences = preferences;
        _folders = folders;
        _clock = clock;
        _logger = logger;
    }

    public bool CanExecute() =>
        _vault.Status == VaultStatus.Unlocked && _locator.IsWorkspaceVisible;

    public string? Execute(string pageTitle)
    {
        if (!CanExecute())
        {
            return null;
        }

        if (!_preferences.FullPageCaptureWarningAcknowledged)
        {
            if (!_dialogs.ConfirmPrivacyWarning())
            {
                return null;
            }

            _preferences.AcknowledgeFullPageCaptureWarning();
        }

        var suggested = CaptureFileName.Suggest(pageTitle, _clock.GetLocalNow());
        var destination = _dialogs.ChoosePngPath(suggested);
        if (string.IsNullOrWhiteSpace(destination))
        {
            return null;
        }

        if (!CanExecute())
        {
            return null;
        }

        var page = _locator.FindActivePage();
        if (page is null)
        {
            return "The current page could not be captured.";
        }

        var result = _capture.Capture(page, destination);
        if (!result.Succeeded)
        {
            return result.Error ?? "The page could not be captured.";
        }

        _logger.LogInformation("Full-page capture completed. Files written: {FileCount}.", result.FilesWritten);

        var nameOnly = Path.GetFileName(destination);
        if (_dialogs.OfferToOpenContainingFolder(nameOnly))
        {
            _folders.OpenContainingFolder(destination);
        }

        return result.FilesWritten > 1
            ? $"Saved {result.FilesWritten} PNG files."
            : $"Saved {nameOnly}.";
    }
}
