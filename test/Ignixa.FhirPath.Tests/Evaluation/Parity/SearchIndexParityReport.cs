namespace Ignixa.FhirPath.Tests.Evaluation.Parity;

/// <summary>
/// The outcome of one full index-parity sweep across the generated and targeted corpora.
/// </summary>
/// <remarks>
/// <see cref="ReferenceFailures"/> is reported separately from <see cref="Divergences"/> because the
/// two describe different things and the dangerous case only appears in the former. A reference-side
/// expression that throws contributes no entries, so if the Ignixa indexer also produced none the
/// resource is not divergent at all - it never reaches <see cref="Divergences"/>. Counting the
/// failures is the only way that mutual silence becomes visible to an assertion.
/// </remarks>
internal sealed record SearchIndexParityReport(
    int ResourceCount,
    TimeSpan Elapsed,
    IReadOnlyList<SearchIndexDivergence> Divergences,
    IReadOnlyList<ReferenceEvaluationFailure> ReferenceFailures);
