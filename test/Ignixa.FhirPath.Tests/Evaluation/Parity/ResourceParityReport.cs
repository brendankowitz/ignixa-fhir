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
    /// Only the second half of that is a genuine cross-check. <c>ParityOutcomeTally.Observe</c> does
    /// one <c>Evaluations++</c> and takes exactly one bucket branch, so the sum identity is a
    /// structural property of that method rather than an independent statement about it: it catches an
    /// increment that was dropped, and cannot catch one that was filed under the wrong heading. The
    /// divergent count is different - it is compared against a list built on a separate pass through
    /// <c>ParityOutcome.Matches</c>, so those two really are two paths to the same fact.
    /// </para>
    /// <para>
    /// What repaired <see cref="AgreementsOnValues"/> is therefore not this property. It used to be
    /// the subtraction below rather than an observed count, which made the headline number depend on
    /// the counters subtracted from it: a <see cref="BothThrew"/> that stopped incrementing inflated
    /// it and made <c>MinimumAgreementsOnValues</c> easier to satisfy, leaving the counter that guards
    /// the conformance claim the one counter nothing guarded. Observing it directly is what fixed
    /// that, and <c>ParityOutcomeTallyTests</c> is what pins the branch assignment the sum identity
    /// cannot see. This property is the cheap check that the tally did not lose an evaluation on the
    /// way.
    /// </para>
    /// </remarks>
    public bool BucketsPartitionEvaluations =>
        BothThrew + BothEmpty + AgreementsOnValues + DivergentEvaluations == SelectEvaluationsPerEngine
        && DivergentEvaluations == Divergences.Count;
}
