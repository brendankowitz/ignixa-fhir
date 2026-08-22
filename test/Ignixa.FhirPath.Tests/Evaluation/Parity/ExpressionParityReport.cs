namespace Ignixa.FhirPath.Tests.Evaluation.Parity;

/// <summary>
/// The outcome of one expression-corpus sweep: every expression in the corpus against every subject
/// resource, classified by how the two engines agreed rather than only by where they disagreed.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ParitySweep"/> used to return a divergence list and nothing else, which made its two
/// headline facts - roughly 1,400 R4 search parameter expressions across five resources - assertable
/// only through the divergences they did not produce. Mutual throws are live in this corpus and always
/// have been: <see cref="KnownDivergences"/> records that the <c>hasExtension()</c> parameter is
/// pinned at four rather than five precisely because both engines throw on the fifth subject. An
/// evaluation that turns into a mutual throw therefore leaves the divergence list unchanged and the
/// suite green, which is the same blindness <c>ResourceParityReport</c> was given a tally to close.
/// </para>
/// <para>
/// The remedy is deliberately at the sweep rather than in <see cref="ParityOutcome.Matches"/>. Making
/// a double-throw a divergence would mean comparing exception types across two unrelated SDK
/// hierarchies, which reports every mutually-agreed error as a disagreement and buries the real ones.
/// Counting the outcome classes keeps <c>Matches</c> as it is and still makes the population
/// assertable.
/// </para>
/// </remarks>
internal sealed record ExpressionParityReport(
    int EvaluationsPerEngine,
    int BothThrew,
    int BothEmpty,
    int AgreementsOnValues,
    int DivergentEvaluations,
    IReadOnlyList<ParityDivergence> Divergences)
{
    /// <summary>
    /// Whether the four outcome buckets account for every evaluation exactly once, and whether the
    /// divergences that were counted are the divergences that were collected.
    /// </summary>
    public bool BucketsPartitionEvaluations =>
        BothThrew + BothEmpty + AgreementsOnValues + DivergentEvaluations == EvaluationsPerEngine
        && DivergentEvaluations == Divergences.Count;

    public string Describe() =>
        $"{EvaluationsPerEngine} evaluations per engine; {Divergences.Count} divergences; "
        + $"{BothThrew} both threw; {BothEmpty} both empty; {AgreementsOnValues} agreed on values.";
}
