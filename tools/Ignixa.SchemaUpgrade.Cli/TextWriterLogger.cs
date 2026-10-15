using Microsoft.Extensions.Logging;

namespace Ignixa.SchemaUpgrade.Cli;

/// <summary>
/// Writes log messages at <see cref="LogLevel.Information"/> and above to the CLI's output, so steps shared
/// with SchemaDeployer -- which report their progress only through an <see cref="ILogger"/> -- are visible to
/// the operator, notably the online index build, which can run for hours. Not thread-safe: the CLI runs one
/// step at a time.
/// </summary>
internal sealed class TextWriterLogger(TextWriter output) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
        => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information && logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var message = formatter(state, exception);
        output.WriteLine(logLevel >= LogLevel.Warning ? $"{logLevel.ToString().ToUpperInvariant()}: {message}" : message);
        if (exception is not null)
        {
            output.WriteLine(exception.Message);
        }
    }
}
