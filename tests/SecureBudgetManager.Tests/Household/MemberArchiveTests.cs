using SecureBudgetManager.Core.Household;
using SecureBudgetManager.Core.Storage;

namespace SecureBudgetManager.Tests.Household;

public sealed class MemberArchiveTests
{
    [Fact]
    public void ArchivedMembersStayAddressableButLeaveCompositionAndDiscretionaryPools()
    {
        var adult = new HouseholdMember { Id = Guid.NewGuid(), Name = "Alex", IsDiscretionaryEligible = true };
        var archived = new HouseholdMember
        {
            Id = Guid.NewGuid(),
            Name = "Sam",
            IsDiscretionaryEligible = true,
            IsArchived = true
        };
        var child = new HouseholdMember { Id = Guid.NewGuid(), Name = "Child", IsDependant = true };

        var document = new BudgetDocument
        {
            HouseholdName = "Sample",
            Members = [adult, archived, child]
        };

        Assert.Equal(1, document.Composition.Adults);
        Assert.Equal(1, document.Composition.Children);
        Assert.Single(document.DiscretionaryEligibleMembers);
        Assert.Equal("Alex", document.DiscretionaryEligibleMembers[0].Name);
        Assert.NotNull(document.FindMember(archived.Id));
        Assert.False(archived.IsCurrentOn(new DateOnly(2026, 9, 4)));
    }

    [Fact]
    public void AHouseholdCannotArchiveItsLastAdult()
    {
        var adult = new HouseholdMember { Id = Guid.NewGuid(), Name = "Alex", IsArchived = true };
        var household = new SecureBudgetManager.Core.Household.Household
        {
            Name = "Sample",
            Members = [adult]
        };

        var error = Record.Exception(household.Validate);
        Assert.IsType<ArgumentException>(error);
        Assert.Contains("active adult", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
