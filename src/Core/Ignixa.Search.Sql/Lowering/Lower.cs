using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;
using Ignixa.Search.Sql.Compilation;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>The compiler's Lower stage: turns a bound Expression tree into a <see cref="QueryPlan"/>, a pure
/// value (all I/O already happened in Resolve). Handles predicate leaves, composites, chains at any depth,
/// compartment searches, and _include/_revinclude/_sort/paging.</summary>
internal static class Lower
{
    /// <summary>Lowers a whole search into a QueryPlan: extracts resource-column predicates into an outer WHERE,
    /// lowers the remaining expression (or a bare resource source) into the CTE graph, then attaches includes,
    /// sort, and paging. A null target type is allowed only for a wildcard compartment or system-level search;
    /// chain, :not/:missing=true, _not-referenced, :text, _include/_revinclude and _sort still require one and throw.</summary>
    internal static LoweredPlan Run(CompilationContext context, SymbolTable symbols)
    {
        ArgumentNullException.ThrowIfNull(context);

        var options = context.Options;
        var expression = context.Expression;
        var targetResourceType = context.TargetResourceType;
        var includes = context.Includes;
        var revIncludes = context.RevIncludes;
        var includeLimit = options.IncludeLimit;
        var sort = context.Sort;

        // Paging is one closed choice, so the T-SQL restriction that TOP cannot combine with OFFSET/FETCH
        // (error 10741) needs no guard here: only one of these two locals can be non-null.
        var keyset = options.Paging as SearchPaging.Keyset;
        var top = keyset?.Top;
        var page = keyset?.Boundary;
        var sortPhase = options.SortPhase;
        var shape = options.Shape;
        var includesOnly = shape is ResultShape.IncludesPage;

        OffsetSpec? offsetPage = null;

        if (options.Paging is SearchPaging.Offset offset)
        {
            // A positional record parameter cannot enforce non-nullness against a caller who ignores the
            // annotation, and a null Spec would fall through to an unpaged statement returning every row.
            offsetPage = offset.Spec
                ?? throw new NotSupportedException(
                    "SearchPaging.Offset requires an OffsetSpec. Use SearchPaging.Keyset, or leave Paging null, " +
                    "to compile without an OFFSET/FETCH page.");

            if (offsetPage.Offset < 0 || offsetPage.Limit <= 0)
            {
                throw new NotSupportedException(
                    $"OffsetSpec must skip a non-negative row count and fetch a positive one; got Offset " +
                    $"{offsetPage.Offset} and Limit {offsetPage.Limit}. OFFSET/FETCH rejects both at runtime.");
            }
        }

        if (top is < 0)
        {
            throw new NotSupportedException(
                $"Top must not be negative; got {top}. A negative TOP is not a smaller page, it is a SQL Server " +
                "runtime error, so it is reported at compile time instead.");
        }

        if (includeLimit is < 0 or int.MaxValue)
        {
            throw new NotSupportedException(
                $"IncludeLimit must be between 0 and {int.MaxValue - 1}; got {includeLimit}. The budget is " +
                "emitted as TOP (IncludeLimit + 1) — a negative value is a SQL Server runtime error, and " +
                "int.MaxValue overflows the probe to a negative TOP. Use 0 to detect included resources " +
                "without fetching them.");
        }

        if (!Enum.IsDefined(sortPhase))
        {
            throw new NotSupportedException(
                $"SortPhase '{(int)sortPhase}' is not a phase this compiler recognises. Any value other than " +
                $"{nameof(SortPhase)}.{nameof(SortPhase.MissingPrimary)} would read the Valued segment, " +
                "handing back rows a caller driving the two-phase loop has already seen.");
        }

        // The count guard runs first: for a phase-restricted count with no _sort, both guards apply, and the
        // generic one would advise switching to SortPhase.Valued, which leaves the count still unsatisfiable.
        if (shape is ResultShape.Count.CurrentSortPhase && sort.Count == 0)
        {
            throw new NotSupportedException(
                "A count was asked to restrict itself to the sort phase but the query has no _sort, so there is " +
                "no segment to restrict it to. Use ResultShape.Count.AllMatches to count the whole match set.");
        }

        // The MissingPrimary segment is defined by the absence of the primary sort key, so with no _sort there
        // is no such segment. Emitting the phase-free statement instead hands back an ordinary first page, and a
        // caller driving the two-phase loop would re-read every row it already saw.
        if (sortPhase is SortPhase.MissingPrimary && sort.Count == 0)
        {
            throw new NotSupportedException(
                "SortPhase.MissingPrimary was requested but the query has no _sort, so there is no primary sort " +
                "key to be missing and no second segment to read. Use SortPhase.Valued for an unsorted query.");
        }

        var accessConstraintApplier = new AccessConstraintApplier(context.AccessConstraints);
        var allowedResourceTypeFilter = new AllowedResourceTypeFilter(context.AllowedResourceTypes, symbols);
        var lowerContext = new StructuralContext(symbols, context.ApproximationReferenceTime, accessConstraintApplier);
        CteRef match;
        Predicate? outerPredicate = null;

        // The node the match set was actually lowered from, not `expression`: resource-column extraction below
        // peels _id/_type/_lastUpdated off an And and leaves the residue. The access-constraint dispatch must
        // read this one -- reading `expression` would misclassify a multi-type union as single-type.
        Expression? matchSource = expression;

        if (expression is null)
        {
            match = LowerBaseSet(lowerContext, targetResourceType, context.ResourceTypes);
        }
        else
        {
            var (remaining, extractedPredicate) = ExtractResourceColumnPredicates(expression, lowerContext.LeafContext);
            outerPredicate = extractedPredicate;
            matchSource = remaining;
            match = remaining switch
            {
                null => LowerBaseSet(lowerContext, targetResourceType, context.ResourceTypes),
                CompartmentSearchExpression compartment => lowerContext.LowerCompartment(compartment),
                _ when targetResourceType is null && !context.SystemLevelSearch => throw new NotSupportedException(
                    "A search with no single target resource type (a wildcard compartment search) can only " +
                    "combine with a CompartmentSearchExpression and resource-column predicates -- an ordinary " +
                    "typed search parameter alongside it has no single resource type to scope it against, " +
                    "which this phase does not support."),
                // targetResourceType may be null here, but only under SystemLevelSearch: the leaves lower
                // with no type scope and the requested types narrow the result set instead.
                _ => NarrowToRequestedTypes(
                    LowerNode(remaining, lowerContext, targetResourceType),
                    lowerContext,
                    targetResourceType,
                    context.ResourceTypes),
            };
        }

        // Every stage that produces rows is constrained, starting with the match set. A single-type match
        // is intersected directly; a multi-type match narrows only the constrained types (see ApplyToTypes).
        // Later stages -- includes, :iterate, and chain targets -- are constrained at their own sites so a
        // caller cannot reach a hidden resource by navigating a reference rather than searching for it.
        if (!accessConstraintApplier.IsEmpty)
        {
            // $everything's match set spans several types (patient row unioned with compartment members) even
            // though its target type is "Patient". A single-type Apply would intersect the union down to
            // Patient-admitted rows -- dropping members and never constraining member types (an auth bypass).
            // ApplyToTypes narrows each constrained type in place, as for a multi-_type or wildcard match.
            match = (targetResourceType, matchSource) switch
            {
                (_, PatientEverythingExpression) => accessConstraintApplier.ApplyToTypes(match, lowerContext, LowerScopedExpression),
                ({ } matchType, _) => accessConstraintApplier.Apply(match, matchType, lowerContext, LowerScopedExpression),
                _ => accessConstraintApplier.ApplyToTypes(match, lowerContext, LowerScopedExpression),
            };
        }

        // The allow-list is the other authorization control: everything not permitted is removed (a plain
        // intersect with the allowed types' base set), so dropping it here fails open and widens the query.
        // Applied after the access constraints; order is irrelevant since both only remove rows.
        if (!allowedResourceTypeFilter.IsEmpty)
        {
            match = allowedResourceTypeFilter.RestrictMatch(match, lowerContext);
        }

        // System-level search is deliberately excluded: the sort joins correlate on the match set's own
        // m.T1 rather than on a literal ResourceTypeId, so they never needed a single target type. A
        // wildcard compartment search is a different null-type case and keeps the original refusal.
        if (targetResourceType is null && !context.SystemLevelSearch && sort.Count > 0)
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
                throw new NotSupportedException(
                    $"_include/_revinclude combined with {NoTargetTypeReason(context.SystemLevelSearch)} (no single " +
                    "target resource type) is not supported -- BuildIncludeStages needs a concrete match resource " +
                    "type to compute SeedFromMatch.");
            }

            includeStages = null;
        }
        else
        {
            includeStages = BuildIncludeStages(includes, revIncludes, symbols, targetResourceType, includeLimit);
        }

        // Bind constraints to each include/:iterate stage. This lowers the constraint predicates into
        // lowerContext.Ctes (emitted before any include CTE, so no forward reference) and records per-stage
        // bindings the emitter turns into type-guarded EXISTS filters. A wildcard stage whose output types
        // are unknown is constrained conservatively; see AccessConstraintApplier.BindIncludeStage.
        if (includeStages is { Count: > 0 } && !accessConstraintApplier.IsEmpty)
        {
            includeStages = includeStages
                .Select(stage =>
                {
                    var bindings = accessConstraintApplier.BindIncludeStage(stage.OutputTypeIds, symbols, lowerContext, LowerScopedExpression);
                    return bindings is null ? stage : stage with { Constraints = bindings };
                })
                .ToList();
        }

        // Enforce the allow-list on each include/:iterate stage: RestrictStage intersects OutputTypeIds with the
        // allowed ids (a wildcard's null output types -- the fail-open case -- become the full allowed set), and
        // substitutes the unmatchable sentinel when empty so the stage renders "= -1" rather than no filter. Run
        // after the access-constraint binding. Chain targets are deliberately not filtered, matching legacy parity.
        if (includeStages is { Count: > 0 } && !allowedResourceTypeFilter.IsEmpty)
        {
            includeStages = includeStages
                .Select(allowedResourceTypeFilter.RestrictStage)
                .ToList();
        }

        if (includesOnly && includeStages is not { Count: > 0 })
        {
            throw new NotSupportedException(
                "IncludesPage was requested with no _include or _revinclude stages, which can only ever " +
                "return an empty result. This is a caller error rather than a query that legitimately " +
                "matches nothing, so it is reported rather than silently emitted.");
        }

        // A _sort is still allowed here: its ordering role drops (include rows page by (T1, Sid1), not the sort
        // key), but SortPhase (MissingPrimary/Valued) is a *filter* that partitions the match set seeding the
        // include stages, so it rides into the match-page CTE independently of ORDER BY. A keyset boundary is the
        // genuinely unsound combination and is refused below.
        if (includesOnly && page is not null)
        {
            throw new NotSupportedException(
                "IncludesPage was requested together with a keyset continuation boundary. An includes-only page " +
                "bounds its match set by a surrogate-id range and pages its include rows by a resume boundary over " +
                "(T1, Sid1); a keyset boundary instead seeks the match rows by the sort-key boundary, a second " +
                "paging mechanism the includes-only page does not use. The combination is reported rather than " +
                "silently applying a match-side seek that would change which resources are included.");
        }

        // Top truncates the seed match set and an offset page skips into it, so either silently changes which
        // resources the include stages reach. Same reasoning as the keyset boundary above: the includes page
        // bounds its match set by a surrogate-id range and pages its own rows by the resume boundary.
        if (includesOnly && (top is not null || offsetPage is not null))
        {
            throw new NotSupportedException(
                "IncludesPage was requested together with " + (top is not null ? "a Top cap" : "an offset page") +
                ". An includes-only page bounds its match set by a surrogate-id range and pages its include rows " +
                "by a resume boundary; a match-side row cap or offset instead changes which resources seed the " +
                "include stages, dropping include rows with no indication that they are missing. Bound the match " +
                "set with SurrogateRange and page the include rows with ResultShape.IncludesPage.Resume.");
        }

        // BuildSortSpec validates the sort -- key count, key types, and MissingPrimary against a resource-column
        // primary key -- so it runs for every shape. A count reads it only when it asked to be restricted to the
        // phase; otherwise SqlBuilder ignores it and counts the whole match set.
        var sortSpec = BuildSortSpec(sort, sortPhase, symbols);

        // A typeless boundary breaks its final tie on Sid1 alone and omits the type column, which agrees with the
        // ORDER BY only for a custom sort (every other sort keeps m.T1 as a tiebreak). Mirror of the guard below,
        // also enforced by SqlBuilder.RejectUnsupportedCombinations for direct QueryPlan callers.
        if (page is { BoundaryResourceTypeId: null } && !HasCustomSortKey(sortSpec))
        {
            throw new NotSupportedException(
                "A typeless keyset Page (BoundaryResourceTypeId is null) requires a custom (search-parameter) " +
                "_sort such as name or birthdate. The sort here is " +
                (sortSpec is null ? "absent" : "a resource-column sort (_lastUpdated / _type / _id)") +
                ", whose keyset order includes the resource type, so a type-free seek would disagree with the " +
                "ORDER BY and paging would be unsound. Use a typed Page here, or a custom sort for a typeless Page.");
        }

        // A custom sort orders by (sort keys…, Sid1) with no type component, so a typed boundary would seek
        // type-major and drop rows within a run of tied sort values at the page seam. Mirrored by
        // SqlBuilder.RejectUnsupportedCombinations for direct QueryPlan callers.
        if (page is { BoundaryResourceTypeId: not null } && HasCustomSortKey(sortSpec))
        {
            throw new NotSupportedException(
                "A typed keyset Page (BoundaryResourceTypeId is non-null) cannot be combined with a custom " +
                "(search-parameter) _sort such as name or birthdate: the emitted ORDER BY is (sort keys…, Sid1) " +
                "while a typed boundary seeks type-major, so rows are silently dropped at the page seam. Decode " +
                "the continuation token to a typeless Page (BoundaryResourceTypeId: null) for a custom sort; the " +
                "type component is redundant because ResourceSurrogateId is globally unique.");
        }

        // A boundary decoded under one phase carries values for that phase's active keys, so carrying it across
        // a Valued/MissingPrimary transition seeks on the wrong key set. Mirrored by
        // SqlBuilder.RejectUnsupportedCombinations for direct QueryPlan callers.
        if (page is not null && page.Boundary.Count != (sortSpec?.ActiveKeyCount ?? 0))
        {
            throw new NotSupportedException(
                $"The keyset boundary carries {page.Boundary.Count} value(s) but {nameof(SortPhase)}." +
                $"{sortPhase} has {sortSpec?.ActiveKeyCount ?? 0} active sort key(s). Decode the continuation " +
                "token for the phase you are reading; a boundary never survives a Valued/MissingPrimary " +
                "transition.");
        }

        return new LoweredPlan(
            new QueryPlan(lowerContext.Ctes, match, top, outerPredicate, includeStages, sortSpec, page, shape, context.Visibility, SurrogateRange: context.SurrogateRange, SearchParameterHash: context.Options.SearchParameterHash is { } hash ? new SqlParameterRef(hash) : null, OffsetPage: offsetPage),
            new PlanProvenance(lowerContext.Origins));
    }

    /// <summary>True when the sort has any search-parameter-backed key (String/Date or an Aggregated leaf) rather
    /// than only resource-column keys (_lastUpdated/_type/_id). Duplicated from SqlBuilder deliberately: each
    /// layer guards its own construction surface, since a QueryPlan can be built without going through Lower.</summary>
    private static bool HasCustomSortKey(SortSpec? sort)
        => sort is not null
           && sort.Keys.Any(k => k.Kind is SortKeyKind.String or SortKeyKind.Date or SortKeyKind.Aggregated);

    /// <summary>Names why there is no single target resource type, so a guard's message diagnoses the caller's
    /// actual situation rather than always blaming a wildcard compartment search.</summary>
    private static string NoTargetTypeReason(bool systemLevelSearch)
        => systemLevelSearch ? "a system-level search" : "a wildcard compartment search";

    /// <summary>
    /// The base match set when no expression narrows it: a single-type ResourceSource when a target type
    /// is named, otherwise a MultiTypeResourceSource over the requested types — empty meaning every type.
    /// </summary>
    private static CteRef LowerBaseSet(
        StructuralContext context,
        string? targetResourceType,
        IReadOnlyList<string>? resourceTypes)
        => targetResourceType is { } single
            ? context.LowerResourceSource(single)
            : context.LowerMultiTypeResourceSource(resourceTypes ?? []);

    /// <summary>Intersects a system-level match with the requested types' base set. A cross-type leaf carries no
    /// ResourceTypeId, so without this <c>GET /?_type=A,B&amp;name=foo</c> would silently return every type with
    /// a matching name. A named target type needs no narrowing; an empty list is the "every type" contract.</summary>
    private static CteRef NarrowToRequestedTypes(
        CteRef match,
        StructuralContext context,
        string? targetResourceType,
        IReadOnlyList<string>? resourceTypes)
    {
        if (targetResourceType is not null || resourceTypes is not { Count: > 0 })
        {
            return match;
        }

        var baseSet = context.LowerMultiTypeResourceSource(resourceTypes);
        return context.Intersect(match, baseSet);
    }

    /// <summary>Dispatches one expression node to the lowering path for its kind. A null <paramref name="resourceType"/>
    /// reaches here only under system-level search; chain carries its own types, so it needs an explicit guard or
    /// a type-less chain would slip past every null-type guard in <see cref="Run"/>. UnionExpression and OR both
    /// lower to a distinct set union (UNION ALL would double-count a row admitted by two legs).</summary>
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
        UnionExpression union => context.Union(
            union.Expressions.Select(leg => LowerScopedExpression(leg, context, resourceType)).ToList()),
        ChainedExpression when resourceType is null => throw new NotSupportedException(
            "Chain is not supported in system-level search in this phase -- a chain resolves and joins against " +
            "a concrete referencing/target type, which a cross-type search has no single value for. Guarding at " +
            "the chain dispatch choke point covers a top-level chain and one nested in an AND equally; a chain " +
            "reached inside another chain's scope always has a concrete type and never trips this."),
        ChainedExpression chain => context.LowerChain(chain, LowerScopedExpression),
        CompartmentSearchExpression compartment => context.LowerCompartment(compartment),
        NotReferencedExpression notReferenced => context.LowerNotReferenced(notReferenced, resourceType),
        PatientEverythingExpression when resourceType is null => throw new NotSupportedException(
            "$everything is not supported in system-level search -- it is anchored on the Patient/Group type " +
            "whose compartment it expands, so it has no meaning without one. Guarding at the dispatch choke " +
            "point rather than letting the traversal run under a scope it cannot use."),
        PatientEverythingExpression everything => context.LowerPatientEverything(everything),
        _ => throw new NotSupportedException(
            $"Lower does not support {expression.GetType().Name} yet -- see this plan's scope notes."),
    };

    /// <summary>Lowers a wrapped search parameter, unwrapping the wrapper's own semantics first: a NotExpression or
    /// a :not-modified predicate becomes a negation, a single composite or an OR of composite alternatives
    /// becomes composite lowering, and anything else falls through to <see cref="LowerNode"/>.</summary>
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
        var presence = context.LowerParameterPresence(missing.Parameter, resourceType, provenanceNode: missing);
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

    /// <summary>Lowers an AND by intersecting its positive children, then subtracting each negated child (<c>A AND NOT B</c>
    /// is <c>A EXCEPT B</c>). Positive siblings form a smaller anchor than a bare negation would need; with no
    /// positive sibling the ResourceSource anchor is the only option (see <see cref="StructuralContext.LowerNot"/>).</summary>
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
            : context.LowerNegationAnchor(resourceType);

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

    /// <summary>Returns the positive inner match a negated child subtracts, or null when the child is not a negation.
    /// The three negation shapes (NotExpression, :not-modified predicate, :missing=true) all reduce to an
    /// expression <see cref="LowerNode"/> already lowers positively.</summary>
    private static Expression? TryGetNegatedInner(Expression child) => child switch
    {
        SearchParameterExpression { Expression: NotExpression not } => not.Expression,
        SearchParameterExpression { Expression: SearchParameterPredicateExpression { Modifier.SearchModifierCode: SearchModifierCode.Not } predicate } =>
            new SearchParameterPredicateExpression(predicate.Parameter, predicate.Comparator, modifier: null, predicate.Value) { Span = predicate.Span },
        MissingSearchParameterExpression { IsMissing: true } missing =>
            new MissingSearchParameterExpression(missing.Parameter, isMissing: false),
        _ => null,
    };

    /// <summary>Splits an expression into the resource-column predicates (_id/_type/_lastUpdated, ANDed together
    /// into an outer WHERE) and the remaining expression that still needs CTE lowering. Either half may be null.</summary>
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

    /// <summary>Returns the resource-column predicate for a single wrapped leaf, or null if it is not one.</summary>
    private static Predicate? TryExtractResourceColumnPredicate(Expression expression, LeafContext leafContext)
        => expression is SearchParameterExpression wrapped
            ? TryLowerResourceColumn(wrapped.Expression, leafContext)
            : null;

    /// <summary>Lowers a resource-column leaf, or a comma list of them (<c>_id=a,b,c</c> binds to an Or). The Or is
    /// all-or-nothing: a non-resource-column branch leaves the whole expression to CTE lowering, because half
    /// an Or in the outer WHERE would widen the match rather than narrow it.</summary>
    private static Predicate? TryLowerResourceColumn(Expression expression, LeafContext leafContext)
    {
        // A negated resource column (_id:not, _type:not) arrives as a NotExpression wrapping the positive
        // alternatives. Lower the positive form, then wrap it in Predicate.Not so the negation reaches the
        // outer WHERE as NOT (...) rather than being silently dropped.
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

    /// <summary>Lowers a chain's target expression or a union leg within its own scope, folding any resource-column
    /// predicates into the scope's ResourceSource and intersecting with the ordinary match. <paramref name="resourceType"/>
    /// is null only for a union leg under a system-level search; that case routes to
    /// <see cref="LowerSystemLevelUnionLeg"/> so the typed path every other caller uses stays unchanged.</summary>
    private static CteRef LowerScopedExpression(Expression expression, StructuralContext context, string? resourceType)
    {
        var (remaining, nestedPredicate) = ExtractResourceColumnPredicates(expression, context.LeafContext);

        if (resourceType is null)
        {
            return LowerSystemLevelUnionLeg(expression, remaining, nestedPredicate, context);
        }

        if (remaining is null)
        {
            return context.LowerResourceSourceWithPredicate(resourceType, nestedPredicate);
        }

        var ordinaryMatch = LowerNode(remaining, context, resourceType);
        return nestedPredicate is null
            ? ordinaryMatch
            : context.Intersect(context.LowerResourceSourceWithPredicate(resourceType, nestedPredicate), ordinaryMatch);
    }

    /// <summary>Lowers one union leg under a system-level (null) scope -- the SMART compartment expansion. A pure
    /// resource-column leg folds into an AllTypes source; a leg with a residue derives its type from its own
    /// single <c>_type Eq X</c> (see <see cref="TryDeriveSingleTypeScope"/>) then lowers as a typed leg; a leg
    /// with no derivable type lowers under null scope and lets the per-node guards decide.</summary>
    private static CteRef LowerSystemLevelUnionLeg(
        Expression leg,
        Expression? remaining,
        Predicate? nestedPredicate,
        StructuralContext context)
    {
        if (remaining is null)
        {
            return context.LowerMultiTypeResourceSourceWithPredicate(nestedPredicate);
        }

        if (TryDeriveSingleTypeScope(leg) is { } derivedType)
        {
            // With a concrete type recovered, the leg is indistinguishable from a natively typed one: scope the
            // residue to that type and intersect with the resource-column predicate, emitting the same single-type
            // ResourceSource a typed leg would. The predicate's redundant ResourceTypeId equality is harmless.
            var scopedMatch = LowerNode(remaining, context, derivedType);
            return nestedPredicate is null
                ? scopedMatch
                : context.Intersect(context.LowerResourceSourceWithPredicate(derivedType, nestedPredicate), scopedMatch);
        }

        var match = LowerNode(remaining, context, resourceType: null);
        return nestedPredicate is null
            ? match
            : context.Intersect(context.LowerMultiTypeResourceSourceWithPredicate(nestedPredicate), match);
    }

    /// <summary>Returns the type name a union leg scopes itself to via a <em>single</em> plain <c>_type Eq X</c> among its
    /// ANDed children, or null otherwise. Confined to one equality: a <c>_type=A,B</c> Or, two distinct
    /// equalities, or a modified/system-qualified <c>_type</c> all yield null rather than a guess that could drop
    /// rows -- a null result lowers the residue under a null type, where its own per-node guard decides.</summary>
    private static string? TryDeriveSingleTypeScope(Expression leg)
    {
        var children = leg is MultiaryExpression { MultiaryOperation: MultiaryOperator.And } and
            ? and.Expressions
            : [leg];

        string? found = null;
        foreach (var child in children)
        {
            if (child is not SearchParameterExpression { Expression: SearchParameterPredicateExpression predicate }
                || predicate.Parameter.Code != "_type"
                || predicate.Modifier is not null
                || predicate.Comparator != SearchComparator.Eq
                || predicate.Value is not TokenSearchValue { System: null, Code: { Length: > 0 } code })
            {
                continue;
            }

            if (found is not null)
            {
                // A second single-valued _type equality makes the scope ambiguous. Refuse to guess: null lowers
                // the residue under a null type rather than scoping to whichever equality came first.
                return null;
            }

            found = code;
        }

        return found;
    }

    /// <summary>An include with its direction and its resolved seed (Requires) and output (Produces) type ids; null means wildcard.</summary>
    private readonly record struct ResolvedInclude(
        IncludeExpression Expression,
        IncludeDirection Direction,
        IReadOnlyList<short>? Requires,
        IReadOnlyList<short>? Produces);

    /// <summary>Builds the ordered include stages for a plan. Non-iterate includes run first, iterate includes are
    /// topologically sorted after them, and each stage records which earlier stages (and whether the match
    /// page) seed it. Returns null when there are no includes or every stage is degenerate.</summary>
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

        if (phase == SortPhase.MissingPrimary && keys[0].Kind is SortKeyKind.LastUpdated or SortKeyKind.ResourceType or SortKeyKind.ResourceId)
        {
            throw new NotSupportedException(
                "_lastUpdated, _type and _id are resource-column sort keys derived directly from " +
                "ResourceSurrogateId, ResourceTypeId and ResourceId -- all are non-nullable resource columns, " +
                "so a value is never missing for any of them, and none has a MissingPrimary segment. Only a " +
                "search-parameter-table primary key (String, Date, or an aggregated leaf type) has a " +
                "MissingPrimary phase.");
        }

        return new SortSpec(keys, phase);
    }

    /// <summary>Builds one <see cref="SortKey"/>, mapping the parameter to a String/Date/LastUpdated/ResourceType/ResourceId/Aggregated kind and resolving its id (none for _lastUpdated, _type or _id).</summary>
    internal static SortKey BuildSortKey(SortExpression sortExpression, SymbolTable symbols)
    {
        if (sortExpression.Parameter.Code == "_lastUpdated")
        {
            return new SortKey(null, SortKeyKind.LastUpdated, sortExpression.SortOrder);
        }

        if (sortExpression.Parameter.Code == "_id")
        {
            return new SortKey(null, SortKeyKind.ResourceId, sortExpression.SortOrder);
        }

        // _type orders by the resource's type id (T1) -- the storage layer's own type ordering, not an ordering
        // over type names, which is what makes "_sort=_type,_lastUpdated" the natural (T1, Sid1) clustered order.
        // _lastUpdated/_id/_type are exactly the codes ResourceColumnLoweringRule.IsResourceColumnCode recognises,
        // so any code reaching the SearchParamId lookup below is a real search parameter Resolve collected.
        if (sortExpression.Parameter.Code == "_type")
        {
            return new SortKey(null, SortKeyKind.ResourceType, sortExpression.SortOrder);
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
