using Microsoft.Extensions.Logging.Abstractions;
using SecureBudgetManager.App.Services;
using SecureBudgetManager.App.Tests.Fakes;
using SecureBudgetManager.Core.Security;

namespace SecureBudgetManager.App.Tests.Fakes;

public static class SessionFactory
{
    public static (BudgetSession Session, FakeBudgetRepository Repository, FakeVault Vault) Open()
    {
        var repository = new FakeBudgetRepository();
        var vault = new FakeVault { Status = VaultStatus.Unlocked };
        var session = new BudgetSession(repository, vault, NullLogger<BudgetSession>.Instance);
        Assert.True(session.Open());
        return (session, repository, vault);
    }
}
