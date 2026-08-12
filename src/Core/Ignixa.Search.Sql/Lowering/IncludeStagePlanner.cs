using Ignixa.Search.Expressions;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Symbols;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Plans the ordered <see cref="IncludeStage"/> list for a query: resolves each _include/_revinclude to its
/// seed and output type ids, orders :iterate stages after what they depend on, and records what seeds each.
/// </summary>
internal static class IncludeStagePlanner
{
    /// <summary>An include with its direction and its resolved seed (Requires) and output (Produces) type ids; null means wildcard.</summary>
    private readonly record struct ResolvedInclude(
        IncludeExpression Expression,
        IncludeDirection Direction,
        IReadOnlyList<short>? Requires,
        IReadOnlyList<short>? Produces);

    /// <summary>Builds the ordered include stages for a plan. Non-iterate includes run first, iterate includes are
    /// topologically sorted after them, and each stage records which earlier stages (and whether the match
    /// page) seed it. Returns null when there are no includes or every stage is degenerate.</summary>
    internal static IReadOnlyList<IncludeStage>? BuildIncludeStages(
        IReadOnlyList<IncludeExpression> includes,
        IReadOnlyList<IncludeExpression> revIncludes,
        SymbolTable symbols,
        string matchResourceType,
        int includeLimit)
    {
        if (includes.Count == 0 && revIncludes.Count == 0)
        {
            return null;
        }

        var resolved = includes.Select(e => ResolveInclude(e, symbols))
            .Concat(revIncludes.Select(e => ResolveInclude(e, symbols)))
            .ToList();

        var nonIterate = resolved.Where(e => !e.Expression.Iterate).ToList();
        var iterate = resolved.Where(e => e.Expression.Iterate).ToList();
        var ordered = nonIterate.Concat(TopologicalSort(iterate)).ToList();

        var matchTypeId = symbols.ResourceTypeId(matchResourceType);
        var stages = new List<IncludeStage>();
        var stageProduces = new List<IReadOnlyList<short>?>();

        foreach (var entry in ordered)
        {
            var seedStages = new List<int>();
            for (var i = 0; i < stages.Count; i++)
            {
                if (Overlaps(stageProduces[i], entry.Requires))
                {
                    seedStages.Add(i);
                }
            }

            var seedFromMatch = Overlaps([matchTypeId], entry.Requires);

            if (seedStages.Count == 0 && !seedFromMatch)
            {
                // Degenerate case: this stage's EXISTS would have zero branches --
                // unrenderable, and not a real shape any binder-produced Requires/Produces pair
                // should reach in practice. Drop it: it can never produce any rows.
                continue;
            }

            var referenceSearchParamId = entry.Expression.WildCard
                ? (short?)null
                : symbols.SearchParamId(entry.Expression.ReferenceSearchParameter);

            stages.Add(new IncludeStage(
                entry.Direction,
                referenceSearchParamId,
                entry.Requires,
                entry.Produces,
                seedStages,
                seedFromMatch,
                entry.Expression.Iterate,
                includeLimit));
            stageProduces.Add(entry.Produces);
        }

        // Every entry can be dropped as degenerate (e.g. a single :iterate stage whose Requires never
        // resolves) -- QueryPlan.Includes must stay null in that case, not an empty-but-non-null list, so a
        // plan with nothing to include emits exactly what it emitted before includes existed. SqlBuilder
        // branches on null, not on Count, so an empty list would still open an includes CTE.
        return stages.Count == 0 ? null : stages;
    }

    /// <summary>Resolves an include's direction and its seed/output resource-type ids into a <see cref="ResolvedInclude"/>.</summary>
    private static ResolvedInclude ResolveInclude(IncludeExpression expression, SymbolTable symbols)
        => new(
            expression,
            expression.Reversed ? IncludeDirection.Reverse : IncludeDirection.Forward,
            ResolveTypeIds(expression.Requires, symbols),
            ResolveTypeIds(expression.Produces, symbols));

    /// <summary>Resolves a set of resource-type names to ids, or null for a wildcard ("*").</summary>
    private static IReadOnlyList<short>? ResolveTypeIds(IReadOnlyCollection<string> types, SymbolTable symbols)
        => types.Contains("*") ? null : types.Select(symbols.ResourceTypeId).ToList();

    /// <summary>Whether two type-id sets intersect, treating a null (wildcard) set as matching anything.</summary>
    private static bool Overlaps(IReadOnlyList<short>? produces, IReadOnlyList<short>? requires)
        => produces is null || requires is null || produces.Any(requires.Contains);

    /// <summary>
    /// Orders :iterate includes so each runs after every include it depends on (Kahn's algorithm, ties
    /// broken by original position for determinism). Throws if the dependencies form a cycle.
    /// </summary>
    private static List<ResolvedInclude> TopologicalSort(List<ResolvedInclude> entries)
    {
        var n = entries.Count;
        var inDegree = new int[n];
        var edges = new List<int>[n];
        for (var i = 0; i < n; i++)
        {
            edges[i] = [];
        }

        for (var x = 0; x < n; x++)
        {
            for (var y = 0; y < n; y++)
            {
                if (x == y)
                {
                    continue; // A self-referential iterate is not a cycle for this purpose.
                }

                if (Overlaps(entries[x].Produces, entries[y].Requires))
                {
                    edges[x].Add(y);
                    inDegree[y]++;
                }
            }
        }

        var ready = new SortedSet<int>(Enumerable.Range(0, n).Where(i => inDegree[i] == 0));
        var result = new List<ResolvedInclude>();
        while (ready.Count > 0)
        {
            var node = ready.Min;
            ready.Remove(node);
            result.Add(entries[node]);
            foreach (var next in edges[node])
            {
                if (--inDegree[next] == 0)
                {
                    ready.Add(next);
                }
            }
        }

        if (result.Count != n)
        {
            throw new NotSupportedException(
                "Two or more :iterate include expressions form a cycle -- the FHIR spec does not define an " +
                "ordering for this case, and fhir-server rejects it too (PR #1391, " +
                "SearchOperationNotSupportedException). Rewrite the search to remove the mutual dependency.");
        }

        return result;
    }
}
