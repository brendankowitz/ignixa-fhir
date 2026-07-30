#pragma warning disable CA1724

using Ignixa.Search.Expressions;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;
using static Ignixa.Search.Sql.Builders.SqlLabels;

namespace Ignixa.Search.Sql.Builders;

/// <summary>
/// Turns a <see cref="QueryPlan"/> into parameterized T-SQL text, deterministically — the same plan
/// always emits byte-identical SQL. Every <see cref="CteDefinition"/> entry becomes its own named CTE, so
/// Match can reference any nesting depth without special-casing the outer SELECT. No user value is ever
/// inlined: every <see cref="SqlParameterRef"/> becomes a named @pN parameter.
/// </summary>
internal static class SqlBuilder
{
    /// <summary>
    /// Renders a plan to SQL and its bound parameters by selecting one of three terminal shapes and
    /// delegating to its emitter: a COUNT_BIG SELECT when CountOnly, a plain (T1, Sid1) select (with
    /// optional sort/paging) when there are no includes, or a match-page CTE plus per-stage include CTEs
    /// unioned into a (T1, Sid1, IsMatch, IsPartial) result.
    /// </summary>
    public static EmittedSql Run(QueryPlan plan, EmitOptions? options = null)
    {
        RejectDanglingReferences(plan);
        RejectUnsupportedCombinations(plan);

        var parameters = new List<EmittedSqlParameter>();
        var writer = new SqlTextWriter(options?.IncludeTextRanges ?? false);
        var visibility = plan.EffectiveVisibility;
        var cteBlocks = EmitCteBlocks(plan, parameters, visibility);

        if (plan.CountOnly)
        {
            EmitCountOnlyShape(plan, writer, cteBlocks, parameters);
        }
        else if (plan.Includes is { Count: > 0 } includes)
        {
            EmitIncludesShape(plan, includes, writer, cteBlocks, parameters, visibility);
        }
        else
        {
            EmitMatchOnlyShape(plan, writer, cteBlocks, parameters, visibility);
        }

        return new EmittedSql(writer.ToString(), parameters, writer.Ranges);
    }

    /// <summary>
    /// Rejects a CTE graph that is not closed and ordered. <see cref="QueryPlan"/> is a public construction
    /// surface and <c>plan with { Query = rewritten }</c> is a documented rewrite path, so a stale index has
    /// to fail here as a compilation failure rather than as an IndexOutOfRangeException from emission, or as
    /// SQL naming a CTE that does not exist yet. T-SQL binds CTEs in order, so a reference must point
    /// strictly backwards.
    /// </summary>
    private static void RejectDanglingReferences(QueryPlan plan)
    {
        RequireIndex(plan.Match.Index, plan.Ctes.Count, ReferenceBound.Defined, "QueryPlan.Match");

        for (var i = 0; i < plan.Ctes.Count; i++)
        {
            switch (plan.Ctes[i])
            {
                case CteDefinition.Intersect(var left, var right):
                    RequireChild(left, i, "Left");
                    RequireChild(right, i, "Right");
                    break;
                case CteDefinition.Except(var exceptLeft, var exceptRight):
                    RequireChild(exceptLeft, i, "Left");
                    RequireChild(exceptRight, i, "Right");
                    break;
                case CteDefinition.Union(var parts):
                    for (var part = 0; part < parts.Count; part++)
                    {
                        RequireChild(parts[part], i, "Parts", part);
                    }

                    break;
                case CteDefinition.ChainJoin chain:
                    RequireChild(chain.InnerMatch, i, "InnerMatch");
                    break;
                case CteDefinition.ReferencedTypeExpansion expansion:
                    RequireChild(expansion.Seed, i, "Seed");
                    break;
            }
        }

        RejectDanglingIncludeReferences(plan);

        static void RequireChild(CteRef reference, int ordinal, string member, int part = -1)
            => RequireIndex(reference.Index, ordinal, ReferenceBound.EarlierCte, "Ctes", ordinal, member, part);
    }

    /// <summary>
    /// Include stages occupy their own index space (incN), so their seeds are bounded by the stage count
    /// while their access constraints index into <see cref="QueryPlan.Ctes"/>.
    /// </summary>
    private static void RejectDanglingIncludeReferences(QueryPlan plan)
    {
        if (plan.Includes is not { Count: > 0 } includes)
        {
            return;
        }

        for (var i = 0; i < includes.Count; i++)
        {
            foreach (var seed in includes[i].SeedStages)
            {
                RequireIndex(seed, i, ReferenceBound.EarlierStage, "Includes", i, "SeedStages");
            }

            foreach (var constraint in includes[i].Constraints ?? [])
            {
                RequireIndex(
                    constraint.ConstraintCteIndex,
                    plan.Ctes.Count,
                    ReferenceBound.Defined,
                    "Includes",
                    i,
                    "Constraints");
            }
        }
    }

    /// <summary>What bounds a plan index: the whole CTE list, or the entries emitted before the referrer.</summary>
    private enum ReferenceBound
    {
        Defined,
        EarlierCte,
        EarlierStage,
    }

    /// <summary>
    /// Guards one plan index. The path is assembled only on failure so a valid plan allocates nothing here:
    /// <paramref name="owner"/> and <paramref name="member"/> are literals, and the ordinals are unboxed ints.
    /// </summary>
    private static void RequireIndex(
        int index,
        int exclusiveUpperBound,
        ReferenceBound bound,
        string owner,
        int ownerOrdinal = -1,
        string? member = null,
        int memberOrdinal = -1)
    {
        if (index >= 0 && index < exclusiveUpperBound)
        {
            return;
        }

        var path = ownerOrdinal < 0 ? owner : $"{owner}[{ownerOrdinal}].{member}";
        if (memberOrdinal >= 0)
        {
            path += $"[{memberOrdinal}]";
        }

        var limit = bound switch
        {
            ReferenceBound.Defined => $"the plan defines {exclusiveUpperBound} CTEs",
            ReferenceBound.EarlierCte => $"a CTE may only reference the {exclusiveUpperBound} emitted before it",
            _ => $"a stage may only seed from the {exclusiveUpperBound} emitted before it",
        };

        throw new NotSupportedException(
            $"{path} references index {index}, but {limit}. A plan whose CTE graph is not closed and ordered " +
            "cannot be emitted; a rewritten plan must renumber the references it moved.");
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
        // Guarded independently of Lower.Run because QueryPlan is a public construction surface, and every
        // path that emits an include stage writes TOP (Limit + 1): int.MaxValue overflows that to a negative
        // row count.
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

        // Guarded independently of Lower.Run because QueryPlan is a public construction surface.
        if (plan.OffsetPage is not null && (plan.Top is not null || plan.Page is not null))
        {
            throw new NotSupportedException(
                "OffsetPage cannot be combined with Top or a keyset Page: TOP alongside OFFSET/FETCH is " +
                "rejected by SQL Server, and a keyset seek alongside OFFSET/FETCH applies two independent " +
                "paging mechanisms to one query.");
        }

        // Guarded independently of Lower.Run because QueryPlan is a public construction surface.
        if (plan.OffsetPage is { } offsetSpec && (offsetSpec.Offset < 0 || offsetSpec.Limit <= 0))
        {
            throw new NotSupportedException(
                $"OffsetPage must skip a non-negative row count and fetch a positive one; got Offset " +
                $"{offsetSpec.Offset} and Limit {offsetSpec.Limit}. OFFSET/FETCH rejects both at runtime.");
        }

        // A typeless seek is sound only against a type-free ORDER BY (sort keys…, Sid1), which EmitOrderBy
        // produces only for a custom sort; other sorts keep m.T1, so the seek would drop tied rows.
        if (IsTypelessPage(plan) && !HasCustomSortKey(plan.Sort))
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
        if (plan.Page is { BoundaryResourceTypeId: not null } && HasCustomSortKey(plan.Sort))
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
        // EmitSeekPredicate so the failure surfaces before any SQL is written, and skipped for counts, which
        // emit no seek and are documented to ignore the boundary entirely.
        if (!plan.CountOnly && plan.Page is { } boundaryPage && boundaryPage.Boundary.Count != (plan.Sort?.ActiveKeyCount ?? 0))
        {
            throw new NotSupportedException(
                $"PageSpec.Boundary has {boundaryPage.Boundary.Count} value(s) but the current SortSpec phase " +
                $"has {plan.Sort?.ActiveKeyCount ?? 0} active key(s) -- boundary values must be freshly decoded " +
                "for the current phase, never reused across a Valued/MissingPrimary transition.");
        }
    }

    /// <summary>
    /// A keyset page whose boundary carries no resource-type component: its seek compares only the sort
    /// key(s) and the surrogate id. Sound because ResourceSurrogateId is globally unique.
    /// </summary>
    private static bool IsTypelessPage(QueryPlan plan) => plan.Page is { BoundaryResourceTypeId: null };

    /// <summary>
    /// A search-parameter-backed sort key (String/Date such as name or birthdate, or Aggregated) as opposed
    /// to the resource-column keys (_lastUpdated / _type / _id). Its keyset order is type-free: (sortValue…, Sid1).
    /// </summary>
    private static bool IsCustomSortKey(SortKeyKind kind)
        => kind is SortKeyKind.String or SortKeyKind.Date or SortKeyKind.Aggregated;

    /// <summary>
    /// True when the sort has any custom key, so the ORDER BY drops m.T1 and orders by (sort keys…, Sid1).
    /// Decided by the sort's keys, never by the page boundary, so every page of one walk shares one ordering.
    /// All keys are considered so a custom sort's missing-value segment stays type-free.
    /// </summary>
    private static bool HasCustomSortKey(SortSpec? sort)
        => sort is not null && sort.Keys.Any(k => IsCustomSortKey(k.Kind));

    /// <summary>
    /// Renders every <see cref="CteDefinition"/> as a named "cteN AS (...)" block, in plan order. Runs before
    /// any shape emits so the CTE's bound values take the leading @pN ordinals PlanExplainer reads back.
    /// </summary>
    private static List<string> EmitCteBlocks(
        QueryPlan plan,
        List<EmittedSqlParameter> parameters,
        ResourceVisibility visibility)
    {
        var cteBlocks = new List<string>(plan.Ctes.Count);
        for (var i = 0; i < plan.Ctes.Count; i++)
        {
            cteBlocks.Add($"{CteLabel(i)} AS (\n{EmitCte(plan.Ctes[i], parameters, visibility)}\n)");
        }

        return cteBlocks;
    }

    /// <summary>Writes the leading ";WITH " and the comma-separated CTE blocks, each in its own section.</summary>
    private static void WriteCteHeader(SqlTextWriter writer, List<string> cteBlocks)
    {
        writer.Append(";WITH ");
        writer.AppendJoin(",\n", cteBlocks, CteLabel, SqlRangeKind.Cte);
    }

    /// <summary>Writes a WHERE clause at the given indent, or nothing when there are no clauses.</summary>
    private static void WriteWhereSection(SqlTextWriter writer, List<string> clauses, int? seekClauseIndex, string indent)
    {
        if (clauses.Count == 0)
        {
            return;
        }

        writer.Append($"\n{indent}WHERE ");
        using (writer.Section(Where, SqlRangeKind.Where))
        {
            WriteAndJoinedClauses(writer, clauses, seekClauseIndex);
        }
    }

    /// <summary>
    /// Emits the Count shape: COUNT_BIG(DISTINCT m.Sid1) over the match CTE. Row caps, offsets and keyset
    /// boundaries are ignored, since a count is of the whole result set rather than a page of it. So is the
    /// sort, unless the shape is <see cref="ResultShape.Count.CurrentSortPhase"/>, which applies the phase's
    /// key join and its MissingPrimary filter.
    /// </summary>
    private static void EmitCountOnlyShape(
        QueryPlan plan,
        SqlTextWriter writer,
        List<string> cteBlocks,
        List<EmittedSqlParameter> parameters)
    {
        WriteCteHeader(writer, cteBlocks);
        writer.Append("\n");

        var phaseSort = plan.EffectiveShape is ResultShape.Count.CurrentSortPhase ? plan.Sort : null;

        var countSortJoins = EmitSortJoins(phaseSort);
        writer.Append($"SELECT COUNT_BIG(DISTINCT m.Sid1) FROM {CteLabel(plan.Match.Index)} m{countSortJoins}");

        if (NeedsResourceJoin(plan, includesProjection: false))
        {
            writer.Append("\nINNER JOIN dbo.Resource r ON r.ResourceTypeId = m.T1 AND r.ResourceSurrogateId = m.Sid1");
        }

        var whereClauses = new List<string>();

        if (plan.OuterPredicate is not null)
        {
            whereClauses.Add(EmitPredicate(plan.OuterPredicate, parameters, ResourceJoinQualifier));
        }

        if (phaseSort is { Phase: SortPhase.MissingPrimary } countPhaseSort)
        {
            whereClauses.Add(EmitMissingPrimaryFilter(countPhaseSort));
        }

        if (plan.SurrogateRange is { } range)
        {
            AppendSurrogateRangeClauses(whereClauses, range, parameters);
        }

        if (plan.SearchParameterHash is { } hash)
        {
            whereClauses.Add(EmitSearchParameterHashClause(hash, parameters));
        }

        WriteWhereSection(writer, whereClauses, seekClauseIndex: null, indent: string.Empty);
    }

    /// <summary>
    /// Emits the no-includes shape: a single (T1, Sid1) SELECT over the match CTE, with the sort key
    /// columns and joins, any projected resource columns, and the keyset ORDER BY.
    /// </summary>
    private static void EmitMatchOnlyShape(
        QueryPlan plan,
        SqlTextWriter writer,
        List<string> cteBlocks,
        List<EmittedSqlParameter> parameters,
        ResourceVisibility visibility)
    {
        var top = plan.Top is { } n ? $"TOP ({n}) " : string.Empty;
        var projectionCols = ProjectionColumns(plan.Projection);
        var projectionJoinFilter = projectionCols.Length > 0 ? ResourceRowFilter(visibility, "r.") : string.Empty;
        var sortJoins = EmitSortJoins(plan.Sort);
        var sortColumns = EmitSortSelectColumns(plan.Sort);

        var whereClauses = BuildMatchWhereClauses(plan, parameters, out var seekClauseIndex);

        WriteCteHeader(writer, cteBlocks);
        writer.Append("\n");
        writer.Append($"SELECT {top}m.T1, m.Sid1{sortColumns}{projectionCols} FROM {CteLabel(plan.Match.Index)} m{sortJoins}");

        // All three of outer predicate, projection, and hash filter share this one join.
        if (NeedsResourceJoin(plan, includesProjection: true))
        {
            writer.Append($"\nINNER JOIN dbo.Resource r ON r.ResourceTypeId = m.T1 AND r.ResourceSurrogateId = m.Sid1{projectionJoinFilter}");
        }

        WriteWhereSection(writer, whereClauses, seekClauseIndex, indent: string.Empty);

        writer.Append("\nORDER BY ");
        using (writer.Section(OrderBy, SqlRangeKind.OrderBy))
        {
            writer.Append(EmitOrderBy(plan.Sort));
        }

        if (plan.OffsetPage is { } offsetPage)
        {
            writer.Append($"\nOFFSET {EmitParam(new SqlParameterRef(offsetPage.Offset), parameters)} ROWS FETCH NEXT {EmitParam(new SqlParameterRef(offsetPage.Limit), parameters)} ROWS ONLY");
        }
    }

    /// <summary>
    /// Emits the includes shape: match-page CTE, include-stage CTEs, and the assembly stitching them into one
    /// (T1, Sid1, IsMatch, IsPartial) result. Two assemblies: the ordinary path unions each stage's limit
    /// companion and orders matches-first; the IncludesOnly path budgets once over the unlimited stage bodies
    /// and orders by (T1, Sid1) to resume from a boundary.
    /// </summary>
    private static void EmitIncludesShape(
        QueryPlan plan,
        IReadOnlyList<IncludeStage> includes,
        SqlTextWriter writer,
        List<string> cteBlocks,
        List<EmittedSqlParameter> parameters,
        ResourceVisibility visibility)
    {
        WriteCteHeader(writer, cteBlocks);
        writer.Append(",\n");
        WriteMatchPageCte(plan, writer, parameters);

        // Bind the boundary here — after the match-page CTE, before the stage loop — so it takes the first
        // stage-level @pN, preserving the leading-ordinal invariant EmitCteBlocks documents. Include CTEs bind
        // no parameters; the predicate it feeds is emitted later by EmitGlobalIncludesPage.
        (string Type, string Surrogate)? resumeParams = plan is { IncludesOnly: true, IncludeBoundary: { } boundary }
            ? (EmitParam(new SqlParameterRef(boundary.TypeId), parameters), EmitParam(new SqlParameterRef(boundary.SurrogateId), parameters))
            : null;

        for (var i = 0; i < includes.Count; i++)
        {
            WriteIncludeStageCtes(writer, includes[i], i, visibility, plan.IncludesOnly);
        }

        writer.Append("\n");

        if (plan.IncludesOnly)
        {
            EmitGlobalIncludesPage(plan, includes, writer, visibility, resumeParams);
            return;
        }

        using (writer.Section(Assembly, SqlRangeKind.Assembly))
        {
            writer.Append(string.Join("\nUNION ALL\n", BuildUnionArms(plan, includes, visibility)));
        }

        writer.Append("\nORDER BY ");
        using (writer.Section(OrderBy, SqlRangeKind.OrderBy))
        {
            writer.Append(EmitOuterOrderByForIncludes(plan.Sort));
        }
    }

    /// <summary>The derived-table alias the global includes page wraps its stage union in.</summary>
    private const string IncludeUnionAlias = "includeUnion";

    /// <summary>
    /// Emits the outer global-page SELECT for an IncludesOnly page: <c>SELECT DISTINCT TOP (@limit + 1)
    /// T1, Sid1, IsMatch, &lt;IsPartial&gt;</c> over the UNION of every include stage body, ordered by (T1, Sid1),
    /// budget applied once so the page resumes from a boundary. Arms use plain <c>UNION</c> (not UNION ALL) so a
    /// resource reachable via two stages is deduped before the COUNT_BIG(*) OVER() window keeps IsPartial honest.
    /// </summary>
    private static void EmitGlobalIncludesPage(
        QueryPlan plan,
        IReadOnlyList<IncludeStage> includes,
        SqlTextWriter writer,
        ResourceVisibility visibility,
        (string Type, string Surrogate)? resumeParams)
    {
        var budget = includes[0].Limit;
        var passThrough = ProjectionPassThroughColumns(plan.Projection);

        using (writer.Section(IncludePage, SqlRangeKind.IncludePage))
        {
            writer.Append(
                $"SELECT DISTINCT TOP ({budget + 1}) T1, Sid1, IsMatch,\n" +
                $"       CAST(CASE WHEN COUNT_BIG(*) OVER() > {budget} THEN 1 ELSE 0 END AS bit) AS IsPartial{passThrough}\n" +
                "FROM (\n");

            using (writer.Section(Assembly, SqlRangeKind.Assembly))
            {
                writer.Append(string.Join("\nUNION\n", BuildGlobalIncludesPageArms(plan, includes, visibility)));
            }

            writer.Append($"\n) {IncludeUnionAlias}");

            if (resumeParams is { } resume)
            {
                WriteWhereSection(
                    writer,
                    [$"(T1 > {resume.Type} OR (T1 = {resume.Type} AND Sid1 > {resume.Surrogate}))"],
                    seekClauseIndex: 0,
                    indent: string.Empty);
            }

            writer.Append("\nORDER BY ");
            using (writer.Section(OrderBy, SqlRangeKind.OrderBy))
            {
                writer.Append("T1 ASC, Sid1 ASC");
            }
        }
    }

    /// <summary>
    /// Builds the inner arms of the global includes page: one arm per include stage, each selecting its
    /// unlimited body, tagging it <c>CAST(0 AS bit)</c> as a non-match, and excluding rows already on the
    /// match page. Joined with plain <c>UNION</c> by the caller so cross-stage duplicates are deduped before
    /// the window count runs (see <see cref="EmitGlobalIncludesPage"/>).
    /// </summary>
    private static List<string> BuildGlobalIncludesPageArms(QueryPlan plan, IReadOnlyList<IncludeStage> includes, ResourceVisibility visibility)
    {
        var projectionCols = ProjectionColumns(plan.Projection);
        var hasActiveProjection = projectionCols.Length > 0;
        var projectionJoinFilter = hasActiveProjection ? ResourceRowFilter(visibility, "r.") : string.Empty;

        var arms = new List<string>();
        for (var i = 0; i < includes.Count; i++)
        {
            // Only the first arm names IsMatch: SQL Server takes a UNION's column names from its first SELECT.
            // Keyed on arms.Count so a future arm inserted ahead cannot move the alias off the first position.
            var isMatchAlias = arms.Count == 0 ? " AS IsMatch" : string.Empty;
            arms.Add(hasActiveProjection
                ? $"SELECT i.T1, i.Sid1, CAST(0 AS bit){isMatchAlias}{projectionCols} FROM {IncludeLabel(i)} i\n" +
                  $"INNER JOIN dbo.Resource r ON r.ResourceTypeId = i.T1 AND r.ResourceSurrogateId = i.Sid1{projectionJoinFilter}\n" +
                  $"WHERE NOT EXISTS (SELECT 1 FROM {MatchPage} m WHERE m.T1 = i.T1 AND m.Sid1 = i.Sid1)"
                : $"SELECT i.T1, i.Sid1, CAST(0 AS bit){isMatchAlias} FROM {IncludeLabel(i)} i\n" +
                  $"WHERE NOT EXISTS (SELECT 1 FROM {MatchPage} m WHERE m.T1 = i.T1 AND m.Sid1 = i.Sid1)");
        }

        return arms;
    }

    /// <summary>
    /// The projected columns as the outer global-page SELECT reads them from the union derived table:
    /// bracket-quoted and unqualified, since SQL Server drops the <c>r.</c> qualifier from derived-table column
    /// names. Empty for a null or empty projection.
    /// </summary>
    private static string ProjectionPassThroughColumns(ProjectionSpec? projection)
        => projection is null || projection.Columns.Count == 0
            ? string.Empty
            : ", " + string.Join(", ", projection.Columns.Select(c => $"[{c.Replace("]", "]]", StringComparison.Ordinal)}]"));

    /// <summary>
    /// Writes the cteMatchPage CTE: the same match row set the no-includes shape selects directly, named so
    /// the include stages and the UNION ALL can each reference it without re-deriving it.
    /// </summary>
    private static void WriteMatchPageCte(QueryPlan plan, SqlTextWriter writer, List<EmittedSqlParameter> parameters)
    {
        var top = plan.Top is { } n ? $"TOP ({n}) " : string.Empty;
        var sortJoins = EmitSortJoins(plan.Sort);

        // An includes-only page never orders by the sort key, so the match CTE projects no SortValueN columns.
        // The sort JOINs stay: the Valued phase's INNER join bounds the match set to rows that have the sort
        // value, and the include stages seed from that bounded set.
        var sortColumns = plan.IncludesOnly ? string.Empty : EmitSortSelectColumns(plan.Sort);

        // Projection is handled in the UNION ALL assembly, not here, so includesProjection is false.
        var resourceJoin = NeedsResourceJoin(plan, includesProjection: false)
            ? "\n    INNER JOIN dbo.Resource r ON r.ResourceTypeId = m.T1 AND r.ResourceSurrogateId = m.Sid1"
            : string.Empty;

        // A CTE's own ORDER BY is legal only alongside TOP or OFFSET/FETCH (SQL Server Msg 1033). The outer
        // UNION ALL's ORDER BY is a top-level SELECT and always legal regardless.
        var cteOrderBy = plan.Top is not null || plan.OffsetPage is not null
            ? $"\n    ORDER BY {EmitOrderBy(plan.Sort)}"
            : string.Empty;

        var whereClauses = BuildMatchWhereClauses(plan, parameters, out var seekClauseIndex);

        using (writer.Section(MatchPage, SqlRangeKind.MatchPage))
        {
            writer.Append(
                $"{MatchPage} AS (\n" +
                $"    SELECT {top}m.T1, m.Sid1{sortColumns}\n" +
                $"    FROM {CteLabel(plan.Match.Index)} m{sortJoins}{resourceJoin}");

            WriteWhereSection(writer, whereClauses, seekClauseIndex, indent: "    ");

            writer.Append(cteOrderBy);

            if (plan.OffsetPage is { } matchOffsetPage)
            {
                writer.Append($"\n    OFFSET {EmitParam(new SqlParameterRef(matchOffsetPage.Offset), parameters)} ROWS FETCH NEXT {EmitParam(new SqlParameterRef(matchOffsetPage.Limit), parameters)} ROWS ONLY");
            }

            writer.Append("\n)");
        }
    }

    /// <summary>
    /// Writes an include stage's CTEs. The ordinary path writes two — the unlimited body and its
    /// limit-applying companion. The IncludesOnly path writes only the body: its budget is applied once,
    /// globally, by <see cref="EmitGlobalIncludesPage"/>, so a per-stage limit companion would apply the
    /// budget twice.
    /// </summary>
    private static void WriteIncludeStageCtes(
        SqlTextWriter writer,
        IncludeStage stage,
        int index,
        ResourceVisibility visibility,
        bool includesOnly)
    {
        writer.Append(",\n");
        using (writer.Section(IncludeLabel(index), SqlRangeKind.Include))
        {
            writer.Append($"{IncludeLabel(index)} AS (\n{EmitIncludeStage(stage, visibility, includesOnly)}\n)");
        }

        if (includesOnly)
        {
            return;
        }

        writer.Append(",\n");
        using (writer.Section(IncludeLimitLabel(index), SqlRangeKind.IncludeLimit))
        {
            writer.Append($"{IncludeLimitLabel(index)} AS (\n{EmitIncludeLimitStage(stage, index)}\n)");
        }
    }

    /// <summary>
    /// Renders an include stage's limit-applying companion: <c>TOP (Limit + 1)</c> rows (budget plus the
    /// one-row truncation sentinel), each stamped with an IsPartial flag from the window count.
    /// </summary>
    /// <remarks>
    /// IsPartial is cast to <c>bit</c> to match the match arm's type in the union; leaving it int promotes the
    /// union column and breaks the documented bit contract.
    /// </remarks>
    private static string EmitIncludeLimitStage(IncludeStage stage, int index)
        => $"    SELECT TOP ({stage.Limit + 1}) T1, Sid1,\n" +
           $"           CAST(CASE WHEN COUNT_BIG(*) OVER() > {stage.Limit} THEN 1 ELSE 0 END AS bit) AS IsPartial\n" +
           $"    FROM {IncludeLabel(index)}\n" +
           $"    ORDER BY T1 ASC, Sid1 ASC";

    /// <summary>
    /// Builds the arms of the final UNION ALL: the match page (unless IncludesOnly) followed by one arm per
    /// include stage, every arm padded to the same (T1, Sid1, IsMatch, IsPartial, sort keys, projection) shape.
    /// </summary>
    private static List<string> BuildUnionArms(QueryPlan plan, IReadOnlyList<IncludeStage> includes, ResourceVisibility visibility)
    {
        var projectionCols = ProjectionColumns(plan.Projection);
        var hasActiveProjection = projectionCols.Length > 0;
        var projectionJoinFilter = hasActiveProjection ? ResourceRowFilter(visibility, "r.") : string.Empty;

        var activeSortKeyCount = ActiveKeyIndices(plan.Sort).Count;
        var nullSortColumns = string.Concat(Enumerable.Repeat(", NULL", activeSortKeyCount));
        var matchSortColumnRefs = string.Concat(Enumerable.Range(0, activeSortKeyCount).Select(o => $", SortValue{o}"));

        var arms = new List<string>();

        if (!plan.IncludesOnly)
        {
            arms.Add(hasActiveProjection
                ? $"SELECT m.T1, m.Sid1, CAST(1 AS bit) AS IsMatch, CAST(0 AS bit) AS IsPartial{matchSortColumnRefs}{projectionCols} FROM {MatchPage} m\n" +
                  $"INNER JOIN dbo.Resource r ON r.ResourceTypeId = m.T1 AND r.ResourceSurrogateId = m.Sid1{projectionJoinFilter}"
                : $"SELECT T1, Sid1, CAST(1 AS bit) AS IsMatch, CAST(0 AS bit) AS IsPartial{matchSortColumnRefs} FROM {MatchPage}");
        }

        for (var i = 0; i < includes.Count; i++)
        {
            // Only the first arm names IsMatch: SQL Server takes a UNION ALL's column names from its first
            // SELECT. Keyed on arms.Count (first arm overall), not i, so an arm inserted before this loop
            // cannot break the ordinal contract when IncludesOnly omits the match arm.
            var isMatchAlias = plan.IncludesOnly && arms.Count == 0 ? " AS IsMatch" : string.Empty;
            arms.Add(hasActiveProjection
                ? $"SELECT i.T1, i.Sid1, CAST(0 AS bit){isMatchAlias}, i.IsPartial{nullSortColumns}{projectionCols} FROM {IncludeLimitLabel(i)} i\n" +
                  $"INNER JOIN dbo.Resource r ON r.ResourceTypeId = i.T1 AND r.ResourceSurrogateId = i.Sid1{projectionJoinFilter}\n" +
                  $"WHERE NOT EXISTS (SELECT 1 FROM {MatchPage} m WHERE m.T1 = i.T1 AND m.Sid1 = i.Sid1)"
                : $"SELECT i.T1, i.Sid1, CAST(0 AS bit){isMatchAlias}, i.IsPartial{nullSortColumns} FROM {IncludeLimitLabel(i)} i\n" +
                  $"WHERE NOT EXISTS (SELECT 1 FROM {MatchPage} m WHERE m.T1 = i.T1 AND m.Sid1 = i.Sid1)");
        }

        return arms;
    }

    /// <summary>
    /// Builds the WHERE clauses selecting the page of match rows, shared by the no-includes shape and the
    /// includes shape's match-page CTE, and reports which clause is the keyset seek. The two shapes must agree
    /// on every clause or a paged search diverges from the same search with an _include. Include stages get
    /// none: their rows are reached by reference, not surrogate id or hash.
    /// </summary>
    private static List<string> BuildMatchWhereClauses(
        QueryPlan plan,
        List<EmittedSqlParameter> parameters,
        out int? seekClauseIndex)
    {
        var clauses = new List<string>();
        seekClauseIndex = null;

        if (plan.OuterPredicate is not null)
        {
            clauses.Add(EmitPredicate(plan.OuterPredicate, parameters, ResourceJoinQualifier));
        }

        if (plan.Sort is { Phase: SortPhase.MissingPrimary } missingPhaseSort)
        {
            clauses.Add(EmitMissingPrimaryFilter(missingPhaseSort));
        }

        if (plan.Page is { } page)
        {
            seekClauseIndex = clauses.Count;
            clauses.Add(EmitSeekPredicate(plan.Sort, page, parameters));
        }

        if (plan.SurrogateRange is { } range)
        {
            AppendSurrogateRangeClauses(clauses, range, parameters);
        }

        if (plan.SearchParameterHash is { } hash)
        {
            clauses.Add(EmitSearchParameterHashClause(hash, parameters));
        }

        return clauses;
    }

    /// <summary>
    /// Renders the reindex-eligibility filter for one search-parameter hash. The IS NULL disjunct qualifies
    /// resources that have never been indexed (no hash because they pre-date the feature).
    /// </summary>
    private static string EmitSearchParameterHashClause(SqlParameterRef hash, List<EmittedSqlParameter> parameters)
        => $"(r.SearchParamHash IS NULL OR r.SearchParamHash <> {EmitParam(hash, parameters)})";

    /// <summary>
    /// Appends the inclusive surrogate-id window to a shape's WHERE clause list. Extracted because omitting it
    /// in one shape is silent: an $export worker would read outside its partition and duplicate exported
    /// resources with no error. Include stages deliberately skip it (their rows are reached by reference).
    /// </summary>
    private static void AppendSurrogateRangeClauses(
        List<string> clauses,
        SurrogateIdRange range,
        List<EmittedSqlParameter> parameters)
    {
        clauses.Add($"m.Sid1 >= {EmitParam(range.Start, parameters)}");
        clauses.Add($"m.Sid1 <= {EmitParam(range.End, parameters)}");
    }

    /// <summary>
    /// Whether a shape must join dbo.Resource: true when any plan feature references an <c>r.</c> column.
    /// Centralised so a missing shape is a runtime bind error, not a test failure.
    /// </summary>
    /// <param name="plan">The query plan being emitted.</param>
    /// <param name="includesProjection">
    /// Whether the calling shape projects through this join. False for CountOnly and the includes match arm.
    /// </param>
    private static bool NeedsResourceJoin(QueryPlan plan, bool includesProjection)
        => plan.OuterPredicate is not null
            || plan.SearchParameterHash is not null
            || (includesProjection && plan.Projection is { Columns.Count: > 0 });

    /// <summary>
    /// Joins already-rendered WHERE fragments with " AND ", wrapping the one at <paramref name="seekClauseIndex"/>
    /// (if any) in its own "seek" section so the keyset-seek predicate stays traceable within the outer "where" section.
    /// </summary>
    private static void WriteAndJoinedClauses(SqlTextWriter writer, List<string> clauses, int? seekClauseIndex)
    {
        for (var i = 0; i < clauses.Count; i++)
        {
            if (i > 0)
            {
                writer.Append(" AND ");
            }

            if (i == seekClauseIndex)
            {
                using (writer.Section(Seek, SqlRangeKind.Seek))
                {
                    writer.Append(clauses[i]);
                }
            }
            else
            {
                writer.Append(clauses[i]);
            }
        }
    }

    /// <summary>Renders one CTE definition's inner SELECT by its node kind.</summary>
    private static string EmitCte(CteDefinition cte, List<EmittedSqlParameter> parameters, ResourceVisibility visibility) => cte switch
    {
        CteDefinition.ParamSource p => EmitParamSource(p, parameters, visibility),
        CteDefinition.Intersect x =>
            $"    SELECT {CteLabel(x.Left.Index)}.T1, {CteLabel(x.Left.Index)}.Sid1\n" +
            $"    FROM {CteLabel(x.Left.Index)}\n" +
            $"    INNER JOIN {CteLabel(x.Right.Index)} ON {CteLabel(x.Left.Index)}.T1 = {CteLabel(x.Right.Index)}.T1 AND {CteLabel(x.Left.Index)}.Sid1 = {CteLabel(x.Right.Index)}.Sid1",
        CteDefinition.Union u =>
            string.Join("\n    UNION\n", u.Parts.Select(r => $"    SELECT T1, Sid1 FROM {CteLabel(r.Index)}")),
        CteDefinition.ResourceSource rs => EmitResourceSource(rs, parameters, visibility),
        CteDefinition.Except ex =>
            $"    SELECT {CteLabel(ex.Left.Index)}.T1, {CteLabel(ex.Left.Index)}.Sid1\n" +
            $"    FROM {CteLabel(ex.Left.Index)}\n" +
            $"    WHERE NOT EXISTS (\n" +
            $"        SELECT 1 FROM {CteLabel(ex.Right.Index)}\n" +
            $"        WHERE {CteLabel(ex.Right.Index)}.T1 = {CteLabel(ex.Left.Index)}.T1 AND {CteLabel(ex.Right.Index)}.Sid1 = {CteLabel(ex.Left.Index)}.Sid1)",
        CteDefinition.ChainJoin cj => EmitChainJoin(cj, parameters, visibility),
        CteDefinition.CompartmentSource cs => EmitCompartmentSource(cs, parameters),
        CteDefinition.NotReferencedSource nr => EmitNotReferencedSource(nr, parameters, visibility),
        CteDefinition.MultiTypeResourceSource mts => EmitMultiTypeResourceSource(mts, parameters, visibility),
        CteDefinition.TableExistsPredicate tep => EmitTableExistsPredicate(tep, parameters, visibility),
        CteDefinition.VisibleSinceFilter vsf => EmitVisibleSinceFilter(vsf, parameters, visibility),
        CteDefinition.ReferencedTypeExpansion re => EmitReferencedTypeExpansion(re, visibility),
        _ => throw new NotSupportedException($"No Emit for {cte.GetType().Name}."),
    };

    /// <summary>
    /// The projected column list, prefixed with ", " and qualified with the terminal join alias, or empty.
    /// An empty column list is treated as a null projection (identity-only output, no dangling comma).
    /// </summary>
    private static string ProjectionColumns(ProjectionSpec? projection)
        => projection is null || projection.Columns.Count == 0
            ? string.Empty
            : ", " + string.Join(", ", projection.Columns.Select(c => $"r.[{c.Replace("]", "]]", StringComparison.Ordinal)}]"));

    /// <summary>
    /// The current-row filter for a dbo.Resource scan under a given visibility, prefixed with " AND " and the
    /// caller's column qualifier, or empty when neither axis is constrained. The leading space is load-bearing
    /// for inline callers; own-line callers trim it. Each axis is tri-state (<see cref="ResourceVisibility"/>):
    /// null emits no clause, false emits <c>= 0</c> (current/live), true emits <c>= 1</c> (superseded/deleted).
    /// </summary>
    private static string ResourceRowFilter(ResourceVisibility visibility, string qualifier)
    {
        var clauses = new List<string>(2);
        if (visibility.IsHistory is { } isHistory)
        {
            clauses.Add($"{qualifier}IsHistory = {(isHistory ? 1 : 0)}");
        }

        if (visibility.IsDeleted is { } isDeleted)
        {
            clauses.Add($"{qualifier}IsDeleted = {(isDeleted ? 1 : 0)}");
        }

        return clauses.Count == 0 ? string.Empty : " AND " + string.Join(" AND ", clauses);
    }

    /// <summary>
    /// The unqualified <c>IsHistory = 0</c> clause a search-param index table needs under a given
    /// visibility, or empty when it needs none. Emitted only for a latest-only search (IsHistory == false)
    /// against a table that has the column (e.g. dbo.TokenText, which retains superseded rows); null and
    /// true render empty so the dbo.Resource scan alone selects the version. Mirrors the legacy generator.
    /// </summary>
    private static string SearchParamTableHistoryClause(TableDescriptor table, ResourceVisibility visibility)
        => visibility.IsHistory == false && table.Columns.Any(c => c.Name == "IsHistory")
            ? "IsHistory = 0"
            : string.Empty;

    /// <summary>Renders a ParamSource: distinct (type, surrogate id) rows from one search-param table filtered by SearchParamId and its optional predicate.</summary>
    private static string EmitParamSource(CteDefinition.ParamSource p, List<EmittedSqlParameter> parameters, ResourceVisibility visibility)
    {
        var predicateClause = p.Predicate is null ? string.Empty : $" AND {EmitPredicate(p.Predicate, parameters)}";

        var historyClause = SearchParamTableHistoryClause(p.Table, visibility) is { Length: > 0 } clause
            ? $" AND {clause}"
            : string.Empty;

        // A null ResourceTypeId is a system-level (cross-type) search: emit no type filter. The requested
        // types are narrowed by the plan's MultiTypeResourceSource base set this CTE is intersected with.
        var typeFilter = p.ResourceTypeId is { } typeId ? $"ResourceTypeId = {typeId} AND " : string.Empty;

        return $"    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
               $"    FROM {p.Table.SchemaName}.{p.Table.TableName}\n" +
               $"    WHERE {typeFilter}SearchParamId = {p.SearchParamId}{historyClause}{predicateClause}";
    }

    /// <summary>Renders a chain as a join through dbo.ReferenceSearchParam and dbo.Resource, correlated to the inner match set, in the forward or reverse direction.</summary>
    private static string EmitChainJoin(CteDefinition.ChainJoin cj, List<EmittedSqlParameter> parameters, ResourceVisibility visibility)
    {
        // Hand-rolled interpolation, not Predicate.Equal via EmitPredicate: every id ChainJoin carries must
        // render as a literal, but EmitPredicate's Equal arm calls EmitParam and would bind a real @pN,
        // breaking the parameter-ordinal invariant PlanExplainer relies on.
        var outputFilter = string.Join(
            " OR ",
            cj.OutputResourceTypeIds.Select(id => $"{OutputTypeColumn(cj.Direction)} = {id}"));
        if (cj.OutputResourceTypeIds.Count > 1)
        {
            outputFilter = $"({outputFilter})";
        }

        var rowFilter = ResourceRowFilter(visibility, "r.");

        // Own-line placement, so the helper's leading space is replaced by this line's indentation.
        // An inline caller must not trim it; see ResourceRowFilter's remarks.
        var rowFilterLine = rowFilter.Length > 0 ? $"       {rowFilter.TrimStart()}\n" : string.Empty;

        return cj.Direction switch
        {
            ChainDirection.Forward =>
                $"    SELECT DISTINCT rsp.ResourceTypeId AS T1, rsp.ResourceSurrogateId AS Sid1\n" +
                $"    FROM dbo.ReferenceSearchParam rsp\n" +
                $"    INNER JOIN dbo.Resource r\n" +
                $"        ON r.ResourceTypeId = rsp.ReferenceResourceTypeId\n" +
                $"       AND r.ResourceId = rsp.ReferenceResourceId\n" +
                rowFilterLine +
                $"    INNER JOIN {CteLabel(cj.InnerMatch.Index)} m\n" +
                $"        ON m.T1 = r.ResourceTypeId AND m.Sid1 = r.ResourceSurrogateId\n" +
                $"    WHERE rsp.SearchParamId = {cj.ReferenceSearchParamId}\n" +
                $"      AND rsp.ReferenceResourceTypeId = {cj.InnerResourceTypeId}\n" +
                $"      AND {outputFilter}\n" +
                $"      AND rsp.BaseUri IS NULL",
            ChainDirection.Reverse =>
                $"    SELECT DISTINCT r.ResourceTypeId AS T1, r.ResourceSurrogateId AS Sid1\n" +
                $"    FROM dbo.ReferenceSearchParam rsp\n" +
                $"    INNER JOIN {CteLabel(cj.InnerMatch.Index)} m\n" +
                $"        ON m.T1 = rsp.ResourceTypeId AND m.Sid1 = rsp.ResourceSurrogateId\n" +
                $"    INNER JOIN dbo.Resource r\n" +
                $"        ON r.ResourceTypeId = rsp.ReferenceResourceTypeId\n" +
                $"       AND r.ResourceId = rsp.ReferenceResourceId\n" +
                rowFilterLine +
                $"    WHERE rsp.SearchParamId = {cj.ReferenceSearchParamId}\n" +
                $"      AND rsp.ResourceTypeId = {cj.InnerResourceTypeId}\n" +
                $"      AND {outputFilter}\n" +
                $"      AND rsp.BaseUri IS NULL",
            _ => throw new NotSupportedException($"Unknown ChainDirection '{cj.Direction}'."),
        };
    }

    /// <summary>The ReferenceSearchParam column holding a chain's output resource type, which side depends on direction.</summary>
    private static string OutputTypeColumn(ChainDirection direction) => direction switch
    {
        ChainDirection.Forward => "rsp.ResourceTypeId",
        ChainDirection.Reverse => "rsp.ReferenceResourceTypeId",
        _ => throw new NotSupportedException($"Unknown ChainDirection '{direction}'."),
    };

    /// <summary>Renders the joins to each sort key's search-param table (INNER for the primary key, LEFT for tie-breakers), filtered to the IsMin/IsMax row for the key's direction.</summary>
    private static string EmitSortJoins(SortSpec? sort)
    {
        if (sort is null)
        {
            return string.Empty;
        }

        var joins = new List<string>();
        for (var i = 0; i < sort.Keys.Count; i++)
        {
            if (i == 0 && sort.Phase == SortPhase.MissingPrimary)
            {
                continue; // primary key excluded from the join list in this phase -- see EmitMissingPrimaryFilter.
            }

            var key = sort.Keys[i];
            if (key.Kind is SortKeyKind.LastUpdated or SortKeyKind.ResourceType)
            {
                continue; // resource-column key already projected by the match set, no join needed.
            }

            if (key.Kind == SortKeyKind.ResourceId)
            {
                var ridJoinType = i == 0 ? "INNER" : "LEFT";
                joins.Add($"\n{ridJoinType} JOIN dbo.Resource rid{i} ON rid{i}.ResourceTypeId = m.T1 AND rid{i}.ResourceSurrogateId = m.Sid1");
                continue;
            }

            if (key.Kind == SortKeyKind.Aggregated)
            {
                // Key 0 in the Valued phase must gate on the key being present (INNER), like String/Date
                // below: an unconditional LEFT would leak missing-key rows across the phase boundary and let a
                // NULL AggValue reach the seek unwrapped. INNER is safe — MIN/MAX over zero rows yields no
                // output row, exactly INNER's semantics.
                var aggJoinType = i == 0 ? "INNER" : "LEFT";
                var aggFunc = key.Direction == SortOrder.Ascending ? "MIN" : "MAX";
                joins.Add(
                    $"\n{aggJoinType} JOIN (\n" +
                    $"    SELECT ResourceTypeId, ResourceSurrogateId, {aggFunc}({key.Column!.Name}) AS AggValue\n" +
                    $"    FROM {key.Table!.SchemaName}.{key.Table.TableName}\n" +
                    $"    WHERE SearchParamId = {key.SearchParamId}\n" +
                    $"    GROUP BY ResourceTypeId, ResourceSurrogateId\n" +
                    $") sk{i} ON sk{i}.ResourceTypeId = m.T1 AND sk{i}.ResourceSurrogateId = m.Sid1");
                continue;
            }

            var table = key.Kind == SortKeyKind.String ? "StringSearchParam" : "DateTimeSearchParam";
            var flag = key.Direction == SortOrder.Ascending ? "IsMin" : "IsMax";
            var joinType = i == 0 ? "INNER" : "LEFT";
            joins.Add(
                $"\n{joinType} JOIN dbo.{table} sk{i}\n" +
                $"    ON sk{i}.ResourceTypeId = m.T1 AND sk{i}.ResourceSurrogateId = m.Sid1\n" +
                $"   AND sk{i}.SearchParamId = {key.SearchParamId} AND sk{i}.{flag} = 1");
        }

        return string.Concat(joins);
    }

    /// <summary>Renders the NOT EXISTS filter that selects rows missing the primary sort key, used in the MissingPrimary phase in place of its join.</summary>
    private static string EmitMissingPrimaryFilter(SortSpec sort)
    {
        var key = sort.Keys[0];
        if (key.Kind == SortKeyKind.LastUpdated || key.SearchParamId is null)
        {
            throw new NotSupportedException(
                "A MissingPrimary sort phase requires a search-parameter primary key. LastUpdated, " +
                "ResourceType and ResourceId are non-nullable resource columns, so they are never missing and " +
                "have no second segment. Sort on a search parameter, or use SortPhase.Valued.");
        }

        if (key.Kind == SortKeyKind.Aggregated)
        {
            return $"NOT EXISTS (SELECT 1 FROM {key.Table!.SchemaName}.{key.Table.TableName} s WHERE s.ResourceTypeId = m.T1 AND s.ResourceSurrogateId = m.Sid1 AND s.SearchParamId = {key.SearchParamId})";
        }

        var table = key.Kind == SortKeyKind.String ? "StringSearchParam" : "DateTimeSearchParam";
        return $"NOT EXISTS (SELECT 1 FROM dbo.{table} s WHERE s.ResourceTypeId = m.T1 AND s.ResourceSurrogateId = m.Sid1 AND s.SearchParamId = {key.SearchParamId})";
    }

    /// <summary>The key indices that carry a value in the current phase: all keys when Valued, all but the primary when MissingPrimary.</summary>
    private static IReadOnlyList<int> ActiveKeyIndices(SortSpec? sort)
        => sort is null
            ? []
            : Enumerable.Range(sort.Phase == SortPhase.Valued ? 0 : 1, sort.ActiveKeyCount).ToList();

    /// <summary>
    /// Renders a sort key's value expression — the raw column, or ISNULL(column, sentinel) where the value
    /// can be missing. This is the single place a key's value expression is produced, so the ORDER BY,
    /// SELECT, and seek-predicate renderings for a key can never drift apart.
    /// </summary>
    private static string SortValueExpr(SortSpec sort, int index)
    {
        var key = sort.Keys[index];
        if (key.Kind == SortKeyKind.LastUpdated)
        {
            return "m.Sid1";
        }

        if (key.Kind == SortKeyKind.ResourceType)
        {
            return "m.T1";
        }

        if (key.Kind == SortKeyKind.ResourceId)
        {
            // Unwrapped even as a LEFT-joined secondary key: (ResourceTypeId, ResourceSurrogateId) is
            // dbo.Resource's clustered PK, so every (T1, Sid1) has a matching row and the LEFT never yields
            // NULL. Architectural, not FK-enforced — a match source of non-resource rows would break it.
            return $"rid{index}.ResourceId";
        }

        var isGuaranteedNonNull = index == 0 && sort.Phase == SortPhase.Valued;

        if (key.Kind == SortKeyKind.Aggregated)
        {
            var aggRaw = $"sk{index}.AggValue";
            if (isGuaranteedNonNull)
            {
                return aggRaw;
            }

            return $"ISNULL({aggRaw}, {SentinelFor(key.Column!.SqlType)})";
        }

        var column = key.Kind == SortKeyKind.String ? "Text" : "StartDateTime";
        var raw = $"sk{index}.{column}";

        if (isGuaranteedNonNull)
        {
            return raw;
        }

        var sentinel = key.Kind == SortKeyKind.String ? "N''" : "'0001-01-01T00:00:00.0000000'";
        return $"ISNULL({raw}, {sentinel})";
    }

    /// <summary>
    /// Maps a search-param column's DDL SQL type to the literal ISNULL substitutes for a missing aggregated
    /// sort value. Aggregated leaf types resolve to varchar (Token/Reference/Uri) or decimal (Number/Quantity);
    /// nvarchar is included for parity with String's N'' sentinel though no Aggregated column uses it.
    /// </summary>
    private static string SentinelFor(string sqlType) => sqlType switch
    {
        "varchar" => "''",
        "nvarchar" => "N''",
        "decimal" or "numeric" or "int" or "bigint" or "smallint" or "float" or "money" => "0",
        _ => throw new NotSupportedException(
            $"No ISNULL sentinel defined for aggregated sort SqlType '{sqlType}' -- add one to SentinelFor " +
            "after confirming the real DDL column type, matching the varchar/decimal families already handled."),
    };

    /// <summary>
    /// Renders the ORDER BY for the plain (no-includes) path: each active key's value and direction, then
    /// the (T1, Sid1) tiebreak. A custom sort drops the m.T1 tiebreak so every page orders by (sort keys…,
    /// Sid1) — see <see cref="HasCustomSortKey"/>.
    /// </summary>
    private static string EmitOrderBy(SortSpec? sort)
    {
        var activeIndices = ActiveKeyIndices(sort);
        var terms = activeIndices.Select(i =>
            $"{SortValueExpr(sort!, i)} {(sort!.Keys[i].Direction == SortOrder.Ascending ? "ASC" : "DESC")}").ToList();

        // SortValueExpr renders LastUpdated as "m.Sid1" and ResourceType as "m.T1"; if either is an active
        // key, appending it again as the trailing tiebreak would reference it twice (SQL Server Msg 145).
        // Safe to drop: the key already fully determines that column. The tiebreak is unconditionally ASC,
        // so dropping it also preserves a descending _type / _lastUpdated key an appended ASC could not express.
        var hasLastUpdatedKey = activeIndices.Any(i => sort!.Keys[i].Kind == SortKeyKind.LastUpdated);
        var hasResourceTypeKey = activeIndices.Any(i => sort!.Keys[i].Kind == SortKeyKind.ResourceType);

        // Drop the m.T1 tiebreak for a custom sort: its keyset order is (sort keys…, Sid1), type-free, and a
        // typeless page's Sid1-only seek must reproduce it exactly. Keeping T1 in ORDER BY while seeking on
        // Sid1 alone would drop tied rows at the page seam (the legacy (sortValue, T1, Sid1) bug). Sid1's
        // global uniqueness makes what remains a total order.
        if (!hasResourceTypeKey && !HasCustomSortKey(sort))
        {
            terms.Add("m.T1 ASC");
        }

        if (!hasLastUpdatedKey)
        {
            terms.Add("m.Sid1 ASC");
        }

        return string.Join(", ", terms);
    }

    /// <summary>
    /// Renders the final ORDER BY for the includes path: matches before includes (IsMatch DESC), then the
    /// projected SortValueN columns, then the (T1, Sid1) tiebreak. A custom sort drops the T1 tiebreak as in
    /// <see cref="EmitOrderBy"/>.
    /// </summary>
    private static string EmitOuterOrderByForIncludes(SortSpec? sort)
    {
        var activeIndices = ActiveKeyIndices(sort);
        var terms = activeIndices.Select((idx, ordinal) =>
            $"SortValue{ordinal} {(sort!.Keys[idx].Direction == SortOrder.Ascending ? "ASC" : "DESC")}")
            .Prepend("IsMatch DESC");
        if (!HasCustomSortKey(sort))
        {
            terms = terms.Append("T1 ASC");
        }

        return string.Join(", ", terms.Append("Sid1 ASC"));
    }

    /// <summary>Renders the ", SortValueN AS ..." select-list columns that project each active key's value for the outer ORDER BY to read.</summary>
    private static string EmitSortSelectColumns(SortSpec? sort)
    {
        var activeIndices = ActiveKeyIndices(sort);
        return activeIndices.Count == 0
            ? string.Empty
            : ", " + string.Join(", ", activeIndices.Select((idx, ordinal) => $"{SortValueExpr(sort!, idx)} AS SortValue{ordinal}"));
    }

    /// <summary>
    /// Renders the keyset-seek WHERE predicate that skips everything up to the page boundary: an OR of
    /// lexicographic branches over the active sort keys, then the surrogate-id tiebreak, in step with the
    /// ORDER BY. A typed <see cref="PageSpec"/> breaks the final tie on (T1, Sid1); a typeless one on Sid1 alone.
    /// The boundary's value count is checked against the phase by <see cref="RejectUnsupportedCombinations"/>.
    /// </summary>
    private static string EmitSeekPredicate(SortSpec? sort, PageSpec page, List<EmittedSqlParameter> parameters)
    {
        var activeIndices = ActiveKeyIndices(sort);

        var boundaryParams = page.Boundary.Select(b => EmitParam(b, parameters)).ToList();

        var branches = new List<string>();
        for (var level = 0; level < activeIndices.Count; level++)
        {
            var terms = new List<string>();
            for (var j = 0; j < level; j++)
            {
                terms.Add($"{SortValueExpr(sort!, activeIndices[j])} = {boundaryParams[j]}");
            }

            var key = sort!.Keys[activeIndices[level]];
            var op = key.Direction == SortOrder.Ascending ? ">" : "<";
            terms.Add($"{SortValueExpr(sort, activeIndices[level])} {op} {boundaryParams[level]}");
            branches.Add(terms.Count > 1 ? $"({string.Join(" AND ", terms)})" : terms[0]);
        }

        var allEqual = activeIndices.Select((idx, j) => $"{SortValueExpr(sort!, idx)} = {boundaryParams[j]}").ToList();
        var allEqualPrefix = allEqual.Count > 0 ? string.Join(" AND ", allEqual) + " AND " : string.Empty;

        // Bind the type parameter (when present) before the surrogate id so a typed page keeps its historical
        // @pN ordinals; a typeless page binds no type parameter and omits the type column from the seek.
        if (page.BoundaryResourceTypeId is { } boundaryType)
        {
            var typeParam = EmitParam(boundaryType, parameters);
            var sidParam = EmitParam(page.BoundarySurrogateId, parameters);
            branches.Add($"({allEqualPrefix}m.T1 = {typeParam} AND m.Sid1 > {sidParam})");
            branches.Add($"({allEqualPrefix}m.T1 > {typeParam})");
        }
        else
        {
            var sidParam = EmitParam(page.BoundarySurrogateId, parameters);
            branches.Add($"({allEqualPrefix}m.Sid1 > {sidParam})");
        }

        return branches.Count == 1
            ? branches[0]
            : $"({string.Join("\n       OR ", branches)})";
    }

    /// <summary>Renders a CompartmentSource: rows of dbo.ReferenceSearchParam for the membership SearchParamId, any of the member resource types, and the fixed compartment reference.</summary>
    private static string EmitCompartmentSource(CteDefinition.CompartmentSource cs, List<EmittedSqlParameter> parameters)
        => $"    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
           $"    FROM dbo.ReferenceSearchParam\n" +
           $"    WHERE SearchParamId = {cs.SearchParamId}\n" +
           $"      AND {EmitTypeInFilter("ResourceTypeId", cs.ResourceTypeIds)}\n" +
           $"      AND {EmitPredicate(cs.Predicate, parameters)}";

    /// <summary>
    /// Renders a NotReferencedSource: current, non-deleted rows of dbo.Resource for the target type that no
    /// dbo.ReferenceSearchParam row points at, optionally narrowed to one source type and/or reference path.
    /// Only the target type is bound; the inner ids are schema surrogates, inlined like every other schema id.
    /// </summary>
    private static string EmitNotReferencedSource(CteDefinition.NotReferencedSource nr, List<EmittedSqlParameter> parameters, ResourceVisibility visibility)
    {
        var innerFilters = string.Empty;
        if (nr.SourceResourceTypeId is { } sourceTypeId)
        {
            innerFilters += $"\n          AND rsp.ResourceTypeId = {sourceTypeId}";
        }

        if (nr.ReferenceSearchParamId is { } refParamId)
        {
            innerFilters += $"\n          AND rsp.SearchParamId = {refParamId}";
        }

        return $"    SELECT r.ResourceTypeId AS T1, r.ResourceSurrogateId AS Sid1\n" +
               $"    FROM dbo.Resource r\n" +
               $"    WHERE r.ResourceTypeId = {EmitParam(new SqlParameterRef(nr.TargetResourceTypeId), parameters)}{ResourceRowFilter(visibility, "r.")}\n" +
               $"      AND NOT EXISTS (\n" +
               $"        SELECT 1\n" +
               $"        FROM dbo.ReferenceSearchParam rsp\n" +
               $"        WHERE rsp.ReferenceResourceId = r.ResourceId\n" +
               $"          AND rsp.ReferenceResourceTypeId = r.ResourceTypeId{innerFilters})";
    }

    /// <summary>
    /// Renders a TableExistsPredicate: distinct (type, surrogate id) rows from one raw table, with an
    /// optional additional predicate and no SearchParamId/ResourceTypeId filter. Visibility reaches it via
    /// <see cref="SearchParamTableHistoryClause"/>, not <see cref="ResourceRowFilter"/>, whose IsDeleted
    /// clause references a column no search-param table has.
    /// </summary>
    private static string EmitTableExistsPredicate(CteDefinition.TableExistsPredicate tep, List<EmittedSqlParameter> parameters, ResourceVisibility visibility)
    {
        var clauses = new List<string>(2);
        if (SearchParamTableHistoryClause(tep.Table, visibility) is { Length: > 0 } historyClause)
        {
            clauses.Add(historyClause);
        }

        if (tep.Predicate is not null)
        {
            clauses.Add(EmitPredicate(tep.Predicate, parameters));
        }

        var whereClause = clauses.Count == 0 ? string.Empty : $"\n    WHERE {string.Join(" AND ", clauses)}";
        return
            $"    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            $"    FROM {tep.Table.SchemaName}.{tep.Table.TableName}{whereClause}";
    }

    /// <summary>Renders a VisibleSinceFilter: resources visible in a transaction on or after Since, joined through dbo.Resource and dbo.Transactions on VisibleDate.</summary>
    private static string EmitVisibleSinceFilter(CteDefinition.VisibleSinceFilter vsf, List<EmittedSqlParameter> parameters, ResourceVisibility visibility)
        => "    SELECT DISTINCT r.ResourceTypeId AS T1, r.ResourceSurrogateId AS Sid1\n" +
           "    FROM dbo.Resource r\n" +
           "    INNER JOIN dbo.Transactions t ON r.TransactionId = t.SurrogateIdRangeFirstValue\n" +
           $"    WHERE t.VisibleDate >= {EmitParam(vsf.Since, parameters)}{ResourceRowFilter(visibility, "r.")}";

    /// <summary>Renders a ReferencedTypeExpansion: the referenced resources reachable via any outbound internal reference from the seed set, restricted to the output resource types. Mirrors ChainJoin's reverse topology but with no SearchParamId/source-type filter (all reference parameters, any source type).</summary>
    private static string EmitReferencedTypeExpansion(CteDefinition.ReferencedTypeExpansion re, ResourceVisibility visibility)
    {
        var rowFilter = ResourceRowFilter(visibility, "r.");

        // Own-line placement, so the helper's leading space is replaced by this line's indentation.
        // An inline caller must not trim it; see ResourceRowFilter's remarks.
        var rowFilterLine = rowFilter.Length > 0 ? $"       {rowFilter.TrimStart()}\n" : string.Empty;

        return $"    SELECT DISTINCT r.ResourceTypeId AS T1, r.ResourceSurrogateId AS Sid1\n" +
               $"    FROM dbo.ReferenceSearchParam rsp\n" +
               $"    INNER JOIN {CteLabel(re.Seed.Index)} m\n" +
               $"        ON m.T1 = rsp.ResourceTypeId AND m.Sid1 = rsp.ResourceSurrogateId\n" +
               $"    INNER JOIN dbo.Resource r\n" +
               $"        ON r.ResourceTypeId = rsp.ReferenceResourceTypeId\n" +
               $"       AND r.ResourceId = rsp.ReferenceResourceId\n" +
               rowFilterLine +
               $"    WHERE {EmitTypeInFilter("rsp.ReferenceResourceTypeId", re.OutputResourceTypeIds)}\n" +
               $"      AND rsp.BaseUri IS NULL";
    }

    /// <summary>
    /// Renders a ResourceSource: current, non-deleted rows of dbo.Resource for one type, with an optional
    /// nested-scope predicate. Binds its type id as a parameter (unlike the sibling emitters, which use
    /// literals); left as-is because converging on literals would shift downstream parameter ordinals.
    /// </summary>
    private static string EmitResourceSource(CteDefinition.ResourceSource rs, List<EmittedSqlParameter> parameters, ResourceVisibility visibility)
    {
        var predicateClause = rs.Predicate is null ? string.Empty : $" AND {EmitPredicate(rs.Predicate, parameters)}";
        return $"    SELECT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
               $"    FROM dbo.Resource\n" +
               $"    WHERE ResourceTypeId = {EmitParam(new SqlParameterRef(rs.ResourceTypeId), parameters)}{ResourceRowFilter(visibility, string.Empty)}{predicateClause}";
    }

    /// <summary>Renders a MultiTypeResourceSource: a dbo.Resource scan across a set of types, or every type when the set is empty.</summary>
    private static string EmitMultiTypeResourceSource(
        CteDefinition.MultiTypeResourceSource mts,
        List<EmittedSqlParameter> parameters,
        ResourceVisibility visibility)
    {
        // Type ids are literals, not bound parameters (matching ParamSource and ChainJoin). An empty list
        // means "every type"; emit no type filter. Unresolvable sentinel ids (-1) are kept intentionally:
        // they match no row, whereas dropping them could collapse an all-unknown list to a full-table scan.
        var clauses = new List<string>(4);
        if (mts.ResourceTypeIds.Count > 0)
        {
            clauses.Add($"ResourceTypeId IN ({string.Join(", ", mts.ResourceTypeIds)})");
        }

        if (visibility.IsHistory is { } isHistory)
        {
            clauses.Add($"IsHistory = {(isHistory ? 1 : 0)}");
        }

        if (visibility.IsDeleted is { } isDeleted)
        {
            clauses.Add($"IsDeleted = {(isDeleted ? 1 : 0)}");
        }

        if (mts.Predicate is not null)
        {
            clauses.Add(EmitPredicate(mts.Predicate, parameters));
        }

        var whereClause = clauses.Count == 0 ? string.Empty : $"    WHERE {string.Join(" AND ", clauses)}";

        return $"    SELECT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
               $"    FROM dbo.Resource\n" +
               whereClause;
    }

    /// <summary>
    /// Renders one include stage: the ReferenceSearchParam/Resource join for its direction, filtered by
    /// reference param and type ids, seeded from the match page and/or earlier stages via EXISTS. The ordinary
    /// path selects <c>TOP (Limit + 1)</c> ordered by (T1, Sid1); the IncludesOnly path drops both. The body is
    /// never filtered by the resume boundary — it seeds downstream <c>:iterate</c> stages (<see cref="EmitSeedExists"/>).
    /// </summary>
    private static string EmitIncludeStage(
        IncludeStage stage,
        ResourceVisibility visibility,
        bool includesOnly)
    {
        var (selectColumns, seedTypeColumn, outputTypeColumn, outputSurrogateColumn, seedCorrelationAlias) = stage.Direction switch
        {
            IncludeDirection.Forward => ("r.ResourceTypeId AS T1, r.ResourceSurrogateId AS Sid1", "rsp.ResourceTypeId", "r.ResourceTypeId", "r.ResourceSurrogateId", "rsp"),
            IncludeDirection.Reverse => ("rsp.ResourceTypeId AS T1, rsp.ResourceSurrogateId AS Sid1", "r.ResourceTypeId", "rsp.ResourceTypeId", "rsp.ResourceSurrogateId", "r"),
            _ => throw new NotSupportedException($"Unknown IncludeDirection '{stage.Direction}'."),
        };

        var whereClauses = new List<string>();
        if (stage.ReferenceSearchParamId is { } paramId)
        {
            whereClauses.Add($"rsp.SearchParamId = {paramId}");
        }

        if (stage.SeedTypeIds is { Count: > 0 } seedTypeIds)
        {
            whereClauses.Add(EmitTypeInFilter(seedTypeColumn, seedTypeIds));
        }

        if (stage.OutputTypeIds is { Count: > 0 } outputTypeIds)
        {
            whereClauses.Add(EmitTypeInFilter(outputTypeColumn, outputTypeIds));
        }

        whereClauses.Add("rsp.BaseUri IS NULL");
        whereClauses.Add(EmitSeedExists(stage, seedCorrelationAlias, includesOnly));

        if (stage.Constraints is { Count: > 0 } constraints)
        {
            foreach (var constraint in constraints)
            {
                whereClauses.Add(EmitConstraintGuard(constraint, outputTypeColumn, outputSurrogateColumn));
            }
        }

        var rowFilter = ResourceRowFilter(visibility, "r.");

        // Own-line placement, so the helper's leading space is replaced by this line's indentation.
        // An inline caller must not trim it; see ResourceRowFilter's remarks.
        var rowFilterLine = rowFilter.Length > 0 ? $"       {rowFilter.TrimStart()}\n" : string.Empty;

        // Drop the per-stage TOP and its ORDER BY for the IncludesOnly page: the budget is applied once
        // globally, and a CTE ORDER BY without TOP is illegal T-SQL anyway.
        var topClause = includesOnly ? string.Empty : $"TOP ({stage.Limit + 1}) ";
        var orderByClause = includesOnly ? string.Empty : "\n    ORDER BY T1 ASC, Sid1 ASC";

        return $"    SELECT DISTINCT {topClause}{selectColumns}\n" +
               $"    FROM dbo.ReferenceSearchParam rsp\n" +
               $"    INNER JOIN dbo.Resource r\n" +
               $"        ON r.ResourceTypeId = rsp.ReferenceResourceTypeId\n" +
               $"       AND r.ResourceId = rsp.ReferenceResourceId\n" +
               rowFilterLine +
               $"    WHERE {string.Join("\n      AND ", whereClauses)}" +
               orderByClause;
    }

    /// <summary>Renders a "column = a OR column = b ..." type-id filter, parenthesized when there is more than one id.</summary>
    private static string EmitTypeInFilter(string column, IReadOnlyList<short> typeIds)
    {
        var filter = string.Join(" OR ", typeIds.Select(id => $"{column} = {id}"));
        return typeIds.Count > 1 ? $"({filter})" : filter;
    }

    /// <summary>Renders the EXISTS clause correlating an include row back to its seeds — the match page and/or earlier stages.</summary>
    /// <param name="includesOnly">
    /// Which label an earlier stage is read through: the ordinary path seeds from the limit companion
    /// (<see cref="IncludeLimitLabel"/>); an IncludesOnly page seeds from the stage body (<see cref="IncludeLabel"/>),
    /// unfiltered by the resume boundary so an <c>:iterate</c> stage on page 2 still sees page-1 targets.
    /// </param>
    private static string EmitSeedExists(IncludeStage stage, string correlationAlias, bool includesOnly)
    {
        var branches = new List<string>();
        if (stage.SeedFromMatch)
        {
            branches.Add($"SELECT 1 FROM {MatchPage} m WHERE m.T1 = {correlationAlias}.ResourceTypeId AND m.Sid1 = {correlationAlias}.ResourceSurrogateId");
        }

        foreach (var seedStageIndex in stage.SeedStages)
        {
            var seedLabel = includesOnly ? IncludeLabel(seedStageIndex) : IncludeLimitLabel(seedStageIndex);
            branches.Add($"SELECT 1 FROM {seedLabel} m WHERE m.T1 = {correlationAlias}.ResourceTypeId AND m.Sid1 = {correlationAlias}.ResourceSurrogateId");
        }

        return $"EXISTS (\n        {string.Join("\n        UNION ALL\n        ", branches)}\n    )";
    }

    /// <summary>
    /// Renders one access-constraint guard on an include stage: a row of the constrained type must appear in
    /// the constraint CTE, while a row of any other type passes untouched. The leading "type &lt;&gt; id OR"
    /// keeps a multi-type or wildcard stage from dropping rows the constraint does not govern.
    /// </summary>
    private static string EmitConstraintGuard(IncludeConstraint constraint, string outputTypeColumn, string outputSurrogateColumn)
        => $"({outputTypeColumn} <> {constraint.ConstraintTypeId} OR EXISTS (" +
           $"SELECT 1 FROM {CteLabel(constraint.ConstraintCteIndex)} ac " +
           $"WHERE ac.T1 = {outputTypeColumn} AND ac.Sid1 = {outputSurrogateColumn}))";

    /// <summary>Renders a predicate tree to a WHERE fragment, fully parenthesizing And/Or so precedence never depends on context.</summary>
    /// <param name="qualifier">
    /// Alias prefix (including the dot) before every column, or empty for a single-table CTE body. The outer
    /// query must qualify with <c>r.</c> because the resource join and a ResourceId sort join (rid0) are both
    /// dbo.Resource — an unqualified column is ambiguous (Msg 209).
    /// </param>
    private static string EmitPredicate(Predicate predicate, List<EmittedSqlParameter> parameters, string qualifier = "") => predicate switch
    {
        Predicate.Equal e => $"{qualifier}{e.Column.Column} = {EmitParam(e.Value, parameters)}{EmitCollation(e.Collation)}",
        Predicate.Like l => $"{qualifier}{l.Column.Column}{EmitCollation(l.Collation)} LIKE {EmitParam(EscapeLike(l), parameters)} ESCAPE '\\'",
        Predicate.And a => $"({EmitPredicate(a.Left, parameters, qualifier)} AND {EmitPredicate(a.Right, parameters, qualifier)})",
        Predicate.LessThan lt => $"{qualifier}{lt.Column.Column} < {EmitParam(lt.Value, parameters)}",
        Predicate.LessThanOrEqual le => $"{qualifier}{le.Column.Column} <= {EmitParam(le.Value, parameters)}",
        Predicate.GreaterThan gt => $"{qualifier}{gt.Column.Column} > {EmitParam(gt.Value, parameters)}",
        Predicate.GreaterThanOrEqual ge => $"{qualifier}{ge.Column.Column} >= {EmitParam(ge.Value, parameters)}",
        Predicate.Or or => $"({EmitPredicate(or.Left, parameters, qualifier)} OR {EmitPredicate(or.Right, parameters, qualifier)})",
        Predicate.Not not => $"NOT ({EmitPredicate(not.Operand, parameters, qualifier)})",
        Predicate.IsNull isNull => $"{qualifier}{isNull.Column.Column} IS NULL",
        Predicate.False => PlanExplainer.UnsatisfiableRendering,
        Predicate.PrefixOfParameter pop => $"LEFT({EmitParam(pop.Value, parameters)}, LEN({qualifier}{pop.Column.Column})){EmitCollation(pop.Collation)} = {qualifier}{pop.Column.Column}",
        _ => throw new NotSupportedException($"No Emit for {predicate.GetType().Name}."),
    };

    /// <summary>
    /// The alias the outer query's <c>dbo.Resource</c> join uses. <see cref="NeedsResourceJoin"/> guarantees
    /// the join exists whenever an outer predicate does, so qualifying with it is always valid and unambiguous.
    /// </summary>
    private const string ResourceJoinQualifier = "r.";

    /// <summary>Escapes the LIKE metacharacters in a value and wraps it in the % / _ pattern for its match kind, returning a parameter ref for binding.</summary>
    private static SqlParameterRef EscapeLike(Predicate.Like like)
    {
        var raw = (string)like.Value.Value;
        var escaped = raw.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal)
            .Replace("[", "\\[", StringComparison.Ordinal);
        var pattern = like.Match switch
        {
            LikeMatch.Contains => $"%{escaped}%",
            LikeMatch.StartsWith => $"{escaped}%",
            LikeMatch.EndsWith => $"%{escaped}",
            _ => throw new NotSupportedException($"No LIKE pattern for {like.Match}."),
        };
        return new SqlParameterRef(pattern);
    }

    /// <summary>Binds a value as the next @pN parameter and returns its name — the single point where user values enter the SQL.</summary>
    private static string EmitParam(SqlParameterRef value, List<EmittedSqlParameter> parameters)
    {
        var name = $"@p{parameters.Count}";
        parameters.Add(new EmittedSqlParameter(name, value.Value));
        return name;
    }

    /// <summary>Renders a " COLLATE ..." suffix, or empty when the predicate has no explicit collation.</summary>
    private static string EmitCollation(string? collation) => collation is null ? string.Empty : $" COLLATE {collation}";
}
