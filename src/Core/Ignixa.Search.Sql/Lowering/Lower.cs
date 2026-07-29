using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
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
    /// wildcard compartment search, or -- when <see cref="LowerOptions.SystemLevelSearch"/> is set -- for a
    /// system-level (cross-type) search of ordinary leaf/composite/AND/OR predicates. Even under system-level
    /// search, chain, :not/:missing=true, _not-referenced, :text, _include/_revinclude, and _sort still
    /// require a single target type and throw. The optional inputs -- paging caps, visibility, surrogate
    /// range, hash gating, the base-set types, the access constraints, and the resource-type allow-list --
    /// are grouped on <see cref="LowerOptions"/> so each is passed by name.
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
        LowerOptions? options = null)
    {
        options ??= new LowerOptions();

        if (options.OffsetPage is not null && (page is not null || options.Top is not null))
        {
            throw new NotSupportedException(
                "OffsetPage cannot combine with keyset paging or Top: OFFSET/FETCH and TOP are mutually exclusive in T-SQL (error 10741).");
        }

        if (options.CountPhaseScoped && (!options.CountOnly || sort.Count == 0))
        {
            throw new NotSupportedException(
                "CountPhaseScoped requires CountOnly with at least one sort key: there is no sort phase to scope the count to otherwise.");
        }

        var accessConstraintApplier = new AccessConstraintApplier(options.AccessConstraints);
        var allowedResourceTypeFilter = new AllowedResourceTypeFilter(options.AllowedResourceTypes, symbols);
        var context = new StructuralContext(symbols, options.ApproximationReferenceTime, accessConstraintApplier);
        CteRef match;
        Predicate? outerPredicate = null;

        // The node the match set was actually lowered from. Not always `expression`: the resource-column
        // extraction below peels _id/_type/_lastUpdated off an And and leaves the residue, so the two
        // diverge for a query like `$everything AND _lastUpdated ge X`. The access-constraint dispatch
        // downstream has to read this one -- picking its enforcement method from `expression` would see a
        // MultiaryExpression where the match set is a multi-type union, and choose the single-type Apply.
        Expression? matchSource = expression;

        if (expression is null)
        {
            match = LowerBaseSet(context, targetResourceType, options.ResourceTypes);
        }
        else
        {
            var (remaining, extractedPredicate) = ExtractResourceColumnPredicates(expression, context.LeafContext);
            outerPredicate = extractedPredicate;
            matchSource = remaining;
            match = remaining switch
            {
                null => LowerBaseSet(context, targetResourceType, options.ResourceTypes),
                CompartmentSearchExpression compartment => context.LowerCompartment(compartment),
                _ when targetResourceType is null && !options.SystemLevelSearch => throw new NotSupportedException(
                    "A search with no single target resource type (a wildcard compartment search) can only " +
                    "combine with a CompartmentSearchExpression and resource-column predicates -- an ordinary " +
                    "typed search parameter alongside it has no single resource type to scope it against, " +
                    "which this phase does not support."),
                // targetResourceType may be null here, but only under SystemLevelSearch: the leaves lower
                // with no type scope and the requested types narrow the result set instead.
                _ => NarrowToRequestedTypes(
                    LowerNode(remaining, context, targetResourceType),
                    context,
                    targetResourceType,
                    options.ResourceTypes),
            };
        }

        // Every stage that produces rows is constrained, starting with the match set. A single-type match
        // is intersected directly; a multi-type match narrows only the constrained types (see ApplyToTypes).
        // Later stages -- includes, :iterate, and chain targets -- are constrained at their own sites so a
        // caller cannot reach a hidden resource by navigating a reference rather than searching for it.
        if (!accessConstraintApplier.IsEmpty)
        {
            // $everything's match set spans several types (the patient row unioned with its compartment
            // members) even though its target type is "Patient". A single-type Apply would intersect the
            // whole union down to Patient-admitted rows -- dropping every compartment member and, worse,
            // never enforcing a constraint on a member type (an authorization bypass). ApplyToTypes narrows
            // each constrained type in place, exactly as it does for a multi-_type or wildcard match.
            match = (targetResourceType, matchSource) switch
            {
                (_, PatientEverythingExpression) => accessConstraintApplier.ApplyToTypes(match, context, LowerScopedExpression),
                ({ } matchType, _) => accessConstraintApplier.Apply(match, matchType, context, LowerScopedExpression),
                _ => accessConstraintApplier.ApplyToTypes(match, context, LowerScopedExpression),
            };
        }

        // The allow-list is the other authorization control, enforced on the same row-producing stages but
        // with allow-list rather than per-type-narrowing semantics: everything not permitted is removed, so
        // a plain intersect with the allowed types' base set is correct for every match shape. A single-type
        // match on an unpermitted type intersects to no rows; a multi-type / system-level / $everything match
        // keeps only its allowed rows. Applied after the access constraints so both restrictions compose;
        // order does not matter to the result set (both only remove rows), but running it last keeps the
        // match's authorization narrowing in one contiguous block.
        if (!allowedResourceTypeFilter.IsEmpty)
        {
            match = allowedResourceTypeFilter.RestrictMatch(match, context);
        }

        // System-level search is deliberately excluded: the sort joins correlate on the match set's own
        // m.T1 rather than on a literal ResourceTypeId, so they never needed a single target type. A
        // wildcard compartment search is a different null-type case and keeps the original refusal.
        if (targetResourceType is null && !options.SystemLevelSearch && sort.Count > 0)
        {
            throw new NotSupportedException(
                "_sort combined with a wildcard compartment search (no single target resource type) is not " +
                "supported -- a SortSpec needs a single ResourceTypeId scope for its joins, the same reasoning " +
                "already established for typed leaves and _include/_revinclude under a null scope.");
        }

        // Reject the self-contradictory combination up front, before doing the work of building
        // include stages — IncludesOnly requests include rows; CountOnly requests a count of match
        // rows; the two are mutually exclusive regardless of what includes are present.
        if (options.IncludesOnly && options.CountOnly)
        {
            throw new NotSupportedException(
                "IncludesOnly and CountOnly cannot both be true: IncludesOnly requests include-stage rows " +
                "while CountOnly requests a count of match rows; the combination is self-contradictory.");
        }

        IReadOnlyList<IncludeStage>? includeStages;
        if (targetResourceType is null)
        {
            if (includes.Count > 0 || revIncludes.Count > 0)
            {
                throw new NotSupportedException(
                    $"_include/_revinclude combined with {NoTargetTypeReason(options.SystemLevelSearch)} (no single " +
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
        // context.Ctes (emitted before any include CTE, so no forward reference) and records per-stage
        // bindings the emitter turns into type-guarded EXISTS filters. A wildcard stage whose output types
        // are unknown is constrained conservatively; see AccessConstraintApplier.BindIncludeStage.
        if (includeStages is { Count: > 0 } && !accessConstraintApplier.IsEmpty)
        {
            includeStages = includeStages
                .Select(stage =>
                {
                    var bindings = accessConstraintApplier.BindIncludeStage(stage.OutputTypeIds, symbols, context, LowerScopedExpression);
                    return bindings is null ? stage : stage with { Constraints = bindings };
                })
                .ToList();
        }

        // Enforce the allow-list on each include/:iterate stage, the key structural enforcement point: the
        // emitter already renders IncludeStage.OutputTypeIds as an "outputTypeColumn IN (...)" filter, which
        // is exactly the shape the legacy SQL generator applies IncludeExpression.AllowedResourceTypesByScope
        // in. RestrictStage intersects OutputTypeIds with the allowed ids (and turns a wildcard's null output
        // types into the full allowed set -- the case most likely to fail open), substituting the unmatchable
        // sentinel when the intersection is empty so an emptied stage renders "= -1" and returns nothing
        // rather than emitting no filter and failing open. Run AFTER the access-constraint binding above so
        // that binding observes each stage's original OutputTypeIds and its wildcard-conservative behaviour is
        // unchanged; a stage the allow-list empties keeps its (now harmless) guards.
        //
        // Chain targets are DELIBERATELY not filtered here: the legacy FHIR Server carries the scope
        // allow-list only on IncludeExpression, not on chain traversal, and a chain target is a join
        // predicate rather than a returned row. Applying the allow-list to chain targets would diverge from
        // that parity and could change which primary matches a legitimate chain admits. This is an
        // intentional parity decision, not an oversight -- it mirrors AccessConstraintApplier, which does
        // constrain chain targets (they can leak rows), whereas the allow-list, being purely about which
        // types are returned, does not.
        if (includeStages is { Count: > 0 } && !allowedResourceTypeFilter.IsEmpty)
        {
            includeStages = includeStages
                .Select(allowedResourceTypeFilter.RestrictStage)
                .ToList();
        }

        if (options.IncludesOnly && includeStages is not { Count: > 0 })
        {
            throw new NotSupportedException(
                "IncludesOnly was requested with no _include or _revinclude stages, which can only ever " +
                "return an empty result. This is a caller error rather than a query that legitimately " +
                "matches nothing, so it is reported rather than silently emitted.");
        }

        // _sort has two independent roles, and an includes-only page keeps one while dropping the other.
        // The ordering role does drop: the page returns no match rows for the sort key to order, and its
        // include rows are paged by (T1, Sid1), not by the sort key -- so the sort never reaches ORDER BY.
        // But the SortPhase (MissingPrimary / Valued) is a *filter*: it partitions the match set into rows
        // that lack a value for the sort parameter and rows that have one. An includes-only page bounds its
        // match set by a surrogate-id window and seeds its include stages from exactly that set, so the
        // phase predicate decides which rows in the window are matches and therefore which include rows
        // exist. Dropping it would return the includes of rows the other phase owns. The phase predicate
        // rides into the match-page CTE independently of ORDER BY (SortSpec.Phase -> the Valued primary-key
        // INNER join / the MissingPrimary NOT EXISTS filter), so the sort is carried through, not refused.
        //
        // A keyset Page is the genuinely unsound combination and is still refused below: it seeks the match
        // rows by the sort-key boundary, which is a second paging mechanism the includes-only page does not
        // use -- its window is the surrogate range and its include cursor pages the stages.
        if (options.IncludesOnly && page is not null)
        {
            throw new NotSupportedException(
                "IncludesOnly was requested together with a keyset Page. An includes-only page bounds its " +
                "match set by a surrogate-id range and pages its include rows by a cursor over (T1, Sid1); a " +
                "keyset Page instead seeks the match rows by the sort-key boundary, a second paging mechanism " +
                "the includes-only page does not use. The combination is reported rather than silently applying " +
                "a match-side seek that would change which resources are included.");
        }

        // The resume cursor pages a stream of include rows; it only has meaning when the result IS that
        // stream. Without IncludesOnly the emitter keeps the match arm and never applies the resume
        // predicate to a match row, so a caller that passed a cursor expecting a second page would instead
        // get a full first page back — the include rows it already holds, silently re-returned. Refuse the
        // combination here, mirrored by SqlBuilder.RejectUnsupportedCombinations for direct QueryPlan callers.
        if (options.IncludeCursor is not null && !options.IncludesOnly)
        {
            throw new NotSupportedException(
                "IncludeCursor was supplied without IncludesOnly. The resume cursor pages the union of " +
                "include stages as one ordered stream, which exists only on an includes-only page; on an " +
                "ordinary search there is no such stream for it to resume, so it is reported rather than " +
                "silently ignored.");
        }

        var sortSpec = BuildSortSpec(sort, sortPhase, symbols);

        return new LoweredPlan(
            new QueryPlan(context.Ctes, match, options.Top, outerPredicate, includeStages, sortSpec, page, options.CountOnly, options.Visibility, SurrogateRange: options.SurrogateRange, SearchParameterHash: options.SearchParameterHash, IncludesOnly: options.IncludesOnly, OffsetPage: options.OffsetPage, CountPhaseScoped: options.CountPhaseScoped, IncludeCursor: options.IncludeCursor),
            new PlanProvenance(context.Origins));
    }

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

    /// <summary>
    /// Intersects a system-level match with the requested types' base set. A cross-type leaf carries no
    /// ResourceTypeId of its own, so without this the requested <c>_type</c> list would be silently
    /// dropped and <c>GET /?_type=A,B&amp;name=foo</c> would return every type that has a matching name.
    /// A named target type needs no narrowing (its leaves are already scoped), and an empty type list is
    /// the deliberate "every type" contract, which an AllTypes intersect would only make more expensive.
    /// </summary>
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

    /// <summary>Dispatches one expression node to the lowering path for its kind (leaf, missing, composite, AND, OR,
    /// union, chain, or compartment).
    /// A null <paramref name="resourceType"/> reaches here only under system-level search. Chain carries its own
    /// resource types rather than consuming the ambient one, so it needs an explicit guard here: without it a
    /// type-less chain would fall through every null-type guard in <see cref="Run"/> and appear to work.
    /// <para>
    /// <see cref="UnionExpression"/> and an OR both lower to the same set union. They are distinct nodes because
    /// they say different things about their operands - an OR combines alternative <em>values</em> of one
    /// parameter, a union combines independent row-producing <em>legs</em> (the shape a SMART compartment expands
    /// to, where one leg is a compartment traversal, another a type filter, another an orphan scan) - but once
    /// each operand has become a CTE that distinction has no expression left in the plan.
    /// </para>
    /// <para>
    /// <see cref="UnionExpression.Operator"/> is deliberately not consulted. A CTE here yields a set of
    /// <c>(ResourceTypeId, ResourceSurrogateId)</c> identities, so a duplicate is the same row admitted by two
    /// legs, never two distinct results. UNION ALL would let such a row be counted twice by <c>_total</c> and
    /// consume two slots of a page, so the distinct union is the only correct emission for either operator.
    /// </para></summary>
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
        UnionExpression when resourceType is null => throw new NotSupportedException(
            "A union of row-producing legs is not supported in system-level search in this phase -- a leg that " +
            "is purely resource-column predicates (the SMART compartment's 'the compartment resource itself' " +
            "and 'universal resource types' legs both are) folds into a typed ResourceSource, which needs a " +
            "concrete type to scope against. Guarding at the dispatch choke point rather than letting such a " +
            "leg reach the leaf dispatcher, which would reject it with a message about the wrong problem."),
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

    /// <summary>Returns the resource-column predicate for a single wrapped leaf, or null if it is not one.</summary>
    private static Predicate? TryExtractResourceColumnPredicate(Expression expression, LeafContext leafContext)
        => expression is SearchParameterExpression wrapped
            ? TryLowerResourceColumn(wrapped.Expression, leafContext)
            : null;

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
    /// Lowers a chain's target expression or a union leg within its own scope, folding any resource-column
    /// predicates into the scope's ResourceSource (such a scope has no outer WHERE to attach them to) and
    /// intersecting with the ordinary match when both are present.
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

        // _type orders by the resource's type id, which the match set already projects as T1. It is not an
        // ordering over type *names* - it is the storage layer's own type ordering, which is what a FHIR
        // server sorting on _type over a partitioned Resource table gives a client, and what makes
        // "_sort=_type,_lastUpdated" the natural (T1, Sid1) clustered order rather than a re-sort.
        //
        // These three are exactly the codes ResourceColumnLoweringRule.IsResourceColumnCode recognises, so
        // every resource column is now sortable and no fall-through guard is needed: any code reaching the
        // SearchParamId lookup below is a real search parameter that Resolve collected.
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
