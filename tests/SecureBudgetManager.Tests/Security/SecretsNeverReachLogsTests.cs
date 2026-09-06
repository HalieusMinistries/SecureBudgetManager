using Microsoft.Extensions.Logging.Abstractions;
using SecureBudgetManager.Core.Security;
using SecureBudgetManager.Infrastructure.Security;
using SecureBudgetManager.Infrastructure.Storage;
using SecureBudgetManager.Tests.TestSupport;

namespace SecureBudgetManager.Tests.Security;

public sealed class SecretsNeverReachLogsTests
{
    [Fact]
    public async Task NoLogMessageContainsASuppliedSecret()
    {
        using var directory = new TempVaultDirectory();
        var vaultLogger = new CapturingLogger<VaultService>();
        using var store = new SqliteBudgetStore(directory, NullLogger<SqliteBudgetStore>.Instance);
        using var vault = new VaultService(store, vaultLogger);
        var unused = "correct-horse-battery-staple".ToCharArray();

        await vault.InitialiseAsync(unused, MasterSecretKind.Password);
        vault.Lock();
        await vault.RevealAsync();

        Assert.NotEmpty(vaultLogger.Messages);

        foreach (var message in vaultLogger.Messages)
        {
            Assert.DoesNotContain("correct-horse-battery-staple", message, StringComparison.OrdinalIgnoreCase);
            Assert.False(SensitiveLogGuard.ContainsDisallowedContent(message));
        }
    }

    [Fact]
    public void UnlockFailureMessagesNeverEchoTheAttemptedSecret()
    {
        var result = UnlockResult.Failure(UnlockFailureReason.IncorrectSecret, "That was incorrect.");

        Assert.DoesNotContain("correct-horse-battery-staple", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SensitiveLogGuardCatchesTheObviousMistakes()
    {
        Assert.True(SensitiveLogGuard.ContainsDisallowedContent("user password is hunter2"));
        Assert.True(SensitiveLogGuard.ContainsDisallowedContent("encryption key rotated to abc"));
        Assert.True(SensitiveLogGuard.ContainsDisallowedContent("card number 4111111111111111"));
        Assert.False(SensitiveLogGuard.ContainsDisallowedContent("Workspace opened."));
    }
}
