using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureBudgetManager.App.Services;
using SecureBudgetManager.Core.Import;

namespace SecureBudgetManager.App.ViewModels;

/// <summary>
/// Shown after unlock when a pending household payload exists. Nothing is written until Confirm.
/// </summary>
public sealed partial class HouseholdImportPreviewViewModel : ObservableObject
{
    private readonly IPendingHouseholdImport _import;

    public HouseholdImportPreviewViewModel(IPendingHouseholdImport import)
    {
        _import = import;
    }

    public event EventHandler? Finished;

    [ObservableProperty]
    private string sourcePath = string.Empty;

    [ObservableProperty]
    private string createdLocalText = string.Empty;

    [ObservableProperty]
    private string summary = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<string> created = [];

    [ObservableProperty]
    private IReadOnlyList<string> updated = [];

    [ObservableProperty]
    private IReadOnlyList<string> unchanged = [];

    [ObservableProperty]
    private IReadOnlyList<string> conflicts = [];

    [ObservableProperty]
    private IReadOnlyList<string> checks = [];

    [ObservableProperty]
    private bool hasConflicts;

    [ObservableProperty]
    private bool acceptConflicts;

    [ObservableProperty]
    private bool verificationPassed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    [ObservableProperty]
    private bool isBusy;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public string? LastResult { get; private set; }

    public bool LoadFromSession()
    {
        if (!_import.TryInspect(out var offer) || offer is null)
        {
            return false;
        }

        SourcePath = offer.SourcePath;
        CreatedLocalText = offer.CreatedLocal.ToString("yyyy-MM-dd HH:mm");
        Summary = offer.Preview.Summary;
        Created = offer.Preview.Created;
        Updated = offer.Preview.Updated;
        Unchanged = offer.Preview.Unchanged;
        Conflicts = offer.Preview.Conflicts;
        HasConflicts = offer.Preview.HasMaterialConflicts;
        AcceptConflicts = false;
        VerificationPassed = offer.Verification.AllPassed;
        Checks = offer.Verification.Checks
            .Select(check => $"{(check.Passed ? "Passed" : "Failed")} · {check.Name}")
            .ToList();
        ErrorMessage = VerificationPassed
            ? null
            : "Verification failed. The payload will be kept and nothing will be written.";
        return true;
    }

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task ConfirmAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            LastResult = await _import.ApplyConfirmedAsync(AcceptConflicts, cancellationToken).ConfigureAwait(true);
            if (LastResult is not null && LastResult.Contains("saved", StringComparison.OrdinalIgnoreCase))
            {
                Finished?.Invoke(this, EventArgs.Empty);
                return;
            }

            ErrorMessage = LastResult ?? "The import was not applied.";
        }
        finally
        {
            IsBusy = false;
            ConfirmCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanConfirm() => VerificationPassed && !IsBusy && (!HasConflicts || AcceptConflicts);

    [RelayCommand]
    private void Cancel()
    {
        LastResult = "Household import was cancelled. The pending file was kept and the database was not changed.";
        Finished?.Invoke(this, EventArgs.Empty);
    }

    partial void OnAcceptConflictsChanged(bool value)
    {
        _ = value;
        ConfirmCommand.NotifyCanExecuteChanged();
    }
}
