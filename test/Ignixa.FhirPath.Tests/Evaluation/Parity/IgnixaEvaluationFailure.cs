using System.Globalization;
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
/// log the code instead. Empty for <c>FhirElementTypeNotSupported</c>, which is the one event that
/// carries no parameter identity at all - a gap in production's logging, not in this capture.
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
