/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Runs a corpus through both engines and collects the disagreements.
 */

namespace Ignixa.FhirPath.Tests.Evaluation.Parity;

/// <summary>
/// Evaluates every expression in a corpus against every subject resource on both engines.
/// </summary>
internal static class ParitySweep
{
    /// <summary>
    /// Runs the corpus and reports both the cases where the engines reached different observable
    /// outcomes and how the rest of the population agreed.
    /// </summary>
    /// <remarks>
    /// Parsing is done once per resource per engine and reused across the whole corpus. With ~1,400
    /// expressions this is the difference between a test that runs in seconds and one nobody waits for.
    /// A resource that one engine cannot parse at all is itself reported rather than thrown, since
    /// "Firely reads this and Ignixa does not" is a parity finding of the highest possible severity.
    /// The outcome tally travels with the divergences because agreement by mutual throw and agreement
    /// on a value are the same thing to a divergence list and completely different things to a
    /// conformance claim - see <see cref="ExpressionParityReport"/>.
    /// </remarks>
    public static ExpressionParityReport Run(IReadOnlyList<string> corpus, string source)
    {
        ArgumentNullException.ThrowIfNull(corpus);

        var divergences = new List<ParityDivergence>();
        var tally = new ParityOutcomeTally();

        foreach (var resource in FirelyParityFixture.Resources)
        {
            CollectForResource(corpus, source, resource, divergences, tally);
        }

        return new ExpressionParityReport(
            tally.Evaluations,
            tally.BothThrew,
            tally.BothEmpty,
            tally.AgreedOnValues,
            tally.Divergent,
            divergences);
    }

    private static void CollectForResource(
        IReadOnlyList<string> corpus,
        string source,
        ParityResource resource,
        List<ParityDivergence> divergences,
        ParityOutcomeTally tally)
    {
        var firelySubject = FirelyEngine.Parse(resource.Json);
        var ignixaSubject = IgnixaEngine.Parse(resource.Json);

        foreach (var expression in corpus)
        {
            var firely = FirelyEngine.Evaluate(firelySubject, expression);
            var ignixa = IgnixaEngine.Evaluate(ignixaSubject, expression);
            tally.Observe(firely, ignixa);

            if (!firely.Matches(ignixa))
            {
                divergences.Add(new ParityDivergence(expression, resource.Name, source, firely, ignixa));
            }
        }
    }
}
