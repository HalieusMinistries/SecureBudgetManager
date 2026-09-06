using CommunityToolkit.Mvvm.ComponentModel;

namespace SecureBudgetManager.App.ViewModels;

public abstract class PageViewModel : ObservableObject
{
    protected PageViewModel(string title, string kicker, string summary)
    {
        Title = title;
        Kicker = kicker;
        Summary = summary;
    }

    public string Title { get; }

    public string Kicker { get; }

    public string Summary { get; }
}
