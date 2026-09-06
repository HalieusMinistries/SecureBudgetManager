using SecureBudgetManager.Core.Help;

namespace SecureBudgetManager.App.ViewModels;

public sealed class HelpViewModel : PageViewModel
{
    public HelpViewModel()
        : base(
            "Help and About",
            "Guide",
            "Plain-English explanations of household-finance terms. This programme does not give legal, tax, immigration or investment advice.")
    {
    }

    public string ProductName => "Secure Budget Manager";

    public string VersionText => "Version 1.0.2";

    public string PrivacyNote =>
        "Household data stays on this computer in a local SQLite database. Screenshots and CSV exports are not encrypted and contain household financial information.";

    public IReadOnlyList<GlossaryEntry> Entries { get; } = FinanceGlossary.All;
}
