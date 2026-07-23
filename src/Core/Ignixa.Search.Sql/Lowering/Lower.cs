using Ignixa.Search.Expressions;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// The compiler's Lower stage: turns a bound Expression tree into a <see cref="QueryPlan"/>. It handles
/// ANDed/ORed predicate leaves, wrapped composites, forward and reverse chains at any nesting depth,
/// compartment searches, and _include/_revinclude/_sort/paging. The resulting plan is a pure value; all
/// I/O already happened in Resolve. CountOnly is the only "count instead of rows" concept the compiler
/// exposes — there is no _total vocabulary here.
/// </summary>
public static class Lower
{
    /// <summary>
    /// Lowers a whole search into a QueryPlan: extracts resource-column predicates into an outer WHERE,
    /// lowers the remaining expression (or a bare resource source when there is none) into the CTE graph,
    /// then attaches include stages, a sort spec, and paging. A null target resource type is allowed for a
    /// wildcard compartment search, or -- when <paramref name="systemLevelSearch"/> is true -- for a
    /// system-level (cross-type) search of ordinary leaf/composite/And/Or predicates and the bare
    /// resource-column base case. Even under system-level search, chain, :not/:missing=true, _include, and
    /// _revinclude still require a single target type and throw; wildcard compartment search
    /// (<paramref name="systemLevelSearch"/> false) keeps throwing for every combination it never supported.
    /// </summary>
    public static LoweredPlan Run(
        Expression? expression,
        SymbolTable symbols,
        string? targetResourceType,
        IReadOnlyList<IncludeExpression> includes,
        IReadOnlyList<IncludeExpression> revIncludes,
        int includeLimit,
        IReadOnlyList<SortExpression> sort,
        SortPhase sortPhase,
        PageSpec? page,
        bool countOnly = false,
        int? top = null,
        bool systemLevelSearch = false,
        DateTimeOffset? approximationReferenceTime = null,
        OffsetSpec? offsetPage = null,
        bool countPhaseScoped = false,
        (long Start, long End)? surrogateIdRange = null)
    {
        if (offsetPage is not null && (page is not null || top is not null))
        {
            throw new NotSupportedException(
                "offsetPage cannot be combined with the keyset page boundary or with top -- T-SQL forbids TOP " +
                "and OFFSET in the same query (error 10741), and offset-mode paging and keyset paging are " +
                "distinct, non-composable pagination models. page+top together remains valid -- that is keyset " +
                "paging's own existing call shape (top is keyset's page-size mechanism), unaffected by this guard.");
        }

        if (countPhaseScoped && !(countOnly && sort.Count > 0))
        {
            throw new ArgumentException(
                "countPhaseScoped is only meaningful combined with countOnly: true and a non-empty sort -- it asks " +
                "'how many rows would this specific sort phase's own join produce', not the whole match set's count " +
                "(that's what countOnly alone already does, unconditionally). Without both, there is no phase to " +
                "scope the count to.",
                nameof(countPhaseScoped));
        }

        var context = new StructuralContext(symbols, approximationReferenceTime);
        CteRef match;
        Predicate? outerPredicate = null;

        if (expression is null)
        {
            match = context.LowerResourceSource(RequireResourceType(targetResourceType, systemLevelSearch));
        }
        else
        {
            var (remaining, extractedPredicate) = ExtractResourceColumnPredicates(expression, context.LeafContext);
            outerPredicate = extractedPredicate;
            match = remaining switch
            {
                null => context.LowerResourceSource(RequireResourceType(targetResourceType, systemLevelSearch)),
                CompartmentSearchExpression compartment => context.LowerCompartment(compartment),
                PatientEverythingExpression everything => context.LowerPatientEverything(everything),
                _ when targetResourceType is null && !systemLevelSearch => throw new NotSupportedException(
                    "A search with no single target resource type (a wildcard compartment search) can only " +
                    "combine with a CompartmentSearchExpression and resource-column predicates -- an ordinary " +
                    "typed search parameter alongside it has no single resource type to scope it against, " +
                    "which this phase does not support."),
                _ => LowerNode(remaining, context, targetResourceType), // null only under system-level search; LowerNode's chain guard still fires.
            };
        }

        if (targetResourceType is null && !systemLevelSearch && sort.Count > 0)
        {
            throw new NotSupportedException(
                "_sort combined with a wildcard compartment search (no single target resource type) is not " +
                "supported -- a SortSpec needs a single ResourceTypeId scope for its joins, the same reasoning " +
                "already established for typed leaves and _include/_revinclude under a null scope.");
        }

        IReadOnlyList<IncludeStage>? includeStages;
        if (targetResourceType is null)
        {
            if (includes.Count > 0 || revIncludes.Count > 0)
            {
                var noTargetTypeReason = systemLevelSearch ? "a system-level search" : "a wildcard compartment search";
                throw new NotSupportedException(
                    $"_include/_revinclude combined with {noTargetTypeReason} (no single target resource type) is " +
                    "not supported -- BuildIncludeStages needs a concrete match resource type to compute " +
                    "SeedFromMatch.");
            }

            includeStages = null;
        }
        else
        {
            includeStages = BuildIncludeStages(includes, revIncludes, symbols, targetResourceType, includeLimit);
        }

        var sortSpec = BuildSortSpec(sort, sortPhase, symbols);

        if (surrogateIdRange is { } range)
        {
            var table = SqlCatalog.Default.Table("Resource");
            var column = new SqlColumnRef(table.TableName, "ResourceSurrogateId");
            var rangePredicate = new Predicate.And(
                new Predicate.GreaterThanOrEqual(column, new SqlParameterRef(range.Start)),
                new Predicate.LessThanOrEqual(column, new SqlParameterRef(range.End)));
            outerPredicate = outerPredicate is null ? rangePredicate : new Predicate.And(outerPredicate, rangePredicate);
        }

        return new LoweredPlan(
            new QueryPlan(context.Ctes, match, top, outerPredicate, includeStages, sortSpec, page, countOnly, offsetPage, countPhaseScoped),
            new PlanProvenance(context.Origins));
    }

    /// <summary>Returns the target resource type, or throws if it is null where one is required. A null type is
    /// allowed only under system-level search (the bare/resource-column-only base case); a wildcard compartment
    /// search never reaches here because its remaining expression is a CompartmentSearchExpression, not null.</summary>
    private static string? RequireResourceType(string? targetResourceType, bool systemLevelSearch)
        => targetResourceType is not null || systemLevelSearch
            ? targetResourceType
            : throw new NotSupportedException(
                "targetResourceType is required unless the top-level expression is a compartment search with no single target resource type.");

    /// <summary>Dispatches one expression node to the lowering path for its kind (leaf, missing, composite, AND, OR, chain, or compartment).
    /// A null <paramref name="resourceType"/> reaches here only under system-level search; chain has its own explicit
    /// guard below because a ChainedExpression never consumes the ambient type (it carries its own), so the
    /// null-type guards in <see cref="Run"/> would otherwise let a type-less chain fall through and "work".</summary>
    private static CteRef LowerNode(Expression expression, StructuralContext context, string? resourceType) => expression switch
    {
        SearchParameterPredicateExpression { Modifier.SearchModifierCode: SearchModifierCode.Not } => throw new NotSupportedException(
            "A :not-modified predicate reached leaf dispatch directly, outside a SearchParameterExpression wrapper -- " +
            "the real binder never produces this shape (LowerSearchParameter handles :not for both the single-value " +
            "and comma-separated cases), so this is unexpected input. Throwing rather than silently lowering it as a " +
            "positive match, which is exactly the bug this guard exists to prevent."),
        SearchParameterPredicateExpression leaf => context.Lower(leaf, resourceType),
        MissingSearchParameterExpression missing => LowerMissing(missing, context, resourceType),
        SearchParameterExpression sp => LowerSearchParameter(sp, context, resourceType),
        MultiaryExpression { MultiaryOperation: MultiaryOperator.And } and => LowerAnd(and, context, resourceType),
        MultiaryExpression { MultiaryOperation: MultiaryOperator.Or } or => context.Union(
            or.Expressions.Select(e => LowerNode(e, context, resourceType)).ToList()),
        ChainedExpression when resourceType is null => throw new NotSupportedException(
            "Chain is not supported in system-level search in this phase -- a chain resolves and joins against a " +
            "concrete referencing/target type, which a cross-type search has no single value for. Guarding at the " +
            "chain dispatch choke point covers a top-level chain and a chain nested in an And equally; a chain " +
            "reached inside another chain's scope always has a concrete type and never trips this."),
        ChainedExpression chain => context.LowerChain(chain, LowerScopedExpression),
        CompartmentSearchExpression compartment => context.LowerCompartment(compartment),
        NotReferencedExpression notReferenced => context.LowerNotReferenced(notReferenced, resourceType),
        _ => throw new NotSupportedException(
            $"Lower does not support {expression.GetType().Name} yet -- see this plan's scope notes."),
    };

    /// <summary>
    /// Lowers a wrapped search parameter, unwrapping the wrapper's own semantics first: a NotExpression or
    /// a :not-modified predicate becomes a negation, a single composite or an OR of composite alternatives
    /// becomes composite lowering, and anything else falls through to <see cref="LowerNode"/>.
    /// </summary>
    private static CteRef LowerSearchParameter(SearchParameterExpression sp, StructuralContext context, string? resourceType)
    {
        if (sp.Expression is NotExpression not)
        {
            return context.LowerNot(LowerNode(not.Expression, context, resourceType), resourceType);
        }

        if (sp.Expression is SearchParameterPredicateExpression { Modifier.SearchModifierCode: SearchModifierCode.Not } predicate)
        {
            var positiveMatch = new SearchParameterPredicateExpression(predicate.Parameter, predicate.Comparator, modifier: null, predicate.Value)
            {
                Span = predicate.Span,
            };
            return context.LowerNot(context.Lower(positiveMatch, resourceType, provenanceNode: predicate), resourceType);
        }

        if (sp.Expression is StringExpression { FieldName: FieldName.TokenText } text)
        {
            return context.LowerTokenText(sp.Parameter, text, resourceType, provenanceNode: sp);
        }

        if (TryGetCompositeComponents(sp.Expression, out var components))
        {
            return context.LowerComposite(sp.Parameter, components!, resourceType, provenanceNode: sp);
        }

        if (sp.Expression is MultiaryExpression { MultiaryOperation: MultiaryOperator.Or } or
            && or.Expressions.Count > 0
            && or.Expressions.All(e => TryGetCompositeComponents(e, out _)))
        {
            var refs = or.Expressions
                .Select(e =>
                {
                    TryGetCompositeComponents(e, out var alt);
                    return context.LowerComposite(sp.Parameter, alt!, resourceType, provenanceNode: e);
                })
                .ToList();
            return context.Union(refs);
        }

        return LowerNode(sp.Expression, context, resourceType);
    }

    /// <summary>Lowers a :missing search to the parameter's presence set, negated when :missing=true.</summary>
    private static CteRef LowerMissing(MissingSearchParameterExpression missing, StructuralContext context, string? resourceType)
    {
        var presence = context.LowerParameterPresence(missing.Parameter, resourceType);
        return missing.IsMissing ? context.LowerNot(presence, resourceType) : presence;
    }

    /// <summary>Returns true and the components when the expression is an AND of composite components; false otherwise.</summary>
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

    /// <summary>
    /// Lowers an AND by intersecting its positive children, then subtracting each negated child from that
    /// intersection. Lowering a negation on its own has to anchor it on every resource of the type just to
    /// subtract from something (see <see cref="StructuralContext.LowerNot"/>); inside an AND the positive
    /// siblings are already a smaller anchor, and `A AND NOT B` is `A EXCEPT B`. With no positive sibling
    /// there is nothing smaller to subtract from, so the ResourceSource anchor is still the only option.
    /// </summary>
    private static CteRef LowerAnd(MultiaryExpression and, StructuralContext context, string? resourceType)
    {
        var positives = new List<Expression>();
        var negated = new List<Expression>();
        foreach (var child in and.Expressions)
        {
            var inner = TryGetNegatedInner(child);
            (inner is null ? positives : negated).Add(inner ?? child);
        }

        if (negated.Count == 0)
        {
            return Intersect(positives, context, resourceType);
        }

        // The positives must be lowered first: an Except may only reference CTEs already defined above it.
        var result = positives.Count > 0
            ? Intersect(positives, context, resourceType)
            : context.LowerResourceSource(resourceType);

        foreach (var inner in negated)
        {
            result = context.Except(result, LowerNode(inner, context, resourceType));
        }

        return result;
    }

    private static CteRef Intersect(IReadOnlyList<Expression> expressions, StructuralContext context, string? resourceType)
    {
        var refs = expressions.Select(e => LowerNode(e, context, resourceType)).ToList();
        var result = refs[0];
        for (var i = 1; i < refs.Count; i++)
        {
            result = context.Intersect(result, refs[i]);
        }

        return result;
    }

    /// <summary>
    /// Returns the expression whose match set a negated child subtracts -- its positive inner match -- or
    /// null when the child is not a negation. The three shapes a binder produces (an explicit
    /// NotExpression, a :not-modified predicate, and :missing=true) all reduce to an expression
    /// <see cref="LowerNode"/> already knows how to lower positively.
    /// </summary>
    private static Expression? TryGetNegatedInner(Expression child) => child switch
    {
        SearchParameterExpression { Expression: NotExpression not } => not.Expression,
        SearchParameterExpression { Expression: SearchParameterPredicateExpression { Modifier.SearchModifierCode: SearchModifierCode.Not } predicate } =>
            new SearchParameterPredicateExpression(predicate.Parameter, predicate.Comparator, modifier: null, predicate.Value) { Span = predicate.Span },
        MissingSearchParameterExpression { IsMissing: true } missing =>
            new MissingSearchParameterExpression(missing.Parameter, isMissing: false),
        _ => null,
    };

    /// <summary>
    /// Splits an expression into the resource-column predicates (_id/_type/_lastUpdated, ANDed together
    /// into an outer WHERE) and the remaining expression that still needs CTE lowering. Either half may be
    /// null.
    /// </summary>
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

    /// <summary>Returns the resource-column predicate for a single wrapped leaf, or an Or of same-column
    /// equalities for a wrapped comma-separated value list (e.g. _type=Patient,Observation), or null if
    /// the expression is neither.</summary>
    private static Predicate? TryExtractResourceColumnPredicate(Expression expression, LeafContext leafContext)
    {
        if (expression is SearchParameterExpression { Expression: SearchParameterPredicateExpression predicate })
        {
            return ResourceColumnLoweringRule.TryLower(predicate, leafContext);
        }

        if (expression is SearchParameterExpression { Expression: MultiaryExpression { MultiaryOperation: MultiaryOperator.Or } or }
            && or.Expressions.Count > 0)
        {
            return TryExtractOrOfResourceColumnEquals(or, leafContext);
        }

        return null;
    }

    /// <summary>Returns an Or of <see cref="Predicate.Equal"/>s when every alternative is a bare predicate
    /// that lowers to a resource-column equality on the same column (a comma-separated resource-column
    /// value list, e.g. _type=Patient,Observation), or null when any alternative is not that shape --
    /// leaving the whole Or unextracted, to fall through to the ordinary dispatch path's own guard.</summary>
    private static Predicate? TryExtractOrOfResourceColumnEquals(MultiaryExpression or, LeafContext leafContext)
    {
        var equalities = new List<Predicate.Equal>(or.Expressions.Count);
        foreach (var alternative in or.Expressions)
        {
            if (alternative is not SearchParameterPredicateExpression predicate
                || ResourceColumnLoweringRule.TryLower(predicate, leafContext) is not Predicate.Equal equal
                || (equalities.Count > 0 && equal.Column != equalities[0].Column))
            {
                return null;
            }

            equalities.Add(equal);
        }

        return equalities.Cast<Predicate>().Aggregate((left, right) => new Predicate.Or(left, right));
    }

    /// <summary>
    /// Lowers a resource-column leaf, or a comma list of them (`_id=a,b,c` binds to an Or of predicates
    /// under one SearchParameterExpression). The Or is all-or-nothing: a branch that is not a resource
    /// column leaves the whole expression to CTE lowering, because half an Or in the outer WHERE would
    /// widen the match rather than narrow it.
    /// </summary>
    private static Predicate? TryLowerResourceColumn(Expression expression, LeafContext leafContext)
    {
        // A negated resource column (_id:not, _type:not) arrives as a NotExpression wrapping the positive
        // alternatives, each stripped of its own modifier by the binder. Lower the positive form, then wrap
        // it in Predicate.Not so the negation reaches the outer WHERE as NOT (...) rather than being
        // silently dropped -- the failure the leaf rule's modifier guard exists to prevent.
        if (expression is NotExpression not)
        {
            var inner = TryLowerResourceColumn(not.Expression, leafContext);
            return inner is null ? null : new Predicate.Not(inner);
        }

        if (expression is SearchParameterPredicateExpression predicate)
        {
            return ResourceColumnLoweringRule.TryLower(predicate, leafContext);
        }

        if (expression is not MultiaryExpression { MultiaryOperation: MultiaryOperator.Or } or)
        {
            return null;
        }

        Predicate? combined = null;
        foreach (var branch in or.Expressions)
        {
            var lowered = TryLowerResourceColumn(branch, leafContext);
            if (lowered is null)
            {
                return null;
            }

            combined = combined is null ? lowered : new Predicate.Or(combined, lowered);
        }

        return combined;
    }

    /// <summary>
    /// Lowers a chain's target expression within its own scope, folding any resource-column predicates into
    /// the scope's ResourceSource (a nested scope has no outer WHERE to attach them to) and intersecting
    /// with the ordinary match when both are present.
    /// </summary>
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

    /// <summary>An include with its direction and its resolved seed (Requires) and output (Produces) type ids; null means wildcard.</summary>
    private readonly record struct ResolvedInclude(
        IncludeExpression Expression,
        IncludeDirection Direction,
        IReadOnlyList<short>? Requires,
        IReadOnlyList<short>? Produces);

    /// <summary>
    /// Builds the ordered include stages for a plan. Non-iterate includes run first, iterate includes are
    /// topologically sorted after them, and each stage records which earlier stages (and whether the match
    /// page) seed it. Returns null when there are no includes or every stage is degenerate.
    /// </summary>
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

    /// <summary>
    /// Builds a <see cref="SortSpec"/> from the sort keys and phase, or null when there are none. Caps at 3
    /// keys and rejects a MissingPrimary phase on _lastUpdated (which is never missing).
    /// </summary>
    private static SortSpec? BuildSortSpec(IReadOnlyList<SortExpression> sort, SortPhase phase, SymbolTable symbols)
    {
        if (sort.Count == 0)
        {
            return null;
        }

        if (sort.Count > 3)
        {
            throw new NotSupportedException(
                $"_sort supports at most 3 keys this phase (got {sort.Count}) -- a cap on per-request join cost " +
                "and plan-shape risk, not an architectural limit. Rewrite the search to use 3 or fewer sort keys.");
        }

        var keys = sort.Select(s => BuildSortKey(s, symbols)).ToList();

        if (phase == SortPhase.MissingPrimary && keys[0].Kind == SortKeyKind.LastUpdated)
        {
            throw new NotSupportedException(
                "_lastUpdated is a resource-column sort key derived directly from ResourceSurrogateId -- " +
                "it is never \"missing,\" so there is no MissingPrimary segment for it. Only a search-" +
                "parameter-table primary key (String or Date) has a MissingPrimary phase.");
        }

        return new SortSpec(keys, phase);
    }

    /// <summary>Builds one <see cref="SortKey"/>, mapping the parameter to a String/Date/LastUpdated/Aggregated kind and resolving its id (none for _lastUpdated).</summary>
    internal static SortKey BuildSortKey(SortExpression sortExpression, SymbolTable symbols)
    {
        if (sortExpression.Parameter.Code == "_lastUpdated")
        {
            return new SortKey(null, SortKeyKind.LastUpdated, sortExpression.SortOrder);
        }

        var searchParamId = symbols.SearchParamId(sortExpression.Parameter);

        if (sortExpression.Parameter.Type == SearchParamType.String)
        {
            return new SortKey(searchParamId, SortKeyKind.String, sortExpression.SortOrder);
        }

        if (sortExpression.Parameter.Type == SearchParamType.Date)
        {
            return new SortKey(searchParamId, SortKeyKind.Date, sortExpression.SortOrder);
        }

        var (tableName, columnName) = sortExpression.Parameter.Type switch
        {
            SearchParamType.Token => ("TokenSearchParam", "Code"),
            SearchParamType.Number => ("NumberSearchParam", "LowValue"),
            SearchParamType.Quantity => ("QuantitySearchParam", "LowValue"),
            SearchParamType.Reference => ("ReferenceSearchParam", "ReferenceResourceId"),
            SearchParamType.Uri => ("UriSearchParam", "Uri"),
            _ => throw new NotSupportedException(
                $"Sorting by a '{sortExpression.Parameter.Type}' search parameter ('{sortExpression.Parameter.Code}') " +
                "is not supported -- String, Date, _lastUpdated, Token, Number, Quantity, Reference, and Uri " +
                "sort keys are handled; Composite has no single scalar column to sort by."),
        };

        var table = SqlCatalog.Default.Table(tableName);
        var column = table.Column(columnName);
        return new SortKey(searchParamId, SortKeyKind.Aggregated, sortExpression.SortOrder, table, column);
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
