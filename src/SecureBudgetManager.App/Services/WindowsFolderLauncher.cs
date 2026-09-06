using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;

namespace SecureBudgetManager.App.Services;

public sealed class WindowsFolderLauncher : IFolderLauncher
{
    private readonly ILogger<WindowsFolderLauncher> _logger;

    public WindowsFolderLauncher(ILogger<WindowsFolderLauncher> logger)
    {
        _logger = logger;
    }

    public void OpenContainingFolder(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var folder = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true
            });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            _logger.LogWarning("The capture folder could not be opened. Exception type: {ExceptionType}.", exception.GetType().Name);
        }
    }
}
