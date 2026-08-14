namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// Rejects the plan shapes that have no coherent SQL rendering. Complements <see cref="QueryPlanValidator"/>,
/// which guards the CTE graph's structure: this one judges the shape, paging and sort combinations. Reached
/// only through <see cref="QueryPlanValidator.Validate"/>, so every entry point that validates a plan applies
/// both — the two were previously separate entry points and <c>Describe</c> ran only the structural half,
/// explaining plans that <c>Run</c> refused.
/// </summary>
internal static class PlanShapeValidator
{
    /// <summary>Runs every shape-level guard.</summary>
    internal static void Validate(QueryPlan plan)
    {
        RejectUnsupportedCombinations(plan);
    }

    /// <summary>Rejects the plan shapes that have no coherent SQL rendering, before any text is produced.</summary>
    private static void RejectUnsupportedCombinations(QueryPlan plan)
    {
        if (plan.IncludesOnly && plan.Includes is not { Count: > 0 })
        {
            throw new NotSupportedException(
                "ResultShape.IncludesPage requires at least one include stage; with none it can only ever " +
                "return an empty result. Add an _include or _revinclude, or use ResultShape.Matches.");
        }

        if (plan.IncludesOnly && plan.Page is not null)
        {
            // A Sort is still allowed here: its phase filters the seed match set rather than ordering it.
            throw new NotSupportedException(
                "ResultShape.IncludesPage cannot be combined with a keyset Page. An includes page bounds its " +
                "match set by a surrogate-id range and pages its include rows by a resume boundary over " +
                "(T1, Sid1); a keyset Page seeks the match rows by the sort-key boundary instead, silently " +
                "changing which resources are included. Put the resume boundary in " +
                "ResultShape.IncludesPage.Resume and clear Page.");
        }

        if (plan.IncludesOnly && (plan.Top is not null || plan.OffsetPage is not null))
        {
            throw new NotSupportedException(
                "ResultShape.IncludesPage cannot be combined with " +
                (plan.Top is not null ? "a Top cap" : "an OffsetPage") +
                ". Either one bounds the match set that seeds the include stages, dropping include rows with no " +
                "indication that they are missing. Bound the match set with SurrogateRange and page the include " +
                "rows with ResultShape.IncludesPage.Resume.");
        }

        // Ordered before the cross-stage agreement guard below: an includes page whose limits disagree AND
        // are out of range would otherwise be told to make them agree, which leaves the plan invalid.
        // Guarded independently of Lower.Run because QueryPlan is a public construction surface. Every stage
        // is checked because a stage Limit reaches a TOP (Limit + 1) on the ordinary includes path, where
        // int.MaxValue overflows it to a negative row count. The includes page budgets globally from stage 0
        // instead, so its later stages carry a limit that is never emitted — still rejected, so a plan means
        // the same thing under either shape.
        if (plan.Includes is { Count: > 0 } stages)
        {
            for (var i = 0; i < stages.Count; i++)
            {
                if (stages[i].Limit is < 0 or int.MaxValue)
                {
                    throw new NotSupportedException(
                        $"Include stage limits must be between 0 and {int.MaxValue - 1}; stage {i} has " +
                        $"{stages[i].Limit}. The limit is emitted as TOP (Limit + 1) to probe for truncation, " +
                        "so a negative value is a SQL Server runtime error and int.MaxValue overflows the probe.");
                }
            }
        }

        if (plan.IncludesOnly && plan.Includes is { Count: > 0 } includeStages
            && includeStages.Any(s => s.Limit != includeStages[0].Limit))
        {
            // One TOP is applied over the union of every stage (budget from includes[0].Limit), so differing
            // per-stage limits would silently page on whichever limit is first.
            throw new NotSupportedException(
                "ResultShape.IncludesPage applies one page budget across the union of every include stage, so " +
                "the stages must agree on a single Limit. A per-stage limit has no coherent meaning here, and " +
                "the mismatch is reported rather than silently paged on whichever limit is first.");
        }

        // Any phase other than MissingPrimary falls through to the Valued segment, which hands back rows the
        // caller driving the two-phase loop has already seen. An undefined enum value is representable — a
        // cast, a deserialised int, or a case added later — so it is rejected rather than reinterpreted.
        if (plan.Sort is { Phase: var sortPhase } && !Enum.IsDefined(sortPhase))
        {
            throw new NotSupportedException(
                $"SortPhase '{(int)sortPhase}' is not a phase this compiler recognises. Use " +
                $"{nameof(SortPhase)}.{nameof(SortPhase.Valued)} or " +
                $"{nameof(SortPhase)}.{nameof(SortPhase.MissingPrimary)}.");
        }

        // Keys.Count, not a null check: SortSpec is a positional record, so SortSpec([], Valued) is
        // constructible and would emit no sort join and no MissingPrimary filter — a whole-set count
        // silently contradicting the restriction.
        if (plan.EffectiveShape is ResultShape.Count.CurrentSortPhase && plan.Sort is not { Keys.Count: > 0 })
        {
            throw new NotSupportedException(
                "A count was asked to restrict itself to the sort phase but the plan carries no sort keys, so " +
                "there is no segment to restrict it to. Use ResultShape.Count.AllMatches to count the whole " +
                "match set.");
        }

        // Guarded independently of Lower.Run because QueryPlan is a public construction surface.
        if (plan.Top is < 0)
        {
            throw new NotSupportedException(
                $"Top must not be negative; got {plan.Top}. TOP with a negative row count is a SQL Server " +
                "runtime error, so it is reported at emit time instead.");
        }

        // EmitMissingPrimaryFilter and EmitSeekPredicate both index Keys[0]; a phased sort with no keys has no
        // primary key to be missing.
        if (plan.Sort is { Phase: SortPhase.MissingPrimary, Keys.Count: 0 })
        {
            throw new NotSupportedException(
                "A MissingPrimary sort phase requires at least one sort key: the phase is defined by the " +
                "absence of the primary key, so with no keys there is nothing to partition the match set on.");
        }

        // Mirror of Lower.BuildSortSpec's cap. Guarded independently because QueryPlan is a public construction
        // surface, and the documented `plan with { Query = … }` rewrite would otherwise emit a fourth sort join
        // and a fourth SortValueN column, silently defeating a cap SortSpec and the README both advertise.
        if (plan.Sort is { } sortKeyCap && sortKeyCap.Keys.Count > 3)
        {
            throw new NotSupportedException(
                $"_sort supports at most 3 keys (got {sortKeyCap.Keys.Count}) -- a cap on per-request join cost, " +
                "since each key adds a join and a projected sort value.");
        }

        RejectUnderspecifiedSortKeys(plan.Sort);

        // Guarded independently of Lower.Run because QueryPlan is a public construction surface.
        if (plan.OffsetPage is not null && (plan.Top is not null || plan.Page is not null))
        {
            throw new NotSupportedException(
                "OffsetPage cannot be combined with Top or a keyset Page: TOP alongside OFFSET/FETCH is " +
                "rejected by SQL Server, and a keyset seek alongside OFFSET/FETCH applies two independent " +
                "paging mechanisms to one query.");
        }

        // Guarded independently of Lower.Run because QueryPlan is a public construction surface.
        // A zero Limit is legal only alongside a probe row: the page itself is empty but the lookahead row
        // still makes the fetch positive. See OffsetSpec.
        if (plan.OffsetPage is { } offsetSpec && (offsetSpec.Offset < 0 || offsetSpec.Limit < 0 || offsetSpec.FetchCount <= 0))
        {
            throw new NotSupportedException(
                $"OffsetPage must skip a non-negative row count and fetch a positive one; got Offset " +
                $"{offsetSpec.Offset}, Limit {offsetSpec.Limit} and ProbeExtraRow {offsetSpec.ProbeExtraRow}, " +
                $"fetching {offsetSpec.FetchCount}. OFFSET/FETCH rejects both at runtime.");
        }

        // Guarded independently of Lower.Run because QueryPlan is a public construction surface. Every node
        // that carries a resource-type list renders it as an OR of equalities and interpolates the joined
        // string straight into its WHERE clause, so joining zero of them leaves the clause empty and the
        // statement does not parse -- an opaque SQL Server error rather than a diagnosis here.
        for (var i = 0; i < plan.Ctes.Count; i++)
        {
            // Not a type list, so it gets its own message rather than being folded into the switch below:
            // the parts are CteRefs joined with UNION, and telling the caller to "name the resource types"
            // would send them to add something this node does not have.
            if (plan.Ctes[i] is CteDefinition.Union { Parts.Count: 0 })
            {
                throw new NotSupportedException(
                    $"Ctes[{i}].{nameof(CteDefinition.Union.Parts)} names no CTEs to union. The parts are joined " +
                    "with UNION, so joining zero of them leaves the CTE body empty and \"cteN AS ()\" does not " +
                    "parse. Name the CTEs the union combines, or remove the node -- a union of nothing is not " +
                    "expressible as an empty body.");
            }

            var emptyTypeList = plan.Ctes[i] switch
            {
                CteDefinition.ChainJoin { OutputResourceTypeIds.Count: 0 } => nameof(CteDefinition.ChainJoin.OutputResourceTypeIds),
                CteDefinition.ReferencedTypeExpansion { OutputResourceTypeIds.Count: 0 } => nameof(CteDefinition.ReferencedTypeExpansion.OutputResourceTypeIds),
                CteDefinition.CompartmentSource { ResourceTypeIds.Count: 0 } => nameof(CteDefinition.CompartmentSource.ResourceTypeIds),
                _ => null,
            };

            if (emptyTypeList is not null)
            {
                throw new NotSupportedException(
                    $"Ctes[{i}].{emptyTypeList} names no resource type. The list is rendered as an OR of type-id " +
                    "equalities and interpolated unconditionally into the WHERE clause, so an empty one emits no " +
                    "filter text and the statement does not parse. Name the types the node should match, or remove " +
                    "the node -- a node that should match nothing is not expressible as an absent filter.");
            }
        }

        // All three page guards below describe ways the emitted seek would disagree with the emitted ORDER BY.
        // A count emits neither -- EmitCountOnlyShape never reads Page -- so one exemption covers all three
        // rather than each restating it. Options-level callers cannot reach this at all: SearchPaging hangs off
        // ResultShape.Matches, so only a hand-built QueryPlan can pair a boundary with a count.
        if (!plan.CountOnly)
        {
            RejectUnsupportedPageCombinations(plan);
        }
    }

    /// <summary>
    /// Rejects a sort key whose kind promises a lookup it does not carry the coordinates for.
    /// <see cref="SortKey"/> documents that <c>SearchParamId</c> is null only for the resource-column kinds
    /// and that <c>Table</c>/<c>Column</c> are non-null only for <see cref="SortKeyKind.Aggregated"/>, but it
    /// is a public record with all three optional, so the invariant is documentation until it is checked.
    /// Unchecked, the emitters dereference or interpolate them: the aggregated join reads
    /// <c>key.Column!.Name</c> and throws NullReferenceException — a type neither TryCompile nor the
    /// plan-trace guard catches, so it escapes a Try* method — and a null SearchParamId interpolates to
    /// nothing, emitting <c>SearchParamId =  AND</c>, which is handed back as a successful compile and fails
    /// as an opaque syntax error at execution.
    /// </summary>
    private static void RejectUnderspecifiedSortKeys(SortSpec? sort)
    {
        if (sort is null)
        {
            return;
        }

        for (var i = 0; i < sort.Keys.Count; i++)
        {
            var key = sort.Keys[i];
            if (key.Kind is SortKeyKind.String or SortKeyKind.Date or SortKeyKind.Aggregated && key.SearchParamId is null)
            {
                throw new NotSupportedException(
                    $"Sort key {i} is {nameof(SortKeyKind)}.{key.Kind} but names no SearchParamId. The kind " +
                    "selects a search-param table whose rows are filtered by that id, and an absent one " +
                    "renders no filter text at all, so the statement does not parse. Supply the id, or use a " +
                    "resource-column kind (LastUpdated / ResourceType / ResourceId), which needs none.");
            }

            if (key.Kind is SortKeyKind.Aggregated && (key.Table is null || key.Column is null))
            {
                throw new NotSupportedException(
                    $"Sort key {i} is {nameof(SortKeyKind)}.{nameof(SortKeyKind.Aggregated)} but names no " +
                    $"{(key.Table is null ? nameof(SortKey.Table) : nameof(SortKey.Column))}. An aggregated " +
                    "key emits MIN/MAX over a named search-param table and column, which Lower resolves from " +
                    "the catalog; without both there is nothing to aggregate over.");
            }
        }
    }

    /// <summary>
    /// Rejects keyset boundaries that would emit a seek disagreeing with the plan's ORDER BY. Only called for
    /// plans that emit a seek; see the exemption in <see cref="RejectUnsupportedCombinations"/>.
    /// </summary>
    private static void RejectUnsupportedPageCombinations(QueryPlan plan)
    {
        // A typeless seek is sound only against a type-free ORDER BY (sort keys…, Sid1), which EmitOrderBy
        // produces only for a custom sort; other sorts keep m.T1, so the seek would drop tied rows.
        if (KeysetPageInvariants.TypelessPageNeedsCustomSort(plan.Page, plan.Sort))
        {
            throw new NotSupportedException(
                "A typeless keyset Page (BoundaryResourceTypeId is null) requires a custom (search-parameter) " +
                "_sort such as name or birthdate. The plan's sort is " +
                (plan.Sort is null ? "absent" : "a resource-column sort (_lastUpdated / _type / _id)") +
                ", whose keyset order includes the resource type, so a type-free seek would disagree with the " +
                "ORDER BY and paging would be unsound. Use a typed Page here, or a custom sort for a typeless Page.");
        }

        // Mirror of the guard above: EmitOrderBy drops m.T1 for any custom sort, but a non-null boundary makes
        // EmitSeekPredicate emit a type-major seek, so the two disagree and lose rows at the page seam.
        if (KeysetPageInvariants.TypedPageConflictsWithCustomSort(plan.Page, plan.Sort))
        {
            throw new NotSupportedException(
                "A typed keyset Page (BoundaryResourceTypeId is non-null) cannot be combined with a custom " +
                "(search-parameter) _sort. EmitOrderBy drops the m.T1 tiebreak for a custom sort, ordering by " +
                "(sort keys…, Sid1), while a typed boundary makes EmitSeekPredicate emit a type-major seek. " +
                "Within a run of tied sort values a row of a lower type id but higher surrogate id then sorts " +
                "after the boundary yet is excluded by the seek, and is silently dropped at the page seam. " +
                "Use a typeless Page (BoundaryResourceTypeId: null) for a custom sort; the type component is " +
                "redundant because ResourceSurrogateId is globally unique.");
        }

        // A boundary decoded in one phase carries values for that phase's active keys, so reusing it across a
        // Valued/MissingPrimary transition would seek on the wrong key set. Checked here rather than in
        // EmitSeekPredicate so the failure surfaces before any SQL is written.
        if (KeysetPageInvariants.BoundaryCountDisagreesWithPhase(plan.Page, plan.Sort))
        {
            throw new NotSupportedException(
                $"PageSpec.Boundary has {plan.Page!.Boundary.Count} value(s) but the current SortSpec phase " +
                $"has {KeysetPageInvariants.ActiveKeyCount(plan.Sort)} active key(s) -- boundary values must be freshly decoded " +
                "for the current phase, never reused across a Valued/MissingPrimary transition.");
        }
    }
}
