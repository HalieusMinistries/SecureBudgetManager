using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SecureBudgetManager.Core.Abstractions;

namespace SecureBudgetManager.App.ViewModels;

/// <summary>
/// Unused first-run remnant. The programme opens the local database directly.
/// </summary>
public sealed partial class SetupViewModel : ObservableObject
{
    public SetupViewModel(IVaultService vault, ILogger<SetupViewModel> logger)
    {
        _ = vault;
        _ = logger;
    }

    public event EventHandler? SetupCompleted;

    [RelayCommand]
    private void Continue() => SetupCompleted?.Invoke(this, EventArgs.Empty);
}
