namespace Ignixa.FhirPath.Tests.Evaluation.Parity;

/// <summary>
/// The outcome of one full <c>Select</c> parity sweep.
/// </summary>
/// <remarks>
/// <see cref="BothThrew"/> and <see cref="BothEmpty"/> record the agreements that produce no
/// divergence and are therefore invisible to every per-divergence assertion. Without them the report
/// cannot distinguish a sweep where the engines agreed on real values from one where they agreed by
/// both failing, and a corpus that silently shrank toward zero would still satisfy a divergence count.
/// <see cref="AgreementsOnValues"/> is what remains once both are removed, and is the count the
/// conformance claim rests on.
/// </remarks>
internal sealed record ResourceParityReport(
    int ResourceCount,
    int SelectEvaluationsPerEngine,
    int BothThrew,
    int BothEmpty,
    TimeSpan Elapsed,
    IReadOnlyList<ParityDivergence> Divergences)
{
    /// <summary>
    /// Evaluations where both engines returned the same non-empty results.
    /// </summary>
    /// <remarks>
    /// Every evaluation lands in exactly one of four buckets - divergent, both threw, both empty, or
    /// agreed on values - because <c>ParityOutcome.Matches</c> makes a double-throw and a double-empty
    /// agreement, and therefore disjoint from the divergences. Subtracting the three counted buckets
    /// yields the fourth, which is the only one that is positive evidence rather than agreement on
    /// absence or on mutual failure.
    /// </remarks>
    public int AgreementsOnValues =>
        SelectEvaluationsPerEngine - BothThrew - BothEmpty - Divergences.Count;
}
