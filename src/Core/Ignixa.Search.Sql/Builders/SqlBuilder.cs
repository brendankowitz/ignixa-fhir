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
public static class SqlBuilder
{
    /// <summary>
    /// Renders a plan to SQL and its bound parameters by selecting one of three terminal shapes and
    /// delegating to its emitter: a COUNT_BIG SELECT when CountOnly, a plain (T1, Sid1) select (with
    /// optional sort/paging) when there are no includes, or a match-page CTE plus per-stage include CTEs
    /// unioned into a (T1, Sid1, IsMatch, IsPartial) result.
    /// </summary>
    /// <remarks>
    /// Shape selection and delegation only. Each shape owns its own SELECT list, joins, WHERE assembly and
    /// ORDER BY, so a feature that applies to one shape cannot be half-applied to another: the earlier
    /// single-body form let IncludesOnly drop the match arm while the outer ORDER BY still referenced its
    /// projected sort columns, which is valid grammar and a bind failure at execution. A new optional
    /// feature belongs in the shapes it applies to, named at each, rather than as another inline block here.
    /// </remarks>
    public static EmittedSql Run(QueryPlan plan, EmitOptions? options = null)
    {
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

    /// <summary>Rejects the plan shapes that have no coherent SQL rendering, before any text is produced.</summary>
    private static void RejectUnsupportedCombinations(QueryPlan plan)
    {
        if (plan.IncludesOnly && plan.CountOnly)
        {
            throw new NotSupportedException(
                "IncludesOnly and CountOnly cannot both be true: IncludesOnly requests include-stage rows " +
                "while CountOnly requests a count of match rows; the combination is self-contradictory.");
        }

        if (plan.IncludesOnly && plan.Includes is not { Count: > 0 })
        {
            throw new NotSupportedException(
                "IncludesOnly was requested with no include stages, which can only ever return an empty " +
                "result. This is a caller error rather than a query that legitimately matches nothing.");
        }

        if (plan.IncludesOnly && plan.Sort is not null)
        {
            // Dropping the match arm leaves the include arm's projected sort columns unaliased while the
            // outer ORDER BY still references SortValueN, so the emitted SQL would bind to a nonexistent
            // column (SQL Server error 207) -- and the grammar tests cannot catch it because an unbound
            // identifier is grammatically valid. A sort orders match rows; an includes-only page returns
            // none and pages its include rows by (T1, Sid1), so the sort key is meaningless here. Refuse
            // it rather than silently emit invalid SQL.
            throw new NotSupportedException(
                "IncludesOnly was requested together with a sort, but an includes-only page returns no match " +
                "rows for the sort key to order and its include rows are paged by (T1, Sid1) rather than the " +
                "sort key. The combination is meaningless, so it is reported rather than silently emitted.");
        }

        if (plan.IncludesOnly && plan.Includes is { Count: > 0 } includeStages
            && includeStages.Any(s => s.Limit != includeStages[0].Limit))
        {
            // The global page applies one TOP over the union of every stage (EmitGlobalIncludesPage takes
            // the budget from includes[0].Limit), because the page budget is a property of the whole
            // ordered stream, not of any single stage. Differing per-stage limits therefore have no single
            // coherent meaning here: the emitter would silently page on whichever limit happened to be
            // first and return a wrong-sized page with no error -- exactly the silent wrong answer the other
            // guards exist to prevent. Refuse it rather than pick a budget arbitrarily.
            throw new NotSupportedException(
                "IncludesOnly was requested with include stages that do not all share one Limit. The page " +
                "budget applies once across the union of every stage, so a per-stage limit has no coherent " +
                "meaning here; the stages must agree on a single Limit. The mismatch is reported rather than " +
                "silently paged on whichever limit is first.");
        }

        // Lower.Run rejects this at the lowering stage, but QueryPlan is a public construction surface, so
        // the builder must guard independently. The resume predicate only pages a stream of include rows,
        // which exists only when IncludesOnly drops the match arm; on an ordinary search the emitter would
        // never apply it and the cursor's caller would silently get a full first page instead of a resumed
        // one. Grammatically valid either way, so the grammar tests cannot catch it.
        if (plan.IncludeCursor is not null && !plan.IncludesOnly)
        {
            throw new NotSupportedException(
                "IncludeCursor was supplied without IncludesOnly. The resume cursor pages the union of " +
                "include stages as one ordered stream, which exists only on an includes-only page; on an " +
                "ordinary search there is no such stream for it to resume.");
        }

        // Lower.Run rejects these two at the lowering stage, but QueryPlan is a public construction surface
        // -- a caller building one directly bypasses that guard entirely. TOP alongside OFFSET/FETCH is
        // SQL Server Msg 10741, and a keyset seek alongside OFFSET/FETCH pages the same rows twice by two
        // different mechanisms. Both are grammatically valid, so the grammar tests cannot catch either.
        if (plan.OffsetPage is not null && (plan.Top is not null || plan.Page is not null))
        {
            throw new NotSupportedException(
                "OffsetPage cannot be combined with Top or a keyset Page: TOP alongside OFFSET/FETCH is " +
                "rejected by SQL Server, and a keyset seek alongside OFFSET/FETCH applies two independent " +
                "paging mechanisms to one query.");
        }

        // A typeless seek breaks its final tie on Sid1 alone and never mentions the type column, so it is
        // sound only against a type-free ORDER BY -- (sort keys…, Sid1). EmitOrderBy produces that shape only
        // for a custom (search-parameter) sort; every other sort keeps m.T1 as a tiebreak (a plain search
        // orders by (T1, Sid1); a _type sort orders by the type itself), so a typeless seek would disagree
        // with the ORDER BY and keyset paging would silently drop rows across a page boundary within a tie.
        // Refuse the mismatch here: it is grammatically valid SQL, so the grammar tests cannot catch it, and
        // this is the construction surface a caller reaches when building a QueryPlan directly.
        if (IsTypelessPage(plan) && !HasCustomSortKey(plan.Sort))
        {
            throw new NotSupportedException(
                "A typeless keyset Page (BoundaryResourceTypeId is null) requires a custom (search-parameter) " +
                "_sort such as name or birthdate. The plan's sort is " +
                (plan.Sort is null ? "absent" : "a resource-column sort (_lastUpdated / _type / _id)") +
                ", whose keyset order includes the resource type, so a type-free seek would disagree with the " +
                "ORDER BY and paging would be unsound. Use a typed Page here, or a custom sort for a typeless Page.");
        }
    }

    /// <summary>
    /// A keyset page whose boundary carries no resource-type component. Its seek compares only the sort
    /// key(s) and the surrogate id; the emitter drops the m.T1 tiebreak from the matching ORDER BY so the
    /// two stay in step. Sound because ResourceSurrogateId is globally unique, so Sid1 is a total order on
    /// its own.
    /// </summary>
    private static bool IsTypelessPage(QueryPlan plan) => plan.Page is { BoundaryResourceTypeId: null };

    /// <summary>
    /// A search-parameter-backed sort key (a String/Date sort such as name or birthdate, or an Aggregated
    /// Token/Number/Quantity/Reference/Uri sort) as opposed to the resource-column keys (_lastUpdated / _type
    /// / _id). These are the keys whose legacy continuation token is [sortValue, resourceSurrogateId] with no
    /// type slot, so their keyset order is (sortValue…, Sid1) -- type-free.
    /// </summary>
    private static bool IsCustomSortKey(SortKeyKind kind)
        => kind is SortKeyKind.String or SortKeyKind.Date or SortKeyKind.Aggregated;

    /// <summary>
    /// True when the sort has any custom (search-parameter) key, so the ORDER BY drops the m.T1 tiebreak and
    /// orders by (sort keys…, Sid1). Decided by the sort's own keys -- never by whether this request carries a
    /// page boundary -- so page 1 (which has no Page) and every later page of one keyset walk share a single
    /// ordering; keying it off the boundary would let page 1 keep m.T1 while a later typeless page dropped it,
    /// reintroducing the very row-skipping this avoids. All keys are considered, not just the phase's active
    /// ones, so the missing-value segment of a custom sort (whose active keys may be empty) stays type-free and
    /// remains pageable in a multi-type search where no single type can substitute into the seek. Sound because
    /// ResourceSurrogateId is globally unique, making (sort keys…, Sid1) a total order without the type column.
    /// </summary>
    private static bool HasCustomSortKey(SortSpec? sort)
        => sort is not null && sort.Keys.Any(k => IsCustomSortKey(k.Kind));

    /// <summary>Renders every <see cref="CteDefinition"/> as a named "cteN AS (...)" block, in plan order.</summary>
    /// <remarks>
    /// Runs before any shape emits, so the CTE graph's bound values always take the leading @pN ordinals
    /// whichever shape follows. PlanExplainer reads parameters back by ordinal, so a shape that bound a
    /// value before this ran would silently misattribute every CTE parameter.
    /// </remarks>
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
    /// Emits the CountOnly shape: COUNT_BIG(DISTINCT m.Sid1) over the match CTE.
    /// </summary>
    /// <remarks>
    /// Deliberately ignores Sort and Page. A count is of the whole result set, so the keyset-seek predicate
    /// would undercount by exactly the rows already paged past, and a sort has nothing to order. This is the
    /// one shape whose WHERE assembly legitimately differs from the match shapes' shared one.
    /// <para>
    /// <see cref="QueryPlan.CountPhaseScoped"/> is the one count-side exception: it joins the current sort
    /// phase's own key (mirroring <see cref="EmitSortJoins"/>) and, in the MissingPrimary phase, applies
    /// the same NOT EXISTS filter the match shapes use, so a two-phase executor's count matches the rows
    /// that phase actually returns rather than the whole match set.
    /// </para>
    /// </remarks>
    private static void EmitCountOnlyShape(
        QueryPlan plan,
        SqlTextWriter writer,
        List<string> cteBlocks,
        List<EmittedSqlParameter> parameters)
    {
        WriteCteHeader(writer, cteBlocks);
        writer.Append("\n");

        // CountPhaseScoped joins the current sort phase's own key -- the same join a match shape would
        // use for that phase -- so a two-phase (Valued/MissingPrimary) executor's count matches the rows
        // that phase actually returns, rather than the whole match set. See the class remarks above.
        var countSortJoins = plan.CountPhaseScoped ? EmitSortJoins(plan.Sort) : string.Empty;
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

        if (plan.CountPhaseScoped && plan.Sort is { Phase: SortPhase.MissingPrimary } countPhaseSort)
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

        // Emit the resource join when any of outer predicate, projection, or hash filter needs it —
        // all three share the same single join; emitting it conditionally per-contributor would
        // produce duplicate JOINs (a SQL error) or miss it entirely (a silent no-op).
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
    /// Emits the includes shape: the match-page CTE, the include-stage CTEs, and the assembly that stitches
    /// them into one (T1, Sid1, IsMatch, IsPartial) result. Two assemblies exist: the ordinary path unions
    /// each stage's own limit companion and orders matches-first; the IncludesOnly path (a <c>$includes</c>
    /// page) instead applies the row budget once, globally, over the union of the unlimited stage bodies and
    /// orders by (T1, Sid1) so it can resume from a cursor. They are separated because the budget lives in a
    /// different place — per stage versus once across the union — not merely because the ORDER BY differs.
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

        // Bind the resume cursor once, after the match-page CTE and before the stage loop, so every stage
        // resumes from the same @t/@sid — the union pages as one ordered stream, so a per-stage cursor
        // would let one stage overtake another across pages. Include CTEs otherwise bind no parameters, so
        // these are the first stage-level @pN and stay after the CTE-graph and match-page ordinals, keeping
        // the leading-ordinal invariant EmitCteBlocks documents intact.
        (string Type, string Surrogate)? resumeParams = plan is { IncludesOnly: true, IncludeCursor: { } cursor }
            ? (EmitParam(new SqlParameterRef(cursor.TypeId), parameters), EmitParam(new SqlParameterRef(cursor.SurrogateId), parameters))
            : null;

        for (var i = 0; i < includes.Count; i++)
        {
            WriteIncludeStageCtes(writer, includes[i], i, visibility, plan.IncludesOnly, resumeParams);
        }

        writer.Append("\n");

        if (plan.IncludesOnly)
        {
            EmitGlobalIncludesPage(plan, includes, writer, visibility);
            return;
        }

        // The final UNION ALL stitches the match page to every include stage, so like the other
        // structural sections it belongs to no single plan row. Sectioned anyway: until it was, this
        // stretch carried no range at all and could not be addressed even as structure.
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
    /// Emits the outer global-page SELECT for an IncludesOnly page: a single
    /// <c>SELECT DISTINCT TOP (@limit + 1) T1, Sid1, IsMatch, &lt;IsPartial&gt;</c> over the UNION ALL of every
    /// include stage body, ordered by (T1, Sid1). This mirrors the FHIR Server legacy $includes page, whose
    /// row budget is applied once across the union rather than once per stage, so it can resume from a
    /// cursor and page the whole include set as one ordered stream.
    /// </summary>
    /// <remarks>
    /// Every stage shares the query's single include limit, so the global budget is taken from
    /// <c>includes[0].Limit</c>; the outer TOP is that budget plus one row, the sentinel that tells a full
    /// page from a truncated one. IsPartial is derived from <c>COUNT_BIG(*) OVER()</c> against the whole
    /// union and cast to <c>bit</c> for the same reason <see cref="EmitIncludeLimitStage"/> casts it: the
    /// column is unioned with a bit-typed IsMatch, and leaving it <c>int</c> would silently change the
    /// result column's type. The order is <c>T1 ASC, Sid1 ASC</c> (not matches-first): an includes-only
    /// page has no match rows, and this order is what the per-stage resume predicate keys on.
    /// </remarks>
    private static void EmitGlobalIncludesPage(
        QueryPlan plan,
        IReadOnlyList<IncludeStage> includes,
        SqlTextWriter writer,
        ResourceVisibility visibility)
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
                writer.Append(string.Join("\nUNION ALL\n", BuildGlobalIncludesPageArms(plan, includes, visibility)));
            }

            writer.Append($"\n) {IncludeUnionAlias}\nORDER BY ");
            using (writer.Section(OrderBy, SqlRangeKind.OrderBy))
            {
                writer.Append("T1 ASC, Sid1 ASC");
            }
        }
    }

    /// <summary>
    /// Builds the inner arms of the global includes page: one arm per include stage, each selecting its
    /// unlimited body, tagging it <c>CAST(0 AS bit)</c> as a non-match, and excluding rows already on the
    /// match page. The outer SELECT applies the single global TOP and IsPartial over their union.
    /// </summary>
    private static List<string> BuildGlobalIncludesPageArms(QueryPlan plan, IReadOnlyList<IncludeStage> includes, ResourceVisibility visibility)
    {
        var projectionCols = ProjectionColumns(plan.Projection);
        var hasActiveProjection = projectionCols.Length > 0;
        var projectionJoinFilter = hasActiveProjection ? ResourceRowFilter(visibility, "r.") : string.Empty;

        var arms = new List<string>();
        for (var i = 0; i < includes.Count; i++)
        {
            // Only the first arm names IsMatch: SQL Server takes a UNION ALL's column names from its first
            // SELECT and the outer query reads them by ordinal, so aliasing every arm is redundant and
            // aliasing none loses the name. Keyed on arms.Count so a future arm inserted ahead of this loop
            // cannot silently move the alias off the first position.
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
    /// bracket-quoted and unqualified, because SQL Server derives a derived table's column names from the
    /// inner <c>r.[Col]</c> references (dropping the qualifier). Empty for a null or empty projection.
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
        var sortColumns = EmitSortSelectColumns(plan.Sort);

        // Emit the resource join inside cteMatchPage when any plan feature referencing an r. column
        // requires it. Projection is handled in the UNION ALL assembly rather than here, so
        // includesProjection is false.
        var resourceJoin = NeedsResourceJoin(plan, includesProjection: false)
            ? "\n    INNER JOIN dbo.Resource r ON r.ResourceTypeId = m.T1 AND r.ResourceSurrogateId = m.Sid1"
            : string.Empty;

        // A CTE's own ORDER BY is only legal T-SQL alongside TOP or OFFSET/FETCH (SQL Server Msg 1033) --
        // when neither is present, cteMatchPage must have no ORDER BY of its own either. The outer final
        // UNION ALL's ORDER BY (EmitOuterOrderByForIncludes, below) is a plain top-level SELECT, always
        // legal regardless of TOP or OFFSET/FETCH, and is unaffected by this.
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
        bool includesOnly,
        (string Type, string Surrogate)? resumeParams)
    {
        writer.Append(",\n");
        using (writer.Section(IncludeLabel(index), SqlRangeKind.Include))
        {
            writer.Append($"{IncludeLabel(index)} AS (\n{EmitIncludeStage(stage, visibility, includesOnly, resumeParams)}\n)");
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
    /// Renders an include stage's limit-applying companion: the first Limit rows, plus an IsPartial flag set
    /// from the window count so the caller can tell a truncated stage from an exactly-full one.
    /// </summary>
    /// <remarks>
    /// The flag is cast to <c>bit</c> rather than left as the <c>int</c> the CASE naturally yields, because this
    /// column is unioned with the match arm's <c>CAST(0 AS bit) AS IsPartial</c>. T-SQL type precedence promotes
    /// a bit/int union to <c>int</c>, so leaving it untyped silently changed the result column's type based on
    /// whether the plan happened to carry includes — and a caller reading the documented
    /// (T1, Sid1, IsMatch, IsPartial) contract as a bit threw InvalidCastException on include rows only.
    /// </remarks>
    private static string EmitIncludeLimitStage(IncludeStage stage, int index)
        => $"    SELECT TOP ({stage.Limit}) T1, Sid1,\n" +
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
            // SQL Server takes a UNION ALL's column names from its first SELECT, and callers read those
            // columns by ordinal. When IncludesOnly omits the match arm, the first arm appended to
            // arms must carry the explicit " AS IsMatch" alias to preserve the four-column shape.
            // Key off arms.Count == 0 (first arm overall), not i == 0 (first include stage),
            // so any future arm inserted before this loop cannot silently break the ordinal contract.
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
    /// Builds the WHERE clauses that select the page of match rows, shared by the no-includes shape and the
    /// includes shape's match-page CTE, and reports which clause is the keyset seek so the caller can section it.
    /// </summary>
    /// <remarks>
    /// Every clause here constrains match rows only, and the two shapes must agree on all of them — the
    /// no-includes SELECT and cteMatchPage produce the same row set by construction, so a filter added to one
    /// and not the other is a silent divergence between a paged search and the same search with an _include.
    /// <para>
    /// Include stages deliberately receive none of these. The surrogate range is a partition window over
    /// surrogate ids, but include rows are reached by reference from matched resources rather than by
    /// surrogate id, so applying it would silently drop legitimately-included resources living outside the
    /// boundary. The hash filter is reindex-only, and reindex does not use _include, so applying it would
    /// silently drop included resources whose hash merely differs from the current definition set.
    /// </para>
    /// </remarks>
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

    /// <summary>Renders the reindex-eligibility filter for one search-parameter hash.</summary>
    /// <remarks>
    /// r.SearchParamHash IS NULL means the resource has never been indexed and must qualify for reindex.
    /// Omitting this disjunct would silently skip exactly the resources most in need of indexing — the ones
    /// that have no hash because they pre-date the feature.
    /// </remarks>
    private static string EmitSearchParameterHashClause(SqlParameterRef hash, List<EmittedSqlParameter> parameters)
        => $"(r.SearchParamHash IS NULL OR r.SearchParamHash <> {EmitParam(hash, parameters)})";

    /// <summary>
    /// Appends the inclusive surrogate-id window to a shape's WHERE clause list. Extracted rather than
    /// inlined at each shape because omitting it in one shape is silent: an $export worker would read
    /// outside its partition, and since partitions are disjoint the only symptom is duplicated exported
    /// resources — no error anywhere. A new shape that needs the window must call this; one that
    /// deliberately does not (an include stage, whose rows are reached by reference rather than by
    /// surrogate id) is then visibly making that choice.
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
    /// Centralised rather than repeated per shape because a future feature that reads a resource column
    /// must be added in exactly one place — missing one shape produces a runtime "multi-part identifier
    /// could not be bound" error rather than a test failure.
    /// </summary>
    /// <param name="plan">The query plan being emitted.</param>
    /// <param name="includesProjection">
    /// Whether the calling shape emits the projection through this join. False for CountOnly (which has no
    /// rows to project) and for the includes match arm (which projects in the UNION ALL assembly instead).
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

    /// <summary>The projected column list, prefixed with ", " and qualified with the terminal join alias, or empty.</summary>
    /// <remarks>
    /// An empty column list is treated as equivalent to a null projection — projecting zero columns is
    /// the same as asking for identity-only output, and avoids emitting a dangling comma in the SELECT list.
    /// </remarks>
    private static string ProjectionColumns(ProjectionSpec? projection)
        => projection is null || projection.Columns.Count == 0
            ? string.Empty
            : ", " + string.Join(", ", projection.Columns.Select(c => $"r.[{c.Replace("]", "]]", StringComparison.Ordinal)}]"));

    /// <summary>
    /// The current-row filter for a dbo.Resource scan under a given visibility, already prefixed with
    /// " AND " and the caller's column qualifier, or empty when neither axis is constrained.
    /// </summary>
    /// <remarks>
    /// The leading space is load-bearing for a caller that embeds the result inline after another SQL
    /// token — dropping it yields <c>= @p0AND IsHistory = 0</c>, which only fails at parse time. A caller
    /// that instead places the filter on its own line trims it and supplies its own indentation; those
    /// two modes are the reason this returns a pre-joined string rather than the raw clauses.
    /// <para>
    /// Each axis is tri-state, mirroring <see cref="ResourceVisibility"/>: a <c>null</c> value emits no
    /// clause for that column, <c>false</c> emits <c>= 0</c> (current/live row), <c>true</c> emits
    /// <c>= 1</c> (superseded/deleted row). The predecessor of this helper emitted <c>= 0</c> when a flag
    /// was clear and nothing when it was set, which could not express a "history rows only" (<c>= 1</c>)
    /// filter at all; encoding the column value directly from the tri-state is what lets the same helper
    /// serve a history-only or soft-deleted-only scan without a second code path.
    /// </para>
    /// </remarks>
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
    /// visibility, or empty when it needs none.
    /// </summary>
    /// <remarks>
    /// Most search-param tables hold rows for current versions only, so history is filtered once at
    /// hydration. dbo.TokenText carries its own IsHistory column and does keep superseded rows, so a
    /// query against it has to exclude them itself. Driven off the catalog rather than the table name:
    /// the filter is required by any table that has the column, and the catalog is generated from DDL.
    /// This is the search-param-table counterpart of <see cref="ResourceRowFilter"/>, which belongs to
    /// dbo.Resource scans and additionally emits IsDeleted — a column no search-param table has.
    /// Returned unprefixed so each caller supplies its own separator.
    /// <para>
    /// Deliberately NOT a mechanical tri-state translation like <see cref="ResourceRowFilter"/>. The
    /// clause is emitted only when the history axis is pinned to current rows (<c>IsHistory == false</c>);
    /// for both <c>null</c> (history unconstrained) and <c>true</c> (history rows only) it renders empty.
    /// This mirrors the legacy generator, whose AppendHistoryClause returns early for every table whose
    /// name ends in "SearchParam" (bar the compartment-search special case) and so never constrains
    /// IsHistory on a search-param index table at all. The reason the two engines agree here is that the
    /// version a search RETURNS is selected once, at the dbo.Resource scan, by <see cref="ResourceRowFilter"/>;
    /// the search-param table's job is only to say WHICH resources match the predicate. For a latest-only
    /// search we additionally pin the index table to <c>IsHistory = 0</c> because TokenText's retained
    /// superseded rows would otherwise let a value that was true of an old version spuriously match a
    /// current resource. Once history is in scope that narrowing is not merely unnecessary but wrong: the
    /// alternative — emitting <c>IsHistory = 1</c> for a history-only search — would restrict the match set
    /// to resources whose predicate happened to be satisfied by a superseded index row, dropping any
    /// resource whose matching value lives only on its current TokenText row (the common case), so a
    /// history search for "status = final" would silently miss resources that are currently final. Emitting
    /// nothing lets the resource-level filter do the version selection and leaves the predicate free to
    /// match on any version's index row, which is exactly what legacy does.
    /// </para>
    /// </remarks>
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

        // A null ResourceTypeId is system-level (cross-type) search: emit no type filter at all rather
        // than a filter on some placeholder id. The requested types are narrowed by the plan's
        // MultiTypeResourceSource base set instead, which this CTE is intersected with.
        var typeFilter = p.ResourceTypeId is { } typeId ? $"ResourceTypeId = {typeId} AND " : string.Empty;

        return $"    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
               $"    FROM {p.Table.SchemaName}.{p.Table.TableName}\n" +
               $"    WHERE {typeFilter}SearchParamId = {p.SearchParamId}{historyClause}{predicateClause}";
    }

    /// <summary>Renders a chain as a join through dbo.ReferenceSearchParam and dbo.Resource, correlated to the inner match set, in the forward or reverse direction.</summary>
    private static string EmitChainJoin(CteDefinition.ChainJoin cj, List<EmittedSqlParameter> parameters, ResourceVisibility visibility)
    {
        // Deliberately hand-rolled string interpolation, not Predicate.Equal/Predicate.Or routed
        // through EmitPredicate -- Predicate.Equal's Value is a SqlParameterRef, and EmitPredicate's
        // Equal arm always calls EmitParam, which would bind a real @pN. Every id ChainJoin carries
        // (like ParamSource's SearchParamId/ResourceTypeId) must render as a literal, so building
        // real Predicate nodes here would silently reintroduce bound parameters and break the
        // parameter-ordinal invariant PlanExplainer relies on for ChainJoin.
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
                // Key 0 in the Valued phase must gate on the key being present, exactly like
                // String/Date's own i==0-is-INNER rule below -- an unconditional LEFT here would let
                // missing-key rows leak into both the Valued and MissingPrimary phases (duplicates
                // across the keyset page boundary) and let a NULL AggValue reach the seek predicate
                // unwrapped (SortValueExpr's isGuaranteedNonNull fast path assumes key 0/Valued is
                // truly non-null -- LEFT would break that guarantee). INNER against the derived table
                // is safe: MIN/MAX over zero grouped rows for a given (type, surrogate id) simply
                // produces no output row for that key, which is exactly INNER JOIN's semantics -- no
                // separate existence check is needed.
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
            throw new InvalidOperationException(
                "SortSpec.Phase == MissingPrimary with a LastUpdated, ResourceType, ResourceId, or otherwise " +
                "SearchParamId-less primary key reached Emit -- none of these is ever \"missing\" (all are " +
                "non-nullable resource columns), so none has a MissingPrimary segment. Lower.BuildSortSpec " +
                "already rejects this combination for all three; QueryPlan is a public construction surface, " +
                "so this guard exists defensively rather than trusting every caller routes through Lower.");
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
            : sort.Phase == SortPhase.Valued
                ? Enumerable.Range(0, sort.Keys.Count).ToList()
                : Enumerable.Range(1, sort.Keys.Count - 1).ToList();

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
            // Deliberately unwrapped even as a secondary key, where the join is LEFT: (ResourceTypeId,
            // ResourceSurrogateId) is dbo.Resource's clustered primary key (PKC_Resource), so every
            // (T1, Sid1) the CTE graph produces has a matching row and the LEFT can never yield NULL.
            // Note this is architectural, not enforced -- no FK ties the search-param tables to
            // dbo.Resource -- so a future source of match rows that are not real resources would
            // break it silently.
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
    /// Maps a search-param table column's real DDL SQL type to the literal ISNULL needs to substitute for a
    /// missing aggregated sort value. The five Aggregated leaf types resolve to two SQL type families today
    /// (varchar for Token/Reference/Uri, decimal for Number/Quantity). nvarchar is included for parity with
    /// String's own N'' sentinel even though no current Aggregated column uses it.
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
    /// the (T1, Sid1) tiebreak. For a custom (search-parameter) sort the m.T1 tiebreak is dropped so every
    /// page orders by (sort keys…, Sid1) -- see <see cref="HasCustomSortKey"/> for why this is keyed off the
    /// sort shape rather than the presence of a page boundary, and why Sid1 alone is a total order.
    /// </summary>
    private static string EmitOrderBy(SortSpec? sort)
    {
        var activeIndices = ActiveKeyIndices(sort);
        var terms = activeIndices.Select(i =>
            $"{SortValueExpr(sort!, i)} {(sort!.Keys[i].Direction == SortOrder.Ascending ? "ASC" : "DESC")}").ToList();

        // SortValueExpr renders LastUpdated as "m.Sid1" and ResourceType as "m.T1" -- if either is an active
        // key, appending the same column again as the trailing tiebreak would reference it twice in one
        // ORDER BY list, which SQL Server rejects (Msg 145, "A column has been specified more than once in
        // the order by list"). Dropping the duplicate is safe rather than merely legal: a key that already
        // orders by that column has fully determined it, and the tiebreak exists only to break ties the keys
        // leave, so it has nothing left to contribute. Note the tiebreak is unconditionally ASC while a key
        // may be DESC, so this also preserves a descending _type or _lastUpdated ordering that an appended
        // ASC term could not have expressed.
        var hasLastUpdatedKey = activeIndices.Any(i => sort!.Keys[i].Kind == SortKeyKind.LastUpdated);
        var hasResourceTypeKey = activeIndices.Any(i => sort!.Keys[i].Kind == SortKeyKind.ResourceType);

        // Drop the m.T1 tiebreak for a custom sort: its keyset order is (sort keys…, Sid1), type-free, and a
        // typeless page's Sid1-only seek must be able to reproduce it exactly. Legacy is the cautionary tale
        // here -- it orders (sortValue, T1, Sid1) yet seeks (sortValue, Sid1), so within a run of tied sort
        // values a type X row with a higher surrogate id can sort before a type Y row with a lower one, and
        // the Sid1-only seek for the next page starts past the Y row and drops it. Keeping m.T1 out of the
        // ORDER BY closes that gap; Sid1's global uniqueness makes what remains a total order. This decision
        // never consults the page boundary, so page 1 and every later page of the walk order identically.
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
    /// projected SortValueN columns, then the (T1, Sid1) tiebreak. A custom sort drops the T1 tiebreak here
    /// for the same reason as <see cref="EmitOrderBy"/>, ordering matches by (SortValueN…, Sid1) so page 1
    /// and every later (possibly typeless) page of the walk share one ordering.
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
    /// lexicographic branches over the active sort keys, then the surrogate-id tiebreak, so it stays in step
    /// with the ORDER BY. A typed <see cref="PageSpec"/> (BoundaryResourceTypeId non-null) breaks the final
    /// tie on (T1, Sid1); a typeless one breaks it on Sid1 alone and never references the type column, which
    /// is sound because ResourceSurrogateId is globally unique so Sid1 is already a total order. Throws if
    /// the boundary value count does not match the current phase's active keys.
    /// </summary>
    private static string EmitSeekPredicate(SortSpec? sort, PageSpec page, List<EmittedSqlParameter> parameters)
    {
        var activeIndices = ActiveKeyIndices(sort);
        if (page.Boundary.Count != activeIndices.Count)
        {
            throw new InvalidOperationException(
                $"PageSpec.Boundary has {page.Boundary.Count} value(s) but the current SortSpec phase has " +
                $"{activeIndices.Count} active key(s) -- boundary values must be freshly decoded for the " +
                "current phase, never reused across a Valued/MissingPrimary transition.");
        }

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

        // Bind the type parameter (when present) before the surrogate id, so a typed page keeps its
        // historical @pN ordinals exactly; a typeless page binds no type parameter at all and its seek
        // omits the type column entirely.
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
    /// Renders a NotReferencedSource: current, non-deleted rows of dbo.Resource for the target type that
    /// no dbo.ReferenceSearchParam row points at. The anti-join correlates on reference-target identity
    /// (ReferenceResourceId/ReferenceResourceTypeId against the candidate's own ResourceId/ResourceTypeId),
    /// optionally narrowed to references originating from one source type and/or one reference path. Only
    /// the target type is bound (as ResourceSource binds its own); the inner ids are schema surrogates,
    /// inlined like every other schema id.
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
    /// optional additional predicate and no SearchParamId/ResourceTypeId filter.
    /// </summary>
    /// <remarks>
    /// Visibility reaches this emitter through <see cref="SearchParamTableHistoryClause"/>, not through
    /// <see cref="ResourceRowFilter"/>: the table scanned here is a search-param index table, not
    /// dbo.Resource. ResourceRowFilter would emit <c>IsDeleted = 0</c>, a column no search-param table has
    /// (dbo.DateTimeSearchParam, this node's only producer today, has neither IsHistory nor IsDeleted), so
    /// applying it would turn valid SQL into a parse error under the default visibility.
    /// </remarks>
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

    /// <summary>Renders a ResourceSource: current, non-deleted rows of dbo.Resource for one type, with an optional nested-scope predicate.</summary>
    /// <remarks>
    /// Note: this emitter binds its type id as a parameter (EmitParam), where the sibling emitters
    /// (ParamSource, ChainJoin, CompartmentSource, MultiTypeResourceSource) render type ids as literals.
    /// The binding predates the current design (commit ce8c0860) and is functionally correct -- a bound
    /// int works. Converging on literals would be the consistent choice, but doing so would shift the
    /// parameter ordinals every downstream emitter and its tests depend on (see the ChainJoin remark on
    /// keeping ordinals stable), so it is deliberately left as-is rather than churned for no functional gain.
    /// </remarks>
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
        // Build the WHERE from an explicit clause list rather than concatenating prefix-" AND " strings
        // and stripping the leading AND. The concatenate-then-strip idiom works only because every piece
        // uses the " AND " prefix convention; any future clause that does not would silently corrupt the
        // SQL. A clause list is the pattern the rest of the file already uses and composes correctly.
        //
        // Type ids are emitted as literals, not bound parameters, matching ParamSource and ChainJoin.
        // An empty list means "every type" (AllTypes factory); do not emit a type filter in that case.
        // Keeping unresolvable sentinel ids (-1) in the list is intentional: they match no row, which is
        // the correct answer for an unknown type. Dropping them could collapse a list of all-unknown
        // types to empty, which would silently widen to a full-table scan instead of matching nothing.
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
    /// reference param and type ids, seeded from the match page and/or earlier stages via EXISTS.
    /// </summary>
    /// <remarks>
    /// The ordinary path selects <c>TOP (Limit + 1)</c> ordered by (T1, Sid1) so the stage's own limit
    /// companion can detect truncation. The IncludesOnly path drops both: the budget is applied once,
    /// globally, over the union of stages by <see cref="EmitGlobalIncludesPage"/>, so a per-stage TOP would
    /// apply it twice. It would in fact be <em>safe</em> to keep — a row in the global top <c>Limit + 1</c>
    /// has within-stage rank no worse than its rank across the union (rows the union places ahead of it can
    /// only come from this same stage or from others, never fewer than zero), so truncating a stage at
    /// <c>Limit + 1</c> can never hide a row the global page needed, even when one stage's rows all sort
    /// before another's. It is dropped because it is redundant, not because it is unsafe. And once the TOP
    /// is gone the ORDER BY must go too: a CTE's own ORDER BY is illegal T-SQL without TOP or OFFSET/FETCH
    /// (SQL Server Msg 1033), so keeping the per-stage ORDER BY while dropping the per-stage TOP would not
    /// even compile.
    /// <para>
    /// When a resume cursor is present each stage additionally carries the keyset predicate
    /// <c>(type &gt; @t OR (type = @t AND sid &gt; @sid))</c> over its own output columns, so the page resumes
    /// strictly after the previous page's last row under the global <c>ORDER BY T1 ASC, Sid1 ASC</c>. The
    /// output columns differ by direction, so the predicate uses the same (type, surrogate) pair the SELECT
    /// list projects: <c>r.*</c> for a forward include, <c>rsp.*</c> for a reverse one.
    /// </para>
    /// </remarks>
    private static string EmitIncludeStage(
        IncludeStage stage,
        ResourceVisibility visibility,
        bool includesOnly,
        (string Type, string Surrogate)? resumeParams)
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
        whereClauses.Add(EmitSeedExists(stage, seedCorrelationAlias));

        if (stage.Constraints is { Count: > 0 } constraints)
        {
            foreach (var constraint in constraints)
            {
                whereClauses.Add(EmitConstraintGuard(constraint, outputTypeColumn, outputSurrogateColumn));
            }
        }

        if (resumeParams is { } resume)
        {
            whereClauses.Add(
                $"({outputTypeColumn} > {resume.Type} OR " +
                $"({outputTypeColumn} = {resume.Type} AND {outputSurrogateColumn} > {resume.Surrogate}))");
        }

        var rowFilter = ResourceRowFilter(visibility, "r.");

        // Own-line placement, so the helper's leading space is replaced by this line's indentation.
        // An inline caller must not trim it; see ResourceRowFilter's remarks.
        var rowFilterLine = rowFilter.Length > 0 ? $"       {rowFilter.TrimStart()}\n" : string.Empty;

        // Drop the per-stage TOP and its ORDER BY for the IncludesOnly page: the budget is applied once,
        // globally, over the union of stages, and a CTE ORDER BY without TOP is illegal T-SQL anyway. See
        // this method's remarks for why keeping the TOP would be safe but redundant.
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

    /// <summary>Renders the EXISTS clause correlating an include row back to its seeds — the match page and/or earlier stages — unioned together.</summary>
    private static string EmitSeedExists(IncludeStage stage, string correlationAlias)
    {
        var branches = new List<string>();
        if (stage.SeedFromMatch)
        {
            branches.Add($"SELECT 1 FROM {MatchPage} m WHERE m.T1 = {correlationAlias}.ResourceTypeId AND m.Sid1 = {correlationAlias}.ResourceSurrogateId");
        }

        foreach (var seedStageIndex in stage.SeedStages)
        {
            branches.Add($"SELECT 1 FROM {IncludeLimitLabel(seedStageIndex)} m WHERE m.T1 = {correlationAlias}.ResourceTypeId AND m.Sid1 = {correlationAlias}.ResourceSurrogateId");
        }

        return $"EXISTS (\n        {string.Join("\n        UNION ALL\n        ", branches)}\n    )";
    }

    /// <summary>
    /// Renders one access-constraint guard on an include stage: a row of the constrained type must appear
    /// in the constraint CTE, while a row of any other type the stage produces passes untouched. The
    /// leading "type &lt;&gt; id OR" is what keeps a multi-type or wildcard stage from dropping the rows the
    /// constraint does not govern — without it the EXISTS would reject every row whose type has no matching
    /// constraint row, silently narrowing types the caller is fully entitled to see.
    /// </summary>
    private static string EmitConstraintGuard(IncludeConstraint constraint, string outputTypeColumn, string outputSurrogateColumn)
        => $"({outputTypeColumn} <> {constraint.ConstraintTypeId} OR EXISTS (" +
           $"SELECT 1 FROM {CteLabel(constraint.ConstraintCteIndex)} ac " +
           $"WHERE ac.T1 = {outputTypeColumn} AND ac.Sid1 = {outputSurrogateColumn}))";

    /// <summary>
    /// Renders a predicate tree to a WHERE fragment, fully parenthesizing And/Or so operator precedence
    /// never depends on the surrounding context.
    /// </summary>
    /// <param name="qualifier">
    /// An alias prefix (including the dot) to put in front of every column, or empty for none. A CTE body
    /// has exactly one table in scope, so unqualified is unambiguous there and stays the default. The outer
    /// query does not: the resource join (<c>r</c>) and a <see cref="SortKeyKind.ResourceId"/> sort join
    /// (<c>rid0</c>) are both <c>dbo.Resource</c>, so an unqualified <c>ResourceId</c>/<c>ResourceTypeId</c>/
    /// <c>ResourceSurrogateId</c> in the outer predicate binds to neither and SQL Server raises Msg 209.
    /// Grammar tests cannot see this — an ambiguous identifier parses fine.
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
    /// The alias the outer query's <c>dbo.Resource</c> join uses. The outer predicate's columns are all
    /// <c>dbo.Resource</c> columns, and <see cref="NeedsResourceJoin"/> guarantees this join exists whenever
    /// an outer predicate does, so qualifying with it is always both valid and unambiguous.
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
