using Microsoft.Extensions.Logging.Abstractions;
using SecureBudgetManager.App.Services;
using SecureBudgetManager.App.Tests.Fakes;
using SecureBudgetManager.App.ViewModels;
using SecureBudgetManager.Core.Household;
using SecureBudgetManager.Core.Security;
using SecureBudgetManager.Core.Storage;

namespace SecureBudgetManager.App.Tests.Services;

public sealed class BudgetSessionTests
{
    [Fact]
    public void OpenFailsWhenTheVaultIsLocked()
    {
        var session = new BudgetSession(
            new FakeBudgetRepository(),
            new FakeVault { Status = VaultStatus.Locked },
            NullLogger<BudgetSession>.Instance);

        Assert.False(session.Open());
        Assert.False(session.IsOpen);
        Assert.Contains("Open the household database", session.LastError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveFailureDoesNotExposeStorageDetail()
    {
        var (session, repository, _) = SessionFactory.Open();
        repository.ThrowOnSave = true;
        repository.SaveException = new InvalidOperationException("PRAGMA cipher_integrity_check failed at page 4");

        Assert.True(session.TryReplace(session.Document with { HouseholdName = "Ours" }, out _));
        var saved = await session.SaveAsync();

        Assert.False(saved);
        Assert.DoesNotContain("cipher", session.LastError, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PRAGMA", session.LastError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DoubleSaveIsRejectedWhileTheFirstSaveIsRunning()
    {
        var (session, repository, _) = SessionFactory.Open();
        repository.ContinueSave = new TaskCompletionSource<bool>();
        Assert.True(session.TryReplace(
            session.Document with
            {
                HouseholdName = "Ours",
                Members = [new HouseholdMember { Id = Guid.NewGuid(), Name = "Alex" }]
            },
            out _));

        var first = session.SaveAsync();
        while (!session.IsSaving)
        {
            await Task.Delay(10);
        }

        var second = await session.SaveAsync();
        repository.ContinueSave.SetResult(true);
        var firstResult = await first;

        Assert.True(firstResult);
        Assert.False(second);
        Assert.Equal(1, repository.SaveCount);
    }

    [Fact]
    public void ClearDropsTheInMemoryDocument()
    {
        var (session, _, _) = SessionFactory.Open();
        Assert.True(session.TryReplace(
            session.Document with
            {
                HouseholdName = "Secret household",
                Members = [new HouseholdMember { Id = Guid.NewGuid(), Name = "Alex" }]
            },
            out _));

        session.Clear();

        Assert.False(session.IsOpen);
        Assert.Equal("Our household", session.Document.HouseholdName);
        Assert.Empty(session.Document.Members);
        Assert.True(session.Document.IsEmpty);
    }

    [Fact]
    public void InvalidDocumentNeverReplacesTheWorkingCopy()
    {
        var (session, _, _) = SessionFactory.Open();
        var original = session.Document;

        var invalid = new BudgetDocument
        {
            Members = [new HouseholdMember { Id = Guid.Empty, Name = "" }]
        };

        Assert.False(session.TryReplace(invalid, out var error));
        Assert.NotNull(error);
        Assert.Same(original, session.Document);
    }
}
