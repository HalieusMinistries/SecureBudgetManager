using System.Windows;

namespace SecureBudgetManager.App.Services;

public sealed class WpfUserDialog : IUserDialog
{
    public bool Confirm(string title, string message)
    {
        var result = MessageBox.Show(
            message,
            string.IsNullOrWhiteSpace(title) ? "Secure Budget Manager" : title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);

        return result == MessageBoxResult.Yes;
    }
}
