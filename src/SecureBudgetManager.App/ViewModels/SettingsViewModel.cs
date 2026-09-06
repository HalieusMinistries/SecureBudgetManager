using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureBudgetManager.App.Security;
using SecureBudgetManager.App.Services;
using SecureBudgetManager.Core.Abstractions;
using SecureBudgetManager.Core.Locking;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Security;
using SecureBudgetManager.Core.Storage;

namespace SecureBudgetManager.App.ViewModels;

public sealed partial class SettingsViewModel : PageViewModel
{
    private readonly IUiPreferenceStore _preferences;
    private readonly InactivityMonitor _inactivity;
    private readonly ISecureBackupService _backups;
    private readonly IUserDialog _dialog;
    private readonly IBudgetSession _session;
    private readonly ILocalDataDirectory _directory;
    private readonly ILocalBudgetStore _store;

    public SettingsViewModel(
        IUiPreferenceStore preferences,
        InactivityMonitor inactivity,
        ISecureBackupService backups,
        IUserDialog dialog,
        IBudgetSession session,
        ILocalDataDirectory directory,
        ILocalBudgetStore store)
        : base(
            "Settings",
            "Preferences",
            "Privacy timeout, local backups and CSV exchange stay on this device. PNG screenshots are not backups.")
    {
        _preferences = preferences;
        _inactivity = inactivity;
        _backups = backups;
        _dialog = dialog;
        _session = session;
        _directory = directory;
        _store = store;
        selectedTimeoutMinutes = preferences.LockTimeoutMinutes;
        ApplyTimeout();
        _session.Changed += (_, _) => RefreshDocumentFields();
        RefreshDocumentFields();
    }

    public IReadOnlyList<ChoiceOption<int>> TimeoutOptions { get; } =
    [
        new(1, "1 minute"),
        new(5, "5 minutes"),
        new(10, "10 minutes"),
        new(15, "15 minutes"),
        new(30, "30 minutes")
    ];

    [ObservableProperty]
    private int selectedTimeoutMinutes;

    [ObservableProperty]
    private string minimumBalance = "0";

    [ObservableProperty]
    private string emergencyMonths = "3";

    [ObservableProperty]
    private string importPreview = string.Empty;

    [ObservableProperty]
    private string databaseInfo = string.Empty;

    [ObservableProperty]
    private string? statusMessage;

    [ObservableProperty]
    private string? errorMessage;

    public string PrivacyNote { get; } =
        "Database backups, CSV exports and PNG screenshots all contain household financial information. " +
        "Anyone with access to those files may read them. Store them only in a trusted location. " +
        "A backup that has never been restored is not a proven backup.";

    public string VersionText { get; } =
        $"Secure Budget Manager {typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0"}";

    public string StorageNote { get; } =
        "This programme stores an ordinary local SQLite file on this computer. There is no vault password, " +
        "no cloud copy and no database encryption. The Privacy screen only hides figures while the programme is running.";

    partial void OnSelectedTimeoutMinutesChanged(int value)
    {
        _preferences.SetLockTimeoutMinutes(value);
        ApplyTimeout();
        StatusMessage = $"Automatic privacy screen set to {value} minute(s).";
    }

    public void ApplyTimeout() =>
        _inactivity.SetTimeout(TimeSpan.FromMinutes(LockTimeoutMinutesSafe()));

    [RelayCommand]
    private async Task SavePlanningAssumptionsAsync(CancellationToken cancellationToken)
    {
        if (!_session.IsOpen)
        {
            ErrorMessage = "Open the household database first.";
            return;
        }

        if (!AmountParsing.TryParseMoney(MinimumBalance, out var reserve)
            || !AmountParsing.TryParseDecimal(EmergencyMonths, out var months)
            || reserve.IsNegative || months < 0m)
        {
            ErrorMessage = "Enter a minimum balance of 0 or more and an emergency-fund target in months.";
            return;
        }

        var document = _session.Document;
        var updated = document with
        {
            Preferences = document.Preferences with
            {
                MinimumBalanceReserve = reserve,
                EmergencyFundTargetMonths = months
            }
        };

        if (!_session.TryReplace(updated, out var error))
        {
            ErrorMessage = error;
            return;
        }

        if (!await _session.SaveAsync(cancellationToken))
        {
            ErrorMessage = _session.LastError;
            return;
        }

        ErrorMessage = null;
        StatusMessage = "Planning assumptions saved.";
    }

    [RelayCommand]
    private async Task CreateBackupAsync(string? folder, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        try
        {
            var record = await _backups.CreateBackupAsync(folder, cancellationToken);
            StatusMessage =
                $"Local database backup created ({record.Manifest.FileName}). It contains household financial information. Store it only in a trusted location.";
            ErrorMessage = null;
        }
        catch (Exception exception) when (exception is VaultLockedException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ErrorMessage = "The backup could not be created. Choose a folder you can write to.";
        }
    }

    [RelayCommand]
    private async Task RestoreBackupAsync(string? file, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(file))
        {
            return;
        }

        if (!_dialog.Confirm(
                "Restore backup",
                "Restore will replace the live local database. The backup contains household financial information. Continue only if you have a current copy of this database."))
        {
            return;
        }

        try
        {
            var check = await _backups.ValidateBackupAsync(file, cancellationToken);
            if (!check.IsValid)
            {
                ErrorMessage = check.Problems.Count > 0
                    ? check.Problems[0]
                    : "That file is not a valid local database backup.";
                return;
            }

            await _backups.RestoreBackupAsync(file, cancellationToken);
            if (!_session.Open())
            {
                ErrorMessage = _session.LastError ?? "The backup was restored but the household data could not be reloaded.";
                return;
            }

            StatusMessage = "Backup restored. Household data has been reloaded.";
            ErrorMessage = null;
        }
        catch (Exception exception) when (exception is VaultLockedException or IOException or UnauthorizedAccessException or InvalidOperationException or DatabaseCorruptException)
        {
            ErrorMessage = "The backup could not be restored. The live database was not replaced.";
        }
    }

    [RelayCommand]
    private void PreviewImport(string? file)
    {
        if (string.IsNullOrWhiteSpace(file))
        {
            return;
        }

        try
        {
            var csv = File.ReadAllText(file);
            var preview = BudgetCsvExchange.Preview(csv);
            ImportPreview = preview.Summary;
            ErrorMessage = preview.HasDuplicates
                ? "Duplicate rows were found. They will be skipped if you import."
                : null;
            StatusMessage = "Import preview ready. Nothing has been written yet.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException)
        {
            ErrorMessage = "That file could not be read as a portable export.";
        }
    }

    [RelayCommand]
    private async Task ImportAsync(string? file, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(file) || !_session.IsOpen)
        {
            ErrorMessage = "Open the household database and choose an export file.";
            return;
        }

        if (!_dialog.Confirm(
                "Import data",
                "Import will add portable rows to the local household database. CSV files contain household financial information. Continue?"))
        {
            return;
        }

        try
        {
            var csv = await File.ReadAllTextAsync(file, cancellationToken);
            var merged = BudgetCsvExchange.Merge(_session.Document, csv, out var skipped);
            if (!_session.TryReplace(merged, out var error))
            {
                ErrorMessage = error;
                return;
            }

            if (!await _session.SaveAsync(cancellationToken))
            {
                ErrorMessage = _session.LastError;
                return;
            }

            StatusMessage = skipped.Count == 0
                ? "Import saved."
                : $"Import saved. {skipped.Count} duplicate or incomplete row(s) were skipped.";
            ErrorMessage = null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException)
        {
            ErrorMessage = "The import could not be applied.";
        }
    }

    [RelayCommand]
    private void Export(string? file)
    {
        if (string.IsNullOrWhiteSpace(file) || !_session.IsOpen)
        {
            ErrorMessage = "Open the household database before exporting.";
            return;
        }

        try
        {
            File.WriteAllText(file, BudgetCsvExchange.Export(_session.Document));
            StatusMessage = "Portable CSV written. It contains household financial information and is not encrypted. Store it only in a trusted location.";
            ErrorMessage = null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ErrorMessage = "The export could not be written.";
        }
    }

    [RelayCommand]
    private async Task ClearAllDataAsync(CancellationToken cancellationToken)
    {
        if (!_session.IsOpen)
        {
            ErrorMessage = "Open the household database first.";
            return;
        }

        if (!_dialog.Confirm("Clear all data", "This removes household records from the local database. Existing backups are not deleted.")
            || !_dialog.Confirm("Confirm clear", "This cannot be undone from inside the programme. Clear all household data?"))
        {
            return;
        }

        if (!_session.TryReplace(new BudgetDocument(), out var error))
        {
            ErrorMessage = error;
            return;
        }

        if (!await _session.SaveAsync(cancellationToken))
        {
            ErrorMessage = _session.LastError;
            return;
        }

        StatusMessage = "Household data cleared. The local database file remains.";
        ErrorMessage = null;
    }

    private void RefreshDocumentFields()
    {
        if (!_session.IsOpen)
        {
            MinimumBalance = string.Empty;
            EmergencyMonths = string.Empty;
            ImportPreview = string.Empty;
            DatabaseInfo = "Open the household database to see file details. Uninstalling the programme must not delete the local household file.";
            StatusMessage = null;
            ErrorMessage = null;
            return;
        }

        MinimumBalance = AmountParsing.Format(_session.Document.Preferences.MinimumBalanceReserve);
        EmergencyMonths = AmountParsing.Format(_session.Document.Preferences.EmergencyFundTargetMonths);
        DatabaseInfo =
            $"Local SQLite file {Path.GetFileName(_directory.GetDatabasePath())}. " +
            $"Schema {_store.SchemaVersion}. Stored only on this computer. " +
            "Uninstall must leave that file in place.";
    }

    private int LockTimeoutMinutesSafe()
    {
        var minutes = SelectedTimeoutMinutes > 0 ? SelectedTimeoutMinutes : _preferences.LockTimeoutMinutes;
        return minutes <= 0 ? (int)InactivityLockPolicy.DefaultTimeout.TotalMinutes : minutes;
    }
}
