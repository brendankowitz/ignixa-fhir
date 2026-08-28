using System.Globalization;
using Ignixa.Abstractions;
using Microsoft.Extensions.Logging;

namespace Ignixa.FhirPath.Tests.Evaluation.Parity;

/// <summary>
/// One production-side evaluation, conversion or classification that <c>ElementSearchIndexer</c>
/// contained while building the Ignixa half of the index comparison.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ReferenceEvaluationFailure"/> made the Firely side's silence assertable and stopped
/// there. The Ignixa side had the same hole and kept it: production deliberately catches, logs and
/// continues, so a parameter that throws contributes no entries, which is indistinguishable from one
/// that matched nothing. Constructing the production indexer with a null logger factory - which the
/// harness did - discarded every one of those, so an Ignixa throw against a Firely parameter that
/// legitimately matched nothing compared zero entries against zero entries and scored as agreement.
/// </para>
/// <para>
/// The failures are read out of the production logger rather than from a wrapper around
/// <c>ISearchIndexer</c>, because containment happens per search parameter inside a single
/// <c>Extract</c> call: by the time <c>Extract</c> returns, the failure is already only a log record.
/// </para>
/// </remarks>
/// <param name="Stage">
/// The name of the <c>ElementSearchIndexer.Log</c> method that recorded the failure, which is what
/// distinguishes an expected evaluation miss from an unexpected converter defect.
/// </param>
/// <param name="ParameterUrl">
/// The search parameter being indexed, falling back to its code for the composite-component events that
/// log the code instead. Populated for every event, <c>FhirElementTypeNotSupported</c> included: that one
/// used to record the element type alone, so a signature naming an unindexable type could not say which
/// parameter carried it. <c>ElementSearchIndexer.Log.FhirElementTypeNotSupported</c> now emits
/// <c>SearchParameterUrl</c> as well, which is what lets the pinned skip signatures name a parameter -
/// and what anyone diagnosing the warning on a running server needs.
/// </param>
/// <param name="FailingExpression">
/// The expression that was being evaluated. For a composite component this is the component fragment
/// rather than the composite's own expression, matching what production logs.
/// </param>
/// <param name="ElementType">The FHIR element type the event named, empty when it named none.</param>
/// <param name="ExceptionType">The exception type, empty for the events that carry no exception.</param>
internal sealed record IgnixaEvaluationFailure(
    string Stage,
    string ParameterUrl,
    string FailingExpression,
    string ElementType,
    string ExceptionType)
{
    /// <summary>
    /// The FHIR version whose harness produced this failure.
    /// </summary>
    /// <remarks>
    /// Deliberately not part of <see cref="Signature"/>: the pinned counts aggregate a site across
    /// versions and re-keying them by version would be a re-pin, not a fix. It is carried because a
    /// search parameter URL is not one search parameter - <c>Location-near</c> is <c>Token</c> under STU3
    /// and <c>Special</c> from R4 - so resolving what a failure's parameter <em>was</em> needs the version
    /// it happened in. Set by <see cref="SearchIndexParityHarness.Compare"/>, which is the only place that
    /// knows it.
    /// </remarks>
    public FhirVersion Version { get; init; }

    private const string ParameterUrlKey = "SearchParameterUrl";
    private const string ParameterCodeKey = "SearchParameterCode";
    private const string ExpressionKey = "FhirPathExpression";
    private const string ElementTypeKey = "ElementType";
    private const string FhirElementTypeKey = "FhirElementType";

    public static IgnixaEvaluationFailure From<TState>(EventId eventId, TState state, Exception? exception)
    {
        var fields = state as IReadOnlyList<KeyValuePair<string, object?>> ?? [];

        return new IgnixaEvaluationFailure(
            eventId.Name ?? eventId.Id.ToString(CultureInfo.InvariantCulture),
            Read(fields, ParameterUrlKey) is { Length: > 0 } parameterUrl
                ? parameterUrl
                : Read(fields, ParameterCodeKey),
            Read(fields, ExpressionKey),
            Read(fields, ElementTypeKey) is { Length: > 0 } elementType
                ? elementType
                : Read(fields, FhirElementTypeKey),
            exception?.GetType().Name ?? string.Empty);
    }

    /// <summary>
    /// Whether an exception was contained here, as opposed to the indexer classifying an element as
    /// unindexable and continuing.
    /// </summary>
    /// <remarks>
    /// This is the line between a failure the harness can adjudicate and one it cannot. A contained
    /// throw came out of Ignixa's evaluator or a converter, both of which Firely also exercised, so it
    /// is an observation about two engines. A classification skip comes from the definition manager,
    /// the type inference or the converter manager - one set of objects that
    /// <see cref="SearchIndexParityHarness"/> shares with the reference indexer, so both sides reach
    /// it through the same code and their agreement is structural. The two are pinned separately for
    /// that reason; see <c>ResourceBackedKnownDivergences.ExpectedIgnixaConverterPipelineSkips</c>.
    /// </remarks>
    public bool ContainedAThrow => ExceptionType.Length > 0;

    /// <summary>
    /// Identifies the failing site without the resource it was observed on, so a pin counts how many
    /// subject resources reach one failure rather than collapsing unrelated failures into one bucket.
    /// </summary>
    public string Signature =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{Stage} :: {ParameterUrl} :: {ElementType} :: {ExceptionType}");

    public string Describe() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{ParameterUrl} [{Stage}] `{FailingExpression}` on {ElementType} threw {ExceptionType}");

    private static string Read(IReadOnlyList<KeyValuePair<string, object?>> fields, string key)
    {
        foreach (var field in fields)
        {
            if (string.Equals(field.Key, key, StringComparison.Ordinal))
            {
                return field.Value?.ToString() ?? string.Empty;
            }
        }

        return string.Empty;
    }
}
