using SecureBudgetManager.App.Tests.Fakes;
using SecureBudgetManager.Core.Household;

namespace SecureBudgetManager.App.Tests.Services;

public sealed class BudgetSessionUndoTests
{
    [Fact]
    public void UndoRestoresThePreviousDocumentWithoutWriting()
    {
        var (session, repository, _) = SessionFactory.Open();
        var originalName = session.Document.HouseholdName;

        Assert.True(session.TryReplace(session.Document with
        {
            HouseholdName = "Changed",
            Members = [new HouseholdMember { Id = Guid.NewGuid(), Name = "Alex" }]
        }, out _));
        Assert.True(session.CanUndo);
        Assert.Equal(0, repository.SaveCount);

        Assert.True(session.Undo());
        Assert.Equal(originalName, session.Document.HouseholdName);
        Assert.Empty(session.Document.Members);
        Assert.True(session.HasUnsavedChanges);
        Assert.Equal(0, repository.SaveCount);
    }
}
