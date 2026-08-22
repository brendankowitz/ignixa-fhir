using Microsoft.Extensions.Logging;

namespace Ignixa.FhirPath.Tests.Evaluation.Parity;

/// <summary>
/// The logger factory the parity harness hands to the production search indexer, so the failures that
/// indexer contains become values a test can assert on instead of vanishing.
/// </summary>
/// <remarks>
/// <para>
/// <c>ElementSearchIndexer</c> catches evaluation and conversion failures per search parameter, logs
/// them and continues - that containment is deliberate, because failing a write over one unindexable
/// parameter is worse than the missing parameter. The consequence for a differential harness is that
/// the log is the only remaining evidence, so a <c>NullLoggerFactory</c> makes the harness blind to
/// exactly the outcome it exists to detect.
/// </para>
/// <para>
/// The sink is <see cref="AsyncLocal{T}"/> because the harness caches one indexer per FHIR version and
/// xUnit runs test classes in parallel, so a shared buffer would attribute one test's failures to
/// another. An entry logged with no sink installed throws rather than being dropped: the only caller
/// that installs this factory is <see cref="SearchIndexParityHarness"/>, and a silent drop here would
/// reintroduce the defect this type exists to close.
/// </para>
/// </remarks>
internal sealed class IgnixaFailureCapture : ILoggerFactory, ILogger
{
    private static readonly AsyncLocal<List<IgnixaEvaluationFailure>?> Sink = new();

    public static IgnixaFailureCapture Instance { get; } = new();

    /// <summary>
    /// Runs <paramref name="work"/> with a fresh sink installed and returns everything the production
    /// indexer contained while it ran.
    /// </summary>
    public static IReadOnlyList<IgnixaEvaluationFailure> While(Action work)
    {
        ArgumentNullException.ThrowIfNull(work);

        var failures = new List<IgnixaEvaluationFailure>();
        var previous = Sink.Value;
        Sink.Value = failures;
        try
        {
            work();
        }
        finally
        {
            Sink.Value = previous;
        }

        return failures;
    }

    public ILogger CreateLogger(string categoryName) => this;

    public void AddProvider(ILoggerProvider provider) =>
        throw new NotSupportedException("The parity capture is the only sink; adding a provider would split the record.");

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (Sink.Value is not { } failures)
        {
            throw new InvalidOperationException(
                $"The production indexer logged '{eventId.Name}' outside a capture scope. "
                + "Route every Extract call through IgnixaFailureCapture.While so contained failures stay assertable.");
        }

        failures.Add(IgnixaEvaluationFailure.From(eventId, state, exception));
    }

    public void Dispose()
    {
    }
}
