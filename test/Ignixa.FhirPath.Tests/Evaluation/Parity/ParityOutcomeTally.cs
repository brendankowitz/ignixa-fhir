namespace Ignixa.FhirPath.Tests.Evaluation.Parity;

/// <summary>
/// Counts how each engine pair agreed, not just how many times they were compared.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ParityOutcome.Matches"/> treats a double-throw as agreement and cannot do otherwise -
/// the two SDKs raise unrelated exception types for the same condition. It also treats two empty
/// result sets as agreement. Neither becomes a <c>ParityDivergence</c>, so neither is reachable from
/// any per-divergence assertion, and a sweep reporting "19,647 evaluations, 120 divergences" says
/// nothing about whether the remaining evaluations agreed on real values or agreed by both failing.
/// </para>
/// <para>
/// Every evaluation lands in exactly one of four buckets - divergent, both threw, both empty, or
/// agreed on values - and all four are counted here at the point of observation. The last of them used
/// to be derived instead, as evaluations minus the other three, which made the number carrying the
/// conformance claim depend on the correctness of the counters subtracted from it: a
/// <see cref="BothThrew"/> that stopped incrementing would have raised the agreement count and made
/// its floor easier to satisfy. A counter that guards a claim cannot be the one nothing guards, so it
/// is now observed directly and <c>ResourceParityReport</c> asserts the partition rather than assuming
/// it.
/// </para>
/// </remarks>
internal sealed class ParityOutcomeTally
{
    public int Evaluations { get; private set; }

    public int BothThrew { get; private set; }

    public int BothEmpty { get; private set; }

    /// <summary>
    /// Evaluations where both engines returned the same non-empty results - the only bucket that is
    /// positive evidence the two agree rather than agreement on absence or on mutual failure.
    /// </summary>
    public int AgreedOnValues { get; private set; }

    /// <summary>
    /// Evaluations that reached different observable outcomes. Counted here as well as collected as
    /// <c>ParityDivergence</c> values, so the two are an independent statement of the same fact and a
    /// divergence dropped on the way into the list cannot pass unnoticed.
    /// </summary>
    public int Divergent { get; private set; }

    public void Observe(ParityOutcome firely, ParityOutcome ignixa)
    {
        ArgumentNullException.ThrowIfNull(firely);
        ArgumentNullException.ThrowIfNull(ignixa);

        Evaluations++;

        if (firely.Threw && ignixa.Threw)
        {
            BothThrew++;
        }
        else if (!firely.Threw && !ignixa.Threw && firely.Results.Count == 0 && ignixa.Results.Count == 0)
        {
            BothEmpty++;
        }
        else if (firely.Matches(ignixa))
        {
            AgreedOnValues++;
        }
        else
        {
            Divergent++;
        }
    }
}
