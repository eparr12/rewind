using Microsoft.Extensions.Logging;

namespace Rewind.Core.Tests;

/// <summary>Records every entry every logger writes, at every level, instead of writing it anywhere.</summary>
internal sealed class ListLoggerProvider : ILoggerProvider
{
    private readonly List<(string Category, LogLevel Level, string Message)> _entries = [];

    public IReadOnlyList<(string Category, LogLevel Level, string Message)> Entries
    {
        get
        {
            lock (_entries)
            {
                return [.. _entries];
            }
        }
    }

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void Dispose()
    {
    }

    private void Add(string category, LogLevel level, string message)
    {
        lock (_entries)
        {
            _entries.Add((category, level, message));
        }
    }

    private sealed class Logger(ListLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            provider.Add(category, logLevel, formatter(state, exception));
    }
}
