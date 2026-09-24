using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.SqlServer.Tests.Fixtures;

/// <summary>
/// Captures formatted log messages by level, so a test can assert that a silent-data-loss path logs where
/// the loss actually happens.
/// </summary>
public sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly List<(LogLevel Level, string Message)> _entries = [];

    public IReadOnlyList<string> Errors => Messages(LogLevel.Error);

    public IReadOnlyList<string> Messages(LogLevel level)
        => _entries.Where(entry => entry.Level == level).Select(entry => entry.Message).ToList();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
        => _entries.Add((logLevel, formatter(state, exception)));
}
