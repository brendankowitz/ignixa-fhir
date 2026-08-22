namespace Ignixa.FhirPath.Tests.Evaluation.Parity;

/// <summary>
/// The outcome of one full <c>Select</c> parity sweep.
/// </summary>
/// <remarks>
/// <see cref="BothThrew"/> and <see cref="BothEmpty"/> record the agreements that produce no
/// divergence and are therefore invisible to every per-divergence assertion. Without them the report
/// cannot distinguish a sweep where the engines agreed on real values from one where they agreed by
/// both failing, and a corpus that silently shrank toward zero would still satisfy a divergence count.
/// <see cref="AgreementsOnValues"/> is the count the conformance claim rests on.
/// </remarks>
internal sealed record ResourceParityReport(
    int ResourceCount,
    int SelectEvaluationsPerEngine,
    int BothThrew,
    int BothEmpty,
    int AgreementsOnValues,
    int DivergentEvaluations,
    TimeSpan Elapsed,
    IReadOnlyList<ParityDivergence> Divergences)
{
    /// <summary>
    /// Whether the four outcome buckets account for every evaluation exactly once, and whether the
    /// divergences that were counted are the divergences that were collected.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="AgreementsOnValues"/> used to be this subtraction rather than an observed count.
    /// That made the headline number depend on the counters subtracted from it: a
    /// <see cref="BothThrew"/> that stopped incrementing inflated it and made
    /// <c>MinimumAgreementsOnValues</c> easier to satisfy, so the counter guarding the conformance
    /// claim was itself unguarded. Counting all four at the point of observation and asserting the
    /// partition here turns that dependency into a cross-check: the two statements are now
    /// independent, and a disagreement between them is a defect in the tally rather than a silently
    /// better-looking result.
    /// </para>
    /// </remarks>
    public bool BucketsPartitionEvaluations =>
        BothThrew + BothEmpty + AgreementsOnValues + DivergentEvaluations == SelectEvaluationsPerEngine
        && DivergentEvaluations == Divergences.Count;
}
