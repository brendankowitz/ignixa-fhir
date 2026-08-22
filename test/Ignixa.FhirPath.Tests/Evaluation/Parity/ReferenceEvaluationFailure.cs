using System.Globalization;

namespace Ignixa.FhirPath.Tests.Evaluation.Parity;

/// <summary>
/// One Firely-side evaluation or conversion that threw while projecting the reference search index.
/// </summary>
/// <remarks>
/// <para>
/// The reference projection used to discard these. That made the harness double-blind: a thrown
/// <c>Select</c> contributes no entries, which is indistinguishable from a parameter that legitimately
/// matched nothing, and production <c>ElementSearchIndexer</c> contains its own evaluation failures by
/// design. Both sides could therefore reach an equal - and equally empty - entry set for unrelated
/// reasons, and the plain list equality in <see cref="SearchIndexParityHarness"/> would report parity
/// that was never established.
/// </para>
/// <para>
/// Recording the failure turns the reference side's silence into a value a test can assert on, the way
/// <c>ElementSearchIndexer</c> logs both its expected and its unexpected evaluation failures rather
/// than letting either vanish.
/// </para>
/// </remarks>
/// <param name="ParameterUrl">The search parameter whose extraction failed.</param>
/// <param name="ParameterExpression">
/// The shipped top-level search parameter expression, which is the scope key. The harness compares
/// only entries whose parameter expression both engines compile, so a failure has to be scoped the
/// same way or it would report Firely declining to compile an expression that is out of scope anyway.
/// For a composite component this is the composite's own expression, not the component fragment.
/// </param>
/// <param name="FailingExpression">
/// The expression that actually threw. Equal to <paramref name="ParameterExpression"/> except for
/// composite components, where it is the component fragment.
/// </param>
internal sealed record ReferenceEvaluationFailure(
    string ParameterUrl,
    string ParameterExpression,
    string FailingExpression,
    ReferenceEvaluationStage Stage,
    string ExceptionType)
{
    public static ReferenceEvaluationFailure From(
        Uri parameterUrl,
        string parameterExpression,
        string failingExpression,
        ReferenceEvaluationStage stage,
        Exception exception) =>
        new(
            parameterUrl.ToString(),
            parameterExpression,
            failingExpression,
            stage,
            exception.GetType().Name);

    /// <summary>
    /// Identifies the failing site without the resource it was observed on, so a pin counts how many
    /// subject resources reach one failure rather than collapsing unrelated failures into one bucket.
    /// </summary>
    public string Signature =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{ParameterUrl} :: {Stage} :: {ExceptionType}");

    public string Describe() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{ParameterUrl} [{Stage}] `{FailingExpression}` threw {ExceptionType}");
}
