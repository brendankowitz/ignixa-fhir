// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Catalog;
using Microsoft.Extensions.Logging;

namespace Ignixa.CliOnFhir.Indexing;

/// <summary>
/// Turns the warnings and errors <c>ElementSearchIndexer</c> logs into <see cref="CatalogDiagnostic"/>s.
/// The indexer reports a failed search parameter only through <see cref="ILogger"/> and carries on, so
/// without this the caller could not tell a resource that is missing from search results because it
/// does not match from one that could not be indexed.
/// </summary>
/// <remarks>
/// Failures are attributed to the entry being indexed on the logging thread, which is correct because the
/// indexer is synchronous: each entry is extracted start to finish on one thread. Everything is still
/// forwarded to the wrapped logger.
/// </remarks>
internal static class ExtractionFailureCapture
{
    public const string DiagnosticCode = "index-extraction-failed";

    [ThreadStatic]
    private static Scope? _current;

    /// <summary>Wraps <paramref name="inner"/> so loggers created for <paramref name="indexerCategory"/> feed this capture.</summary>
    public static ILoggerFactory Wrap(ILoggerFactory inner, string indexerCategory) => new CapturingLoggerFactory(inner, indexerCategory);

    /// <summary>
    /// Runs <paramref name="extract"/> and returns the diagnostics logged by the indexer while it ran on this thread.
    /// </summary>
    public static (T Result, List<CatalogDiagnostic> Diagnostics) Capture<T>(CatalogEntry entry, Func<T> extract)
    {
        ArgumentNullException.ThrowIfNull(extract);

        var scope = new Scope(entry);
        Scope? previous = _current;
        _current = scope;
        try
        {
            T result = extract();
            return (result, scope.Diagnostics);
        }
        finally
        {
            _current = previous;
        }
    }

    private sealed class Scope(CatalogEntry entry)
    {
        public CatalogEntry Entry { get; } = entry;

        public List<CatalogDiagnostic> Diagnostics { get; } = [];
    }

    private sealed class CapturingLoggerFactory(ILoggerFactory inner, string indexerCategory) : ILoggerFactory
    {
        public ILogger CreateLogger(string categoryName)
        {
            ILogger logger = inner.CreateLogger(categoryName);
            return string.Equals(categoryName, indexerCategory, StringComparison.Ordinal) ? new CapturingLogger(logger) : logger;
        }

        public void AddProvider(ILoggerProvider provider) => inner.AddProvider(provider);

        public void Dispose()
        {
            // The wrapped factory is owned by the caller.
        }
    }

    private sealed class CapturingLogger(ILogger inner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => inner.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning || inner.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (inner.IsEnabled(logLevel))
            {
                inner.Log(logLevel, eventId, state, exception, formatter);
            }

            if (logLevel < LogLevel.Warning || _current is not { } scope)
            {
                return;
            }

            string message = formatter(state, exception);
            if (exception is not null)
            {
                message = $"{message} {exception.GetType().Name}: {exception.Message}";
            }

            scope.Diagnostics.Add(new CatalogDiagnostic(DiagnosticCode, scope.Entry.FilePath, scope.Entry.Line, message));
        }
    }
}
