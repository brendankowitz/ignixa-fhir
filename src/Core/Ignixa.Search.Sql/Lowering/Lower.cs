using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;
using Ignixa.Search.Sql.Compilation;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>The compiler's Lower stage: turns a bound Expression tree into a <see cref="QueryPlan"/>, a pure
/// value (all I/O already happened in Resolve). Handles predicate leaves, composites, chains up to
/// <c>StructuralContext.MaxChainDepth</c>,
/// compartment searches, and _include/_revinclude/_sort/paging.</summary>
internal static class Lower
{
    /// <summary>Lowers a whole search into a QueryPlan: extracts resource-column predicates into an outer WHERE,
    /// lowers the remaining expression (or a bare resource source) into the CTE graph, then attaches includes,
    /// sort, and paging. A null target type is allowed only for a wildcard compartment or system-level search,
    /// and the two differ. A wildcard compartment search admits a CompartmentSearchExpression and
    /// resource-column predicates only — every other residue throws, a chain included — and rejects _sort. A
    /// system-level search lowers its leaves cross-type and accepts both _sort and a chain (which names its own
    /// types, see <see cref="StructuralLoweringDispatcher.LowerNode"/>), but still rejects :not/:missing=true,
    /// _not-referenced, :text and $everything. _include/_revinclude throws under either.</summary>
    internal static LoweredPlan Run(CompilationContext context, SymbolTable symbols)
    {
        ArgumentNullException.ThrowIfNull(context);

        var options = context.Options;
        var targetResourceType = context.TargetResourceType;
        var includeLimit = options.IncludeLimit;
        var sort = context.Sort;

        var sortPhase = options.SortPhase;
        var shape = options.Shape;
        var includesOnly = shape is ResultShape.IncludesPage;

        // Paging hangs off Matches, so a count or an includes page cannot carry one at all — the combinations
        // those shapes reject are unrepresentable here rather than guarded. Paging is itself one closed choice,
        // so the T-SQL restriction that TOP cannot combine with OFFSET/FETCH (error 10741) needs no guard
        // either: only one of these locals can be non-null.
        var paging = (shape as ResultShape.Matches)?.Paging;
        var keyset = paging as SearchPaging.Keyset;
        var top = keyset?.Top;
        var page = keyset?.Boundary;

        var offsetPage = paging is SearchPaging.Offset offset ? RequireOffsetSpec(offset) : null;

        RejectUnsupportedOptions(top, includeLimit, sortPhase, shape, sort);

        var accessConstraintApplier = new AccessConstraintApplier(context.AccessConstraints);
        var allowedResourceTypeFilter = new AllowedResourceTypeFilter(context.AllowedResourceTypes, symbols);
        var lowerContext = new StructuralContext(symbols, context.ApproximationReferenceTime, accessConstraintApplier);

        var (match, outerPredicate) = LowerMatchSet(context, lowerContext, accessConstraintApplier, allowedResourceTypeFilter);

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

        var includeStages = BuildAuthorizedIncludeStages(context, symbols, lowerContext, accessConstraintApplier, allowedResourceTypeFilter);

        if (includesOnly && includeStages is not { Count: > 0 })
        {
            throw new NotSupportedException(
                "IncludesPage was requested with no _include or _revinclude stages, which can only ever " +
                "return an empty result. This is a caller error rather than a query that legitimately " +
                "matches nothing, so it is reported rather than silently emitted.");
        }

        // A _sort is still allowed here: its ordering role drops (include rows page by (T1, Sid1), not the sort
        // key), but SortPhase (MissingPrimary/Valued) is a *filter* that partitions the match set seeding the
        // include stages, so it rides into the match-page CTE independently of ORDER BY. A keyset boundary, a
        // Top cap and an offset page are the genuinely unsound combinations, and hanging SearchPaging off
        // ResultShape.Matches makes all three unrepresentable rather than rejected.

        // BuildSortSpec validates the sort -- key count, key types, and MissingPrimary against a resource-column
        // primary key -- so it runs for every shape. A count reads it only when it asked to be restricted to the
        // phase; otherwise SqlBuilder ignores it and counts the whole match set.
        var sortSpec = BuildSortSpec(sort, sortPhase, symbols);

        RejectUnsoundKeysetPage(page, sortSpec, sortPhase);

        var matchSpec = new MatchPageSpec(
            match,
            Top: top,
            OuterPredicate: outerPredicate,
            Sort: sortSpec,
            Page: page,
            Shape: shape,
            SurrogateRange: context.SurrogateRange,
            SearchParameterHash: context.Options.SearchParameterHash is { } hash ? new SqlParameterRef(hash) : null,
            OffsetPage: offsetPage);
        List<CteDefinition> ctes = [.. lowerContext.Ctes];
        CteRef? includeSeed = null;

        if (!matchSpec.CountOnly && includeStages is { Count: > 0 })
        {
            var matchPage = new CteRef(ctes.Count);
            ctes.Add(new CteDefinition.MatchPage(matchSpec));
            includeSeed = matchPage;

            if (offsetPage is { ProbeExtraRow: true } && includeStages.Any(stage => stage.SeedFromMatch))
            {
                includeSeed = new CteRef(ctes.Count);
                ctes.Add(new CteDefinition.MatchSeed(matchPage, matchSpec));
            }
        }

        var query = new QueryPlan(
            ctes,
            matchSpec,
            Includes: includeStages,
            Visibility: context.Visibility,
            IncludeSeed: includeSeed);

        return new LoweredPlan(query, new PlanProvenance(lowerContext.Origins));
    }

    /// <summary>The <see cref="OffsetSpec"/> an OFFSET/FETCH page must carry, rejecting a missing or
    /// out-of-range one before it reaches the emitter.</summary>
    private static OffsetSpec RequireOffsetSpec(SearchPaging.Offset offset)
    {
        // A positional record parameter cannot enforce non-nullness against a caller who ignores the
        // annotation, and a null Spec would fall through to an unpaged statement returning every row.
        var offsetPage = offset.Spec
            ?? throw new NotSupportedException(
                "SearchPaging.Offset requires an OffsetSpec. Use SearchPaging.Keyset, or leave Paging null, " +
                "to compile without an OFFSET/FETCH page.");

        // A zero Limit is legal only alongside a probe row: that is the phase-boundary case where the
        // whole remaining budget IS the lookahead row (see the two-phase sort executor), and it still
        // fetches one row.
        if (offsetPage.Offset < 0 || offsetPage.Limit < 0 || offsetPage.FetchCount <= 0)
        {
            throw new NotSupportedException(
                $"OffsetSpec must skip a non-negative row count and fetch a positive one; got Offset " +
                $"{offsetPage.Offset}, Limit {offsetPage.Limit} and ProbeExtraRow " +
                $"{offsetPage.ProbeExtraRow}, fetching {offsetPage.FetchCount}. OFFSET/FETCH rejects " +
                $"both at runtime.");
        }

        return offsetPage;
    }

    /// <summary>Rejects the options-level combinations no plan can satisfy: an out-of-range row cap or include
    /// budget, an unrecognised sort phase, and the two phase-sensitive shapes that need a _sort the query does
    /// not have. The guards fire in the order written, which the messages depend on.</summary>
    private static void RejectUnsupportedOptions(
        int? top,
        int includeLimit,
        SortPhase sortPhase,
        ResultShape shape,
        IReadOnlyList<SortExpression> sort)
    {
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
    }

    /// <summary>Lowers the match set into the CTE graph and applies both authorization controls to it, returning
    /// the match alongside the resource-column predicate peeled off into the outer WHERE.</summary>
    private static (CteRef Match, Predicate? OuterPredicate) LowerMatchSet(
        CompilationContext context,
        StructuralContext lowerContext,
        AccessConstraintApplier accessConstraintApplier,
        AllowedResourceTypeFilter allowedResourceTypeFilter)
    {
        var expression = context.Expression;
        var targetResourceType = context.TargetResourceType;

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
            var (remaining, extractedPredicate) = ResourceColumnExtractor.ExtractResourceColumnPredicates(expression, lowerContext.LeafContext);
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
                    StructuralLoweringDispatcher.LowerNode(remaining, lowerContext, targetResourceType),
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
                (_, PatientEverythingExpression) => accessConstraintApplier.ApplyToTypes(match, lowerContext, StructuralLoweringDispatcher.LowerScopedExpression),
                ({ } matchType, _) => accessConstraintApplier.Apply(match, matchType, lowerContext, StructuralLoweringDispatcher.LowerScopedExpression),
                _ => accessConstraintApplier.ApplyToTypes(match, lowerContext, StructuralLoweringDispatcher.LowerScopedExpression),
            };
        }

        // The allow-list is the other authorization control: everything not permitted is removed (a plain
        // intersect with the allowed types' base set), so dropping it here fails open and widens the query.
        // Applied after the access constraints; order is irrelevant since both only remove rows.
        if (!allowedResourceTypeFilter.IsEmpty)
        {
            match = allowedResourceTypeFilter.RestrictMatch(match, lowerContext);
        }

        return (match, outerPredicate);
    }

    /// <summary>Plans the include/:iterate stages and applies both authorization controls to each, or returns
    /// null when the query has none.</summary>
    private static IReadOnlyList<IncludeStage>? BuildAuthorizedIncludeStages(
        CompilationContext context,
        SymbolTable symbols,
        StructuralContext lowerContext,
        AccessConstraintApplier accessConstraintApplier,
        AllowedResourceTypeFilter allowedResourceTypeFilter)
    {
        var targetResourceType = context.TargetResourceType;
        var includes = context.Includes;
        var revIncludes = context.RevIncludes;

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
            includeStages = IncludeStagePlanner.BuildIncludeStages(includes, revIncludes, symbols, targetResourceType, context.Options.IncludeLimit);
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
                    var bindings = accessConstraintApplier.BindIncludeStage(stage.OutputTypeIds, symbols, lowerContext, StructuralLoweringDispatcher.LowerScopedExpression);
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

        return includeStages;
    }

    /// <summary>Rejects a keyset boundary whose emitted seek would disagree with the emitted ORDER BY and so
    /// would silently drop rows at the page seam.</summary>
    private static void RejectUnsoundKeysetPage(PageSpec? page, SortSpec? sortSpec, SortPhase sortPhase)
    {
        // A typeless boundary breaks its final tie on Sid1 alone and omits the type column, which agrees with the
        // ORDER BY only for a custom sort (every other sort keeps m.T1 as a tiebreak). Rule shared with
        // SqlBuilder via KeysetPageInvariants, which also enforces it for direct QueryPlan callers.
        if (KeysetPageInvariants.TypelessPageNeedsCustomSort(page, sortSpec))
        {
            throw new NotSupportedException(
                "A typeless keyset Page (BoundaryResourceTypeId is null) requires a custom (search-parameter) " +
                "_sort such as name or birthdate. The sort here is " +
                (sortSpec is null ? "absent" : "a resource-column sort (_lastUpdated / _type / _id)") +
                ", whose keyset order includes the resource type, so a type-free seek would disagree with the " +
                "ORDER BY and paging would be unsound. Use a typed Page here, or a custom sort for a typeless Page.");
        }

        // A custom sort orders by (sort keys…, Sid1) with no type component, so a typed boundary would seek
        // type-major and drop rows within a run of tied sort values at the page seam. Rule shared with
        // SqlBuilder via KeysetPageInvariants.
        if (KeysetPageInvariants.TypedPageConflictsWithCustomSort(page, sortSpec))
        {
            throw new NotSupportedException(
                "A typed keyset Page (BoundaryResourceTypeId is non-null) cannot be combined with a custom " +
                "(search-parameter) _sort such as name or birthdate: the emitted ORDER BY is (sort keys…, Sid1) " +
                "while a typed boundary seeks type-major, so rows are silently dropped at the page seam. Decode " +
                "the continuation token to a typeless Page (BoundaryResourceTypeId: null) for a custom sort; the " +
                "type component is redundant because ResourceSurrogateId is globally unique.");
        }

        // A boundary decoded under one phase carries values for that phase's active keys, so carrying it across
        // a Valued/MissingPrimary transition seeks on the wrong key set. Only Matches can carry a boundary at
        // all, so no shape exemption is needed here. Rule shared with SqlBuilder via KeysetPageInvariants,
        // where the plan's flat fields keep the combination representable.
        if (KeysetPageInvariants.BoundaryCountDisagreesWithPhase(page, sortSpec))
        {
            throw new NotSupportedException(
                $"The keyset boundary carries {page!.Boundary.Count} value(s) but {nameof(SortPhase)}." +
                $"{sortPhase} has {KeysetPageInvariants.ActiveKeyCount(sortSpec)} active sort key(s). Decode the continuation " +
                "token for the phase you are reading; a boundary never survives a Valued/MissingPrimary " +
                "transition.");
        }
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
        if (sortExpression.Parameter.Code == SearchParameterNames.LastUpdated)
        {
            return new SortKey(null, SortKeyKind.LastUpdated, sortExpression.SortOrder);
        }

        if (sortExpression.Parameter.Code == SearchParameterNames.Id)
        {
            return new SortKey(null, SortKeyKind.ResourceId, sortExpression.SortOrder);
        }

        // _type orders by the resource's type id (T1) -- the storage layer's own type ordering, not an ordering
        // over type names, which is what makes "_sort=_type,_lastUpdated" the natural (T1, Sid1) clustered order.
        // These three are IntrinsicSearchParameters.Codes, so any code reaching the SearchParamId lookup below
        // is a real search parameter Resolve collected.
        if (sortExpression.Parameter.Code == SearchParameterNames.ResourceType)
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
}
