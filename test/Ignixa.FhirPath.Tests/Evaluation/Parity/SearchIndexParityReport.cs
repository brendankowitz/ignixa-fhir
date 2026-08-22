namespace Ignixa.FhirPath.Tests.Evaluation.Parity;

/// <summary>
/// The outcome of one full index-parity sweep across the generated and targeted corpora.
/// </summary>
/// <remarks>
/// <para>
/// The failure lists are reported separately from <see cref="Divergences"/> because they describe
/// different things and the dangerous case only appears in them. An expression that throws contributes
/// no entries, so if the other engine also produced none the resource is not divergent at all - it
/// never reaches <see cref="Divergences"/>. Counting the failures on both sides is the only way that
/// mutual silence becomes visible to an assertion.
/// </para>
/// <para>
/// The entry counts are here for the same reason one level up. Divergence classification and failure
/// pins both say what went wrong; neither says how much was compared, so a change that halved every
/// parameter's output would satisfy both and lose half the evidence silently.
/// </para>
/// </remarks>
internal sealed record SearchIndexParityReport(
    int ResourceCount,
    int FirelyEntriesCompared,
    int IgnixaEntriesCompared,
    TimeSpan Elapsed,
    IReadOnlyList<SearchIndexDivergence> Divergences,
    IReadOnlyList<ReferenceEvaluationFailure> ReferenceFailures,
    IReadOnlyList<IgnixaEvaluationFailure> IgnixaFailures)
{
    /// <summary>
    /// Every canonicalised entry either engine contributed to the comparison.
    /// </summary>
    public int EntriesCompared => FirelyEntriesCompared + IgnixaEntriesCompared;
}
