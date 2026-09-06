using Microsoft.Extensions.Logging;

namespace SecureBudgetManager.Tests.TestSupport;

/// <summary>
/// Records every formatted log message so tests can assert that secrets never reach the log.
/// </summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<string> _messages = [];

    public IReadOnlyList<string> Messages => _messages;

    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        _messages.Add(formatter(state, exception));

        if (exception is not null)
        {
            _messages.Add(exception.ToString());
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
