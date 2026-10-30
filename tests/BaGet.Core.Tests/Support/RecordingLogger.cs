using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace BaGet.Core.Tests.Support;

/// <summary>
/// An <see cref="ILogger{T}"/> that keeps what was logged, so a test can assert that a
/// rejection said why it happened. A rejection whose only trace is a stack trace, or no
/// trace at all, cannot be diagnosed by whoever uploaded the package.
/// </summary>
public class RecordingLogger<T> : ILogger<T>
{
    private readonly List<LogEntry> _entries = new();

    public IReadOnlyList<LogEntry> Entries => _entries;

    public IEnumerable<LogEntry> Errors => _entries.Where(e => e.Level == LogLevel.Error);

    #pragma warning disable CS8633
    public IDisposable BeginScope<TState>(TState state) => new NoopScope();
    #pragma warning restore CS8633

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        _entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
    }

    public class LogEntry
    {
        public LogEntry(LogLevel level, string message, Exception? exception)
        {
            Level = level;
            Message = message;
            Exception = exception;
        }

        public LogLevel Level { get; }
        public string Message { get; }
        public Exception? Exception { get; }

        public override string ToString() => $"{Level}: {Message}";
    }

    private class NoopScope : IDisposable
    {
        public void Dispose()
        {
        }
    }
}