using SecureBudgetManager.App.Services;
using SecureBudgetManager.App.Tests.Fakes;
using SecureBudgetManager.Core.Security;

namespace SecureBudgetManager.App.Tests.Security;

public sealed class UserFacingErrorAndLogTests
{
    [Fact]
    public void UnexpectedExceptionsDoNotIncludeEngineOrCipherDetail()
    {
        var message = UserFacingError.From(
            new InvalidOperationException("SQLCipher key pragma failed: SQLITE_NOTADB"));

        Assert.DoesNotContain("SQLCipher", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SQLITE", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pragma", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VaultLockedMessageIsSafeToShow()
    {
        var message = UserFacingError.From(new VaultLockedException());

        Assert.Contains("locked", message, StringComparison.OrdinalIgnoreCase);
        Assert.False(SensitiveLogGuard.ContainsDisallowedContent(message));
    }

    [Fact]
    public async Task SessionLogsNeverIncludeHouseholdNames()
    {
        var logger = new RecordingLogger();
        var repository = new FakeBudgetRepository();
        var session = new BudgetSession(repository, new FakeVault(), logger);
        Assert.True(session.Open());
        Assert.True(session.TryReplace(session.Document with { HouseholdName = "The Harris household" }, out _));
        await session.SaveAsync();

        foreach (var entry in logger.Messages)
        {
            Assert.DoesNotContain("The Harris household", entry, StringComparison.Ordinal);
            Assert.False(SensitiveLogGuard.ContainsDisallowedContent(entry));
        }
    }

    private sealed class RecordingLogger : Microsoft.Extensions.Logging.ILogger<BudgetSession>
    {
        public List<string> Messages { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullDisposable.Instance;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
