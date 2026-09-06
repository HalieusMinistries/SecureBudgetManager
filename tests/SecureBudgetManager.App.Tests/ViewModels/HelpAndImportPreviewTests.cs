using SecureBudgetManager.App.ViewModels;
using SecureBudgetManager.Core.Help;

namespace SecureBudgetManager.App.Tests.ViewModels;

public sealed class HelpAndImportPreviewTests
{
    [Fact]
    public void HelpListsTheBundledGlossary()
    {
        var help = new HelpViewModel();

        Assert.Equal(FinanceGlossary.All.Count, help.Entries.Count);
        Assert.Contains("1.0.1", help.VersionText, StringComparison.Ordinal);
        Assert.Contains("not encrypted", help.PrivacyNote, StringComparison.OrdinalIgnoreCase);
    }
}
