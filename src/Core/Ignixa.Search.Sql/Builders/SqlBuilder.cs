#pragma warning disable CA1724

using Ignixa.Search.Expressions;
using Ignixa.Search.Sql.Ast;
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
    }

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
    /// </remarks>
    private static void EmitCountOnlyShape(
        QueryPlan plan,
        SqlTextWriter writer,
        List<string> cteBlocks,
        List<EmittedSqlParameter> parameters)
    {
        WriteCteHeader(writer, cteBlocks);
        writer.Append("\n");
        writer.Append($"SELECT COUNT_BIG(DISTINCT m.Sid1) FROM {CteLabel(plan.Match.Index)} m");

        if (NeedsResourceJoin(plan, includesProjection: false))
        {
            writer.Append("\nINNER JOIN dbo.Resource r ON r.ResourceTypeId = m.T1 AND r.ResourceSurrogateId = m.Sid1");
        }

        var whereClauses = new List<string>();

        if (plan.OuterPredicate is not null)
        {
            whereClauses.Add(EmitPredicate(plan.OuterPredicate, parameters));
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
    }

    /// <summary>
    /// Emits the includes shape: the match-page CTE, a pair of CTEs per include stage, and the UNION ALL
    /// that stitches them into one (T1, Sid1, IsMatch, IsPartial) result ordered matches-first.
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

        for (var i = 0; i < includes.Count; i++)
        {
            WriteIncludeStageCtes(writer, includes[i], i, visibility);
        }

        writer.Append("\n");

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

        // A CTE's own ORDER BY is only legal T-SQL alongside TOP (SQL Server Msg 1033) -- when plan.Top
        // is null, cteMatchPage has no TOP and so must have no ORDER BY of its own either. The outer
        // final UNION ALL's ORDER BY (EmitOuterOrderByForIncludes) is a plain top-level SELECT,
        // always legal regardless of TOP, and is unaffected by this.
        var cteOrderBy = plan.Top is not null ? $"\n    ORDER BY {EmitOrderBy(plan.Sort)}" : string.Empty;

        var whereClauses = BuildMatchWhereClauses(plan, parameters, out var seekClauseIndex);

        using (writer.Section(MatchPage, SqlRangeKind.MatchPage))
        {
            writer.Append(
                $"{MatchPage} AS (\n" +
                $"    SELECT {top}m.T1, m.Sid1{sortColumns}\n" +
                $"    FROM {CteLabel(plan.Match.Index)} m{sortJoins}{resourceJoin}");

            WriteWhereSection(writer, whereClauses, seekClauseIndex, indent: "    ");

            writer.Append(cteOrderBy);
            writer.Append("\n)");
        }
    }

    /// <summary>Writes one include stage's pair of CTEs: the unlimited body and its limit-applying companion.</summary>
    private static void WriteIncludeStageCtes(SqlTextWriter writer, IncludeStage stage, int index, ResourceVisibility visibility)
    {
        writer.Append(",\n");
        using (writer.Section(IncludeLabel(index), SqlRangeKind.Include))
        {
            writer.Append($"{IncludeLabel(index)} AS (\n{EmitIncludeStage(stage, visibility)}\n)");
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
    private static string EmitIncludeLimitStage(IncludeStage stage, int index)
        => $"    SELECT TOP ({stage.Limit}) T1, Sid1,\n" +
           $"           CASE WHEN COUNT_BIG(*) OVER() > {stage.Limit} THEN 1 ELSE 0 END AS IsPartial\n" +
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
            clauses.Add(EmitPredicate(plan.OuterPredicate, parameters));
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
    /// " AND " and the caller's column qualifier, or empty when both relaxations are on.
    /// </summary>
    /// <remarks>
    /// The leading space is load-bearing for a caller that embeds the result inline after another SQL
    /// token — dropping it yields <c>= @p0AND IsHistory = 0</c>, which only fails at parse time. A caller
    /// that instead places the filter on its own line trims it and supplies its own indentation; those
    /// two modes are the reason this returns a pre-joined string rather than the raw clauses.
    /// </remarks>
    private static string ResourceRowFilter(ResourceVisibility visibility, string qualifier)
    {
        var clauses = new List<string>(2);
        if (!visibility.IncludeHistory)
        {
            clauses.Add($"{qualifier}IsHistory = 0");
        }

        if (!visibility.IncludeDeleted)
        {
            clauses.Add($"{qualifier}IsDeleted = 0");
        }

        return clauses.Count == 0 ? string.Empty : " AND " + string.Join(" AND ", clauses);
    }

    /// <summary>Renders a ParamSource: distinct (type, surrogate id) rows from one search-param table filtered by SearchParamId and its optional predicate.</summary>
    private static string EmitParamSource(CteDefinition.ParamSource p, List<EmittedSqlParameter> parameters, ResourceVisibility visibility)
    {
        var predicateClause = p.Predicate is null ? string.Empty : $" AND {EmitPredicate(p.Predicate, parameters)}";

        // Most search-param tables hold rows for current versions only, so history is filtered once at
        // hydration. dbo.TokenText carries its own IsHistory column and does keep superseded rows, so a
        // query against it has to exclude them itself. Driven off the catalog rather than the table name:
        // the filter is required by any table that has the column, and the catalog is generated from DDL.
        var historyClause = !visibility.IncludeHistory && p.Table.Columns.Any(c => c.Name == "IsHistory")
            ? " AND IsHistory = 0"
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
            if (key.Kind == SortKeyKind.LastUpdated)
            {
                continue; // resource-column key, no join needed.
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
                "SortSpec.Phase == MissingPrimary with a LastUpdated, ResourceId, or otherwise SearchParamId-less " +
                "primary key reached Emit -- none of these are ever \"missing\" (all are non-nullable resource " +
                "columns), so none has a MissingPrimary segment. Lower.BuildSortSpec already rejects this " +
                "combination for LastUpdated and ResourceId; QueryPlan is a public construction surface, so this " +
                "guard exists defensively rather than trusting every caller routes through Lower.");
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

    /// <summary>Renders the ORDER BY for the plain (no-includes) path: each active key's value and direction, then the (T1, Sid1) tiebreak.</summary>
    private static string EmitOrderBy(SortSpec? sort)
    {
        var activeIndices = ActiveKeyIndices(sort);
        var terms = activeIndices.Select(i =>
            $"{SortValueExpr(sort!, i)} {(sort!.Keys[i].Direction == SortOrder.Ascending ? "ASC" : "DESC")}").ToList();

        // SortValueExpr(LastUpdated) is literally "m.Sid1" -- if an active key is LastUpdated, appending
        // "m.Sid1 ASC" again as the trailing tiebreak would reference the same column twice in one ORDER
        // BY list, which SQL Server rejects (Msg 145, "A column has been specified more than once in the
        // order by list"). m.T1 is never duplicated this way (no key's value expression is T1), so it is
        // always safe to append.
        var hasLastUpdatedKey = activeIndices.Any(i => sort!.Keys[i].Kind == SortKeyKind.LastUpdated);
        terms.Add("m.T1 ASC");
        if (!hasLastUpdatedKey)
        {
            terms.Add("m.Sid1 ASC");
        }

        return string.Join(", ", terms);
    }

    /// <summary>Renders the final ORDER BY for the includes path: matches before includes (IsMatch DESC), then the projected SortValueN columns, then the (T1, Sid1) tiebreak.</summary>
    private static string EmitOuterOrderByForIncludes(SortSpec? sort)
    {
        var activeIndices = ActiveKeyIndices(sort);
        var terms = activeIndices.Select((idx, ordinal) =>
            $"SortValue{ordinal} {(sort!.Keys[idx].Direction == SortOrder.Ascending ? "ASC" : "DESC")}");
        return string.Join(", ", terms.Prepend("IsMatch DESC").Append("T1 ASC").Append("Sid1 ASC"));
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
    /// lexicographic branches over the active sort keys, then the (T1, Sid1) tiebreak, so it stays in step
    /// with the ORDER BY. Throws if the boundary value count does not match the current phase's active keys.
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
        var typeParam = EmitParam(page.BoundaryResourceTypeId, parameters);
        var sidParam = EmitParam(page.BoundarySurrogateId, parameters);

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
        branches.Add($"({allEqualPrefix}m.T1 = {typeParam} AND m.Sid1 > {sidParam})");
        branches.Add($"({allEqualPrefix}m.T1 > {typeParam})");

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

        if (!visibility.IncludeHistory)
        {
            clauses.Add("IsHistory = 0");
        }

        if (!visibility.IncludeDeleted)
        {
            clauses.Add("IsDeleted = 0");
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

    /// <summary>Renders one include stage: the ReferenceSearchParam/Resource join for its direction, filtered by reference param and type ids, seeded from the match page and/or earlier stages via EXISTS. Selects TOP(Limit+1) to detect truncation.</summary>
    private static string EmitIncludeStage(IncludeStage stage, ResourceVisibility visibility)
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

        var rowFilter = ResourceRowFilter(visibility, "r.");

        // Own-line placement, so the helper's leading space is replaced by this line's indentation.
        // An inline caller must not trim it; see ResourceRowFilter's remarks.
        var rowFilterLine = rowFilter.Length > 0 ? $"       {rowFilter.TrimStart()}\n" : string.Empty;

        return $"    SELECT DISTINCT TOP ({stage.Limit + 1}) {selectColumns}\n" +
               $"    FROM dbo.ReferenceSearchParam rsp\n" +
               $"    INNER JOIN dbo.Resource r\n" +
               $"        ON r.ResourceTypeId = rsp.ReferenceResourceTypeId\n" +
               $"       AND r.ResourceId = rsp.ReferenceResourceId\n" +
               rowFilterLine +
               $"    WHERE {string.Join("\n      AND ", whereClauses)}\n" +
               $"    ORDER BY T1 ASC, Sid1 ASC";
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

    /// <summary>Renders a predicate tree to a WHERE fragment, fully parenthesizing And/Or so operator precedence never depends on the surrounding context.</summary>
    private static string EmitPredicate(Predicate predicate, List<EmittedSqlParameter> parameters) => predicate switch
    {
        Predicate.Equal e => $"{e.Column.Column} = {EmitParam(e.Value, parameters)}{EmitCollation(e.Collation)}",
        Predicate.Like l => $"{l.Column.Column}{EmitCollation(l.Collation)} LIKE {EmitParam(EscapeLike(l), parameters)} ESCAPE '\\'",
        Predicate.And a => $"({EmitPredicate(a.Left, parameters)} AND {EmitPredicate(a.Right, parameters)})",
        Predicate.LessThan lt => $"{lt.Column.Column} < {EmitParam(lt.Value, parameters)}",
        Predicate.LessThanOrEqual le => $"{le.Column.Column} <= {EmitParam(le.Value, parameters)}",
        Predicate.GreaterThan gt => $"{gt.Column.Column} > {EmitParam(gt.Value, parameters)}",
        Predicate.GreaterThanOrEqual ge => $"{ge.Column.Column} >= {EmitParam(ge.Value, parameters)}",
        Predicate.Or or => $"({EmitPredicate(or.Left, parameters)} OR {EmitPredicate(or.Right, parameters)})",
        Predicate.Not not => $"NOT ({EmitPredicate(not.Operand, parameters)})",
        Predicate.IsNull isNull => $"{isNull.Column.Column} IS NULL",
        Predicate.False => PlanExplainer.UnsatisfiableRendering,
        Predicate.PrefixOfParameter pop => $"LEFT({EmitParam(pop.Value, parameters)}, LEN({pop.Column.Column})){EmitCollation(pop.Collation)} = {pop.Column.Column}",
        _ => throw new NotSupportedException($"No Emit for {predicate.GetType().Name}."),
    };

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
