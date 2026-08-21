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
/// Separating the counts makes the population itself assertable. Every evaluation lands in exactly one
/// of four buckets - divergent, both threw, both empty, or agreed on values - because a double-throw
/// and a double-empty both satisfy <see cref="ParityOutcome.Matches"/> and so are disjoint from the
/// divergences.
/// </para>
/// </remarks>
internal sealed class ParityOutcomeTally
{
    public int Evaluations { get; private set; }

    public int BothThrew { get; private set; }

    public int BothEmpty { get; private set; }

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
    }
}
