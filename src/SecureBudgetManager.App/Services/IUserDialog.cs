namespace SecureBudgetManager.App.Services;

/// <summary>
/// Confirmation prompts. The WPF implementation uses a message box; tests substitute a fake.
/// </summary>
public interface IUserDialog
{
    bool Confirm(string title, string message);
}
