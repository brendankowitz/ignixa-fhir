using Ignixa.Search.Expressions;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// The compiler's Lower stage: turns a bound Expression tree of ANDed/ORed
/// SearchParameterPredicateExpression leaves, SearchParameterExpression-wrapped composites, and
/// ChainedExpression (forward and reverse chain, any nesting depth, dispatched to
/// <see cref="StructuralContext.LowerChain"/>) into a QueryPlan, and includes/revIncludes (via
/// BuildIncludeStages, Phase 7) into QueryPlan.Includes. Sort is not handled -- see this plan's global
/// constraints for why.
/// </summary>
public static class Lower
{
    public static QueryPlan Run(
        Expression? expression,
        SymbolTable symbols,
        string targetResourceType,
        IReadOnlyList<IncludeExpression> includes,
        IReadOnlyList<IncludeExpression> revIncludes,
        int includeLimit,
        int? top = null)
    {
        var context = new StructuralContext(symbols);
        CteRef match;
        Predicate? outerPredicate = null;

        if (expression is null)
        {
            match = context.LowerResourceSource(targetResourceType);
        }
        else
        {
            var leafContext = new LeafContext(symbols);
            var (remaining, extractedPredicate) = ExtractResourceColumnPredicates(expression, leafContext);
            outerPredicate = extractedPredicate;
            match = remaining is null
                ? context.LowerResourceSource(targetResourceType)
                : LowerNode(remaining, context, targetResourceType);
        }

        var includeStages = BuildIncludeStages(includes, revIncludes, symbols, targetResourceType, includeLimit);
        return new QueryPlan(context.Ctes, match, top, outerPredicate, includeStages);
    }

    private static CteRef LowerNode(Expression expression, StructuralContext context, string resourceType) => expression switch
    {
        SearchParameterPredicateExpression { Modifier.SearchModifierCode: SearchModifierCode.Not } => throw new NotSupportedException(
            "A :not-modified predicate reached leaf dispatch directly, outside a SearchParameterExpression wrapper -- " +
            "the real binder never produces this shape (LowerSearchParameter handles :not for both the single-value " +
            "and comma-separated cases), so this is unexpected input. Throwing rather than silently lowering it as a " +
            "positive match, which is exactly the bug this guard exists to prevent."),
        SearchParameterPredicateExpression leaf => context.Lower(leaf, resourceType),
        SearchParameterExpression sp => LowerSearchParameter(sp, context, resourceType),
        MultiaryExpression { MultiaryOperation: MultiaryOperator.And } and => LowerAnd(and, context, resourceType),
        MultiaryExpression { MultiaryOperation: MultiaryOperator.Or } or => context.Union(
            or.Expressions.Select(e => LowerNode(e, context, resourceType)).ToList()),
        ChainedExpression chain => context.LowerChain(chain, LowerScopedExpression),
        _ => throw new NotSupportedException(
            $"Lower does not support {expression.GetType().Name} yet -- see this plan's scope notes."),
    };

    private static CteRef LowerSearchParameter(SearchParameterExpression sp, StructuralContext context, string resourceType)
    {
        if (sp.Expression is NotExpression not)
        {
            return context.LowerNot(LowerNode(not.Expression, context, resourceType), resourceType);
        }

        if (sp.Expression is SearchParameterPredicateExpression { Modifier.SearchModifierCode: SearchModifierCode.Not } predicate)
        {
            var positiveMatch = new SearchParameterPredicateExpression(predicate.Parameter, predicate.Comparator, modifier: null, predicate.Value);
            return context.LowerNot(context.Lower(positiveMatch, resourceType), resourceType);
        }

        if (TryGetCompositeComponents(sp.Expression, out var components))
        {
            return context.LowerComposite(sp.Parameter, components!, resourceType);
        }

        if (sp.Expression is MultiaryExpression { MultiaryOperation: MultiaryOperator.Or } or
            && or.Expressions.Count > 0
            && or.Expressions.All(e => TryGetCompositeComponents(e, out _)))
        {
            var refs = or.Expressions
                .Select(e =>
                {
                    TryGetCompositeComponents(e, out var alt);
                    return context.LowerComposite(sp.Parameter, alt!, resourceType);
                })
                .ToList();
            return context.Union(refs);
        }

        return LowerNode(sp.Expression, context, resourceType);
    }

    private static bool TryGetCompositeComponents(Expression expression, out IReadOnlyList<CompositeComponentExpression>? components)
    {
        if (expression is MultiaryExpression { MultiaryOperation: MultiaryOperator.And } and
            && and.Expressions.Count > 0
            && and.Expressions.All(e => e is CompositeComponentExpression))
        {
            components = and.Expressions.Cast<CompositeComponentExpression>().ToList();
            return true;
        }

        components = null;
        return false;
    }

    private static CteRef LowerAnd(MultiaryExpression and, StructuralContext context, string resourceType)
    {
        var refs = and.Expressions.Select(e => LowerNode(e, context, resourceType)).ToList();
        var result = refs[0];
        for (var i = 1; i < refs.Count; i++)
        {
            result = context.Intersect(result, refs[i]);
        }
        return result;
    }

    private static (Expression? Remaining, Predicate? OuterPredicate) ExtractResourceColumnPredicates(Expression expression, LeafContext leafContext)
    {
        if (expression is MultiaryExpression { MultiaryOperation: MultiaryOperator.And } and)
        {
            var kept = new List<Expression>();
            Predicate? outer = null;
            foreach (var child in and.Expressions)
            {
                var resourcePredicate = TryExtractResourceColumnPredicate(child, leafContext);
                outer = resourcePredicate is null
                    ? outer
                    : outer is null ? resourcePredicate : new Predicate.And(outer, resourcePredicate);
                if (resourcePredicate is null)
                {
                    kept.Add(child);
                }
            }

            Expression? remaining = kept.Count switch
            {
                0 => null,
                1 => kept[0],
                _ => new MultiaryExpression(MultiaryOperator.And, kept),
            };
            return (remaining, outer);
        }

        var single = TryExtractResourceColumnPredicate(expression, leafContext);
        return single is null ? (expression, null) : (null, single);
    }

    private static Predicate? TryExtractResourceColumnPredicate(Expression expression, LeafContext leafContext)
        => expression is SearchParameterExpression { Expression: SearchParameterPredicateExpression predicate }
            ? ResourceColumnLoweringRule.TryLower(predicate, leafContext)
            : null;

    private static CteRef LowerScopedExpression(Expression expression, StructuralContext context, string resourceType)
    {
        var (remaining, nestedPredicate) = ExtractResourceColumnPredicates(expression, context.LeafContext);
        if (remaining is null)
        {
            return context.LowerResourceSourceWithPredicate(resourceType, nestedPredicate);
        }

        var ordinaryMatch = LowerNode(remaining, context, resourceType);
        return nestedPredicate is null
            ? ordinaryMatch
            : context.Intersect(context.LowerResourceSourceWithPredicate(resourceType, nestedPredicate), ordinaryMatch);
    }

    private readonly record struct ResolvedInclude(
        IncludeExpression Expression,
        IncludeDirection Direction,
        IReadOnlyList<short>? Requires,
        IReadOnlyList<short>? Produces);

    private static IReadOnlyList<IncludeStage>? BuildIncludeStages(
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

        var resolved = includes.Select(e => ResolveInclude(e, IncludeDirection.Forward, symbols))
            .Concat(revIncludes.Select(e => ResolveInclude(e, IncludeDirection.Reverse, symbols)))
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
                // Degenerate case (design doc §2): this stage's EXISTS would have zero branches --
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
        // resolves) -- QueryPlan.Includes must stay null in that case, not an empty-but-non-null list,
        // to preserve "no Includes byte-identical to before this field existed" (QueryPlan.cs remarks).
        return stages.Count == 0 ? null : stages;
    }

    private static ResolvedInclude ResolveInclude(IncludeExpression expression, IncludeDirection direction, SymbolTable symbols)
        => new(expression, direction, ResolveTypeIds(expression.Requires, symbols), ResolveTypeIds(expression.Produces, symbols));

    private static IReadOnlyList<short>? ResolveTypeIds(IReadOnlyCollection<string> types, SymbolTable symbols)
        => types.Contains("*") ? null : types.Select(symbols.ResourceTypeId).ToList();

    private static bool Overlaps(IReadOnlyList<short>? produces, IReadOnlyList<short>? requires)
        => produces is null || requires is null || produces.Any(requires.Contains);

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
                    continue; // A self-referential iterate is not a cycle for this purpose (design §4.4).
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
