using Microsoft.Extensions.Logging.Abstractions;
using SecureBudgetManager.App.Services;
using SecureBudgetManager.App.Tests.Fakes;
using SecureBudgetManager.Core.Security;
using System.Windows;
using System.Windows.Controls;

namespace SecureBudgetManager.App.Tests.Capture;

[Collection("StaWpf")]
public sealed class FullPageCaptureCommandTests
{
    [Fact]
    public void CanExecute_IsFalseWhileLocked()
    {
        var harness = Harness.Create(VaultStatus.Locked, workspaceVisible: true);
        Assert.False(harness.Command.CanExecute());
        Assert.Null(harness.Command.Execute("Income"));
        Assert.Equal(0, harness.Capture.Calls);
        Assert.Equal(0, harness.Dialogs.ChooseCalls);
    }

    [Theory]
    [InlineData(VaultStatus.Locked)]
    [InlineData(VaultStatus.NotInitialised)]
    [InlineData(VaultStatus.Unavailable)]
    public void AuthenticationStates_CannotCapture(VaultStatus status)
    {
        var harness = Harness.Create(status, workspaceVisible: false);
        Assert.False(harness.Command.CanExecute());
        Assert.Null(harness.Command.Execute("Unlock"));
        Assert.Equal(0, harness.Capture.Calls);
    }

    [Fact]
    public void WorkspaceHidden_CannotCaptureEvenIfVaultIsUnlocked()
    {
        var harness = Harness.Create(VaultStatus.Unlocked, workspaceVisible: false);
        Assert.False(harness.Command.CanExecute());
        Assert.Null(harness.Command.Execute("Income"));
        Assert.Equal(0, harness.Capture.Calls);
    }

    [Fact]
    public void CancellingSaveDialog_CreatesNoFile()
    {
        var folder = CreateEmptyFolder();
        try
        {
            var harness = Harness.Create(VaultStatus.Unlocked, workspaceVisible: true);
            harness.Preferences.FullPageCaptureWarningAcknowledged = true;
            harness.Dialogs.ChosenPath = null;

            Assert.Null(harness.Command.Execute("Income"));
            Assert.Equal(0, harness.Capture.Calls);
            Assert.Empty(Directory.GetFiles(folder));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void CancellingPrivacyWarning_CreatesNoFileAndDoesNotRememberAcknowledgement()
    {
        var harness = Harness.Create(VaultStatus.Unlocked, workspaceVisible: true);
        harness.Dialogs.WarningResult = false;

        Assert.Null(harness.Command.Execute("Income"));
        Assert.Equal(0, harness.Capture.Calls);
        Assert.Equal(0, harness.Dialogs.ChooseCalls);
        Assert.False(harness.Preferences.FullPageCaptureWarningAcknowledged);
    }

    [Fact]
    public void SuggestedFileName_IsSafeAndUsesPageTitleOnly()
    {
        var harness = Harness.Create(VaultStatus.Unlocked, workspaceVisible: true);
        harness.Preferences.FullPageCaptureWarningAcknowledged = true;
        harness.Dialogs.ChosenPath = null;

        harness.Command.Execute("Income");

        Assert.Equal("SecureBudgetManager-Income-2026-09-03-120500.png", harness.Dialogs.LastSuggested);
        Assert.DoesNotContain("Harris", harness.Dialogs.LastSuggested, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("$", harness.Dialogs.LastSuggested, StringComparison.Ordinal);
    }

    [Fact]
    public void SuccessfulExport_DoesNotOpenFolderUnlessAsked()
    {
        var folder = CreateEmptyFolder();
        var dest = Path.Combine(folder, "SecureBudgetManager-Income-2026-09-03-120500.png");
        try
        {
            var harness = Harness.Create(VaultStatus.Unlocked, workspaceVisible: true);
            harness.Preferences.FullPageCaptureWarningAcknowledged = true;
            harness.Dialogs.ChosenPath = dest;
            harness.Dialogs.OpenFolder = false;

            Sta.Run(() =>
            {
                harness.Locator.Page = new Border();
                var message = harness.Command.Execute("Income");
                Assert.Equal("Saved SecureBudgetManager-Income-2026-09-03-120500.png.", message);
                Assert.Equal(1, harness.Capture.Calls);
                Assert.Equal(dest, harness.Capture.LastPath);
                Assert.Null(harness.Folders.Opened);
            });
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static string CreateEmptyFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "sbm-capture-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private sealed class Harness
    {
        private Harness(
            FullPageCaptureCommand command,
            FakeVault vault,
            FakeLocator locator,
            FakeCapture capture,
            FakeDialogs dialogs,
            FakePreferences preferences,
            FakeFolders folders)
        {
            Command = command;
            Vault = vault;
            Locator = locator;
            Capture = capture;
            Dialogs = dialogs;
            Preferences = preferences;
            Folders = folders;
        }

        public FullPageCaptureCommand Command { get; }

        public FakeVault Vault { get; }

        public FakeLocator Locator { get; }

        public FakeCapture Capture { get; }

        public FakeDialogs Dialogs { get; }

        public FakePreferences Preferences { get; }

        public FakeFolders Folders { get; }

        public static Harness Create(VaultStatus status, bool workspaceVisible)
        {
            var vault = new FakeVault { Status = status };
            var locator = new FakeLocator { IsWorkspaceVisible = workspaceVisible };
            var capture = new FakeCapture();
            var dialogs = new FakeDialogs();
            var preferences = new FakePreferences();
            var folders = new FakeFolders();
            var clock = new FixedClock(new DateTimeOffset(2026, 9, 3, 12, 5, 0, TimeSpan.FromHours(-6)));
            var command = new FullPageCaptureCommand(
                vault,
                locator,
                capture,
                dialogs,
                preferences,
                folders,
                clock,
                NullLogger<FullPageCaptureCommand>.Instance);

            return new Harness(command, vault, locator, capture, dialogs, preferences, folders);
        }
    }

    private sealed class FakeLocator : ICaptureVisualLocator
    {
        public bool IsWorkspaceVisible { get; set; }

        public FrameworkElement? Page { get; set; }

        public FrameworkElement? FindActivePage() => Page;
    }

    private sealed class FakeCapture : IFullPageCaptureService
    {
        public int Calls { get; private set; }

        public string? LastPath { get; private set; }

        public FullPageCaptureResult Capture(FrameworkElement page, string destinationPath)
        {
            Calls++;
            LastPath = destinationPath;
            return new FullPageCaptureResult(true, 1, null);
        }
    }

    private sealed class FakeDialogs : ICaptureDialogs
    {
        public bool WarningResult { get; set; } = true;

        public string? ChosenPath { get; set; }

        public bool OpenFolder { get; set; }

        public int ChooseCalls { get; private set; }

        public string? LastSuggested { get; private set; }

        public bool ConfirmPrivacyWarning() => WarningResult;

        public string? ChoosePngPath(string suggestedFileName)
        {
            ChooseCalls++;
            LastSuggested = suggestedFileName;
            return ChosenPath;
        }

        public bool OfferToOpenContainingFolder(string fileNameOnly) => OpenFolder;
    }

    private sealed class FakePreferences : IUiPreferenceStore
    {
        public bool FullPageCaptureWarningAcknowledged { get; set; }

        public int LockTimeoutMinutes { get; set; } = 10;

        public void AcknowledgeFullPageCaptureWarning() => FullPageCaptureWarningAcknowledged = true;

        public void SetLockTimeoutMinutes(int minutes) => LockTimeoutMinutes = minutes;
    }

    private sealed class FakeFolders : IFolderLauncher
    {
        public string? Opened { get; private set; }

        public void OpenContainingFolder(string filePath) => Opened = filePath;
    }

    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedClock(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now.ToUniversalTime();

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.CreateCustomTimeZone(
            "Test",
            _now.Offset,
            "Test",
            "Test");
    }
}
