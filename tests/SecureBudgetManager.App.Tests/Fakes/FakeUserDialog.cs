using SecureBudgetManager.App.Services;

namespace SecureBudgetManager.App.Tests.Fakes;

public sealed class FakeUserDialog : IUserDialog
{
    public bool NextResult { get; set; } = true;

    public int ConfirmCount { get; private set; }

    public string? LastTitle { get; private set; }

    public string? LastMessage { get; private set; }

    public bool Confirm(string title, string message)
    {
        ConfirmCount++;
        LastTitle = title;
        LastMessage = message;
        return NextResult;
    }
}
