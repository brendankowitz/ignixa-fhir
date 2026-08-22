namespace Ignixa.FhirPath.Tests.Evaluation.Parity;

/// <summary>
/// The outcome of one full index-parity sweep across the generated and targeted corpora.
/// </summary>
/// <remarks>
/// The two failure lists are reported separately from <see cref="Divergences"/> because they describe
/// different things and the dangerous case only appears in them. An expression that throws contributes
/// no entries, so if the other engine also produced none the resource is not divergent at all - it
/// never reaches <see cref="Divergences"/>. Counting the failures on both sides is the only way that
/// mutual silence becomes visible to an assertion.
/// </remarks>
internal sealed record SearchIndexParityReport(
    int ResourceCount,
    TimeSpan Elapsed,
    IReadOnlyList<SearchIndexDivergence> Divergences,
    IReadOnlyList<ReferenceEvaluationFailure> ReferenceFailures,
    IReadOnlyList<IgnixaEvaluationFailure> IgnixaFailures);
