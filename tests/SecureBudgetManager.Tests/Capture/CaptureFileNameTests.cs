using SecureBudgetManager.Core.Capture;

namespace SecureBudgetManager.Tests.Capture;

public sealed class CaptureFileNameTests
{
    [Fact]
    public void Suggest_UsesSafePageLabelAndTimestamp()
    {
        var stamp = new DateTimeOffset(2026, 9, 3, 12, 5, 0, TimeSpan.FromHours(-6));
        var name = CaptureFileName.Suggest("Income", stamp);

        Assert.Equal("SecureBudgetManager-Income-2026-09-03-120500.png", name);
        Assert.False(CaptureFileName.ContainsPersonalOrFinancialHint(name));
    }

    [Theory]
    [InlineData("Dashboard")]
    [InlineData("Household")]
    [InlineData("Income")]
    [InlineData("Expenses")]
    [InlineData("Payroll")]
    [InlineData("Cash Flow")]
    [InlineData("Settings")]
    public void Suggest_KnownPageTitles_DoNotIncludePersonalOrFinancialData(string title)
    {
        var name = CaptureFileName.Suggest(title, new DateTimeOffset(2026, 9, 3, 12, 5, 0, TimeSpan.Zero));

        Assert.StartsWith("SecureBudgetManager-", name, StringComparison.Ordinal);
        Assert.EndsWith(".png", name, StringComparison.Ordinal);
        Assert.DoesNotContain("$", name, StringComparison.Ordinal);
        Assert.DoesNotContain("Harris", name, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Warehouse", name, StringComparison.OrdinalIgnoreCase);
        Assert.False(CaptureFileName.ContainsPersonalOrFinancialHint(name));
    }

    [Fact]
    public void Sanitise_StripsUnsafeCharacters()
    {
        Assert.Equal("Page", CaptureFileName.SanitisePageLabel("   "));
        Assert.Equal("Cash-Flow", CaptureFileName.SanitisePageLabel("Cash Flow"));
    }

    [Fact]
    public void ContainsPersonalOrFinancialHint_DetectsMoneyButAllowsHouseholdPageName()
    {
        Assert.False(CaptureFileName.ContainsPersonalOrFinancialHint("SecureBudgetManager-Household-2026-09-03-120500.png"));
        Assert.True(CaptureFileName.ContainsPersonalOrFinancialHint("capture-$1200.png"));
        Assert.True(CaptureFileName.ContainsPersonalOrFinancialHint("totals-12.50.png"));
    }

    [Fact]
    public void SegmentPaths_NumbersExtraFiles()
    {
        var single = CaptureFileName.SegmentPaths(@"C:\shots\page.png", 1);
        Assert.Equal(@"C:\shots\page.png", Assert.Single(single));

        var many = CaptureFileName.SegmentPaths(@"C:\shots\page.png", 2);
        Assert.Equal(@"C:\shots\page-01.png", many[0]);
        Assert.Equal(@"C:\shots\page-02.png", many[1]);
    }
}
