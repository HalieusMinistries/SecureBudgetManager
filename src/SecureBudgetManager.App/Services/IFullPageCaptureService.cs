using System.Windows;

namespace SecureBudgetManager.App.Services;

public sealed record FullPageCaptureResult(bool Succeeded, int FilesWritten, string? Error);

public interface IFullPageCaptureService
{
    FullPageCaptureResult Capture(FrameworkElement page, string destinationPath);
}
