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
/// <para>
/// Failures are attributed to the entry being indexed on the logging thread, which is correct because the
/// indexer is synchronous: each entry is extracted start to finish on one thread.
/// </para>
/// <para>
/// The indexer's own text quotes folder data (the resource id, the raw value an exception failed on), which is untrusted
/// (agent-tool-surface.md, risk 1) and reaches whoever reads the envelope or stderr. So neither the diagnostic nor what is
/// forwarded to the wrapped logger uses it: both are built from the event's structured state, keeping only the fields the
/// search parameter definitions and the schema supply (<see cref="DefinitionFields"/>), naming the resource as every tool
/// names it (<see cref="FhirIds"/>), and reducing a value and an exception's message to digests.
/// </para>
/// </remarks>
internal static class ExtractionFailureCapture
{
    public const string DiagnosticCode = "index-extraction-failed";

    /// <summary>The event <c>ElementSearchIndexer</c> logs for an element type no converter of the parameter accepts.</summary>
    private const string ElementTypeNotSupportedEvent = "FhirElementTypeNotSupported";

    /// <summary>Event fields that come from the search parameter definitions or the schema, never from a resource, so they are shown.</summary>
    private static readonly HashSet<string> DefinitionFields = new(
        ["SearchParameterUrl", "SearchParameterCode", "FhirPathExpression", "ElementType", "FhirElementType", "ConverterType", "ComponentIndex", "ResourceType"],
        StringComparer.Ordinal);

    /// <summary>Event fields that hold a value read from a resource, shown only as a digest.</summary>
    private static readonly HashSet<string> ValueFields = new(["Value"], StringComparer.Ordinal);

    [ThreadStatic]
    private static Scope? _current;

    /// <summary>Wraps <paramref name="inner"/> so loggers created for <paramref name="indexerCategory"/> feed this capture.</summary>
    public static ILoggerFactory Wrap(ILoggerFactory inner, string indexerCategory) => new CapturingLoggerFactory(inner, indexerCategory);

    /// <summary>
    /// Runs <paramref name="extract"/> and returns the diagnostics logged by the indexer while it ran on this thread,
    /// except the "element type 'canonical' is not supported" warnings for the parameters in
    /// <paramref name="indexedCanonicals"/>'s answer: the parameter URLs whose canonicals another indexer did index on this
    /// resource. The warning is recognised from the log event's structured state (its name, <c>ElementType</c> and
    /// <c>SearchParameterUrl</c>), never its text; any other canonical keeps its warning.
    /// </summary>
    public static (T Result, List<CatalogDiagnostic> Diagnostics) Capture<T>(CatalogEntry entry, Func<T> extract, Func<T, IReadOnlySet<string>> indexedCanonicals)
    {
        ArgumentNullException.ThrowIfNull(extract);
        ArgumentNullException.ThrowIfNull(indexedCanonicals);

        var scope = new Scope(entry);
        Scope? previous = _current;
        _current = scope;
        try
        {
            T result = extract();
            IReadOnlySet<string> covered = indexedCanonicals(result);
            return (result, [.. scope.Captured
                .Where(captured => captured.UnsupportedCanonicalUrl is not { } url || !covered.Contains(url))
                .Select(captured => captured.Diagnostic)]);
        }
        finally
        {
            _current = previous;
        }
    }

    /// <summary>The <c>SearchParameterUrl</c> of an "element type 'canonical' is not supported" event, or null for any other event.</summary>
    private static string? UnsupportedCanonicalUrl<TState>(EventId eventId, TState state)
    {
        if (eventId.Name != ElementTypeNotSupportedEvent || state is not IReadOnlyList<KeyValuePair<string, object?>> fields)
        {
            return null;
        }

        string? elementType = null;
        string? url = null;
        foreach (KeyValuePair<string, object?> field in fields)
        {
            elementType = field.Key == "ElementType" ? field.Value?.ToString() : elementType;
            url = field.Key == "SearchParameterUrl" ? field.Value?.ToString() : url;
        }

        return elementType == "canonical" ? url : null;
    }

    /// <summary>
    /// The event as the tool reports it: its name, the definition fields as they are, value fields and the exception's message
    /// as digests, and the resource being indexed by its tool-wide name. Never the indexer's formatted text.
    /// </summary>
    internal static string Redacted<TState>(EventId eventId, TState state, Exception? exception, CatalogEntry? entry)
    {
        List<string> fields = [];
        if (state is IEnumerable<KeyValuePair<string, object?>> structured)
        {
            foreach ((string key, object? value) in structured)
            {
                if (DefinitionFields.Contains(key))
                {
                    fields.Add($"{key} '{value}'");
                }
                else if (ValueFields.Contains(key) && value is not null)
                {
                    fields.Add($"{key} {FhirIds.Digest(value.ToString() ?? string.Empty)}");
                }
            }
        }

        string name = eventId.Name ?? eventId.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string resource = entry is null ? string.Empty : $" on {FhirIds.Reference(entry)}";
        string detail = fields.Count == 0 ? string.Empty : $": {string.Join(", ", fields)}";
        string failure = exception is null ? string.Empty : $"; {exception.GetType().Name} (message digest {FhirIds.Digest(exception.Message)})";
        return $"The search indexer reported {name}{resource}{detail}{failure}.";
    }

    private sealed class Scope(CatalogEntry entry)
    {
        public CatalogEntry Entry { get; } = entry;

        public List<(CatalogDiagnostic Diagnostic, string? UnsupportedCanonicalUrl)> Captured { get; } = [];
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
            Scope? scope = _current;
            string message = Redacted(eventId, state, exception, scope?.Entry);

            // Forwarded in the redacted form only, without the exception, whose message may quote the resource: the wrapped
            // logger (stderr for the CLI and the MCP server) sees exactly what the diagnostic says.
            if (inner.IsEnabled(logLevel))
            {
                inner.Log(logLevel, eventId, message, null, static (text, _) => text);
            }

            if (logLevel >= LogLevel.Warning && scope is not null)
            {
                scope.Captured.Add((new CatalogDiagnostic(DiagnosticCode, scope.Entry.FilePath, scope.Entry.Line, message), UnsupportedCanonicalUrl(eventId, state)));
            }
        }
    }
}
