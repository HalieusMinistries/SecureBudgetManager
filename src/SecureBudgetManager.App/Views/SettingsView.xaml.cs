using Microsoft.Win32;
using SecureBudgetManager.App.ViewModels;
using System.Windows;

namespace SecureBudgetManager.App.Views;

public partial class SettingsView
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private async void OnCreateBackup(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel viewModel)
        {
            return;
        }

        var dialog = new OpenFolderDialog { Title = "Choose a trusted folder for the local database backup" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await viewModel.CreateBackupCommand.ExecuteAsync(dialog.FolderName);
    }

    private async void OnRestoreBackup(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel viewModel)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Choose a local database backup",
            Filter = "Database backup (*.sbmbak)|*.sbmbak"
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await viewModel.RestoreBackupCommand.ExecuteAsync(dialog.FileName);
    }

    private void OnExport(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel viewModel)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export portable CSV",
            Filter = "CSV (*.csv)|*.csv",
            FileName = "household-export.csv"
        };
        if (dialog.ShowDialog() == true)
        {
            viewModel.ExportCommand.Execute(dialog.FileName);
        }
    }

    private void OnPreviewImport(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel viewModel || !TryPickCsv(out var file))
        {
            return;
        }

        viewModel.PreviewImportCommand.Execute(file);
    }

    private async void OnImport(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel viewModel || !TryPickCsv(out var file))
        {
            return;
        }

        await viewModel.ImportCommand.ExecuteAsync(file);
    }

    private static bool TryPickCsv(out string file)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a portable CSV",
            Filter = "CSV (*.csv)|*.csv"
        };
        if (dialog.ShowDialog() == true)
        {
            file = dialog.FileName;
            return true;
        }

        file = string.Empty;
        return false;
    }
}
