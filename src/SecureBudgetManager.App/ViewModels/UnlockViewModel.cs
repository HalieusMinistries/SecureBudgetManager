using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureBudgetManager.Core.Abstractions;
using SecureBudgetManager.Core.Security;

namespace SecureBudgetManager.App.ViewModels;

/// <summary>
/// On-screen privacy concealment. It hides figures while the programme is running. It is not
/// database encryption and is not a security boundary against someone with the Windows files.
/// </summary>
public sealed partial class UnlockViewModel : ObservableObject
{
    private readonly IVaultService _vault;

    public UnlockViewModel(IVaultService vault)
    {
        _vault = vault;
    }

    public event EventHandler? Unlocked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string? lockReason;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public void Reset(string? reason)
    {
        LockReason = reason;
        ErrorMessage = null;
    }

    [RelayCommand]
    private async Task RevealAsync()
    {
        ErrorMessage = null;
        try
        {
            IsBusy = true;
            var result = await _vault.RevealAsync().ConfigureAwait(true);
            if (result.Succeeded)
            {
                Unlocked?.Invoke(this, EventArgs.Empty);
                return;
            }

            ErrorMessage = result.Message;
        }
        catch (VaultException exception)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
