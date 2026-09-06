using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace SecureBudgetManager.App.Services;

public sealed class WpfCaptureDialogs : ICaptureDialogs
{
    public const string PrivacyWarning =
        "This screenshot contains household financial information. Anyone with the PNG file may read it. Store it only in a trusted location.";

    public bool ConfirmPrivacyWarning()
    {
        var result = MessageBox.Show(
            PrivacyWarning,
            "Capture full page",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);

        return result == MessageBoxResult.OK;
    }

    public string? ChoosePngPath(string suggestedFileName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Capture full page",
            Filter = "PNG image (*.png)|*.png",
            DefaultExt = ".png",
            AddExtension = true,
            FileName = Path.GetFileName(suggestedFileName),
            OverwritePrompt = true,
            CheckPathExists = true
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public bool OfferToOpenContainingFolder(string fileNameOnly)
    {
        var result = MessageBox.Show(
            $"Saved {fileNameOnly}. Open the folder?",
            "Capture full page",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information,
            MessageBoxResult.No);

        return result == MessageBoxResult.Yes;
    }
}
