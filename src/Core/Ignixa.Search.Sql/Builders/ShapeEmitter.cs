using Ignixa.Search.Expressions;
using Ignixa.Search.Sql.Ast;
using static Ignixa.Search.Sql.Builders.CteEmitter;
using static Ignixa.Search.Sql.Builders.IncludeEmitter;
using static Ignixa.Search.Sql.Builders.MatchPageEmitter;
using static Ignixa.Search.Sql.Builders.PredicateEmitter;
using static Ignixa.Search.Sql.Builders.SortEmitter;
using static Ignixa.Search.Sql.Builders.SqlBuilder;
using static Ignixa.Search.Sql.Builders.SqlLabels;

namespace Ignixa.Search.Sql.Builders;

/// <summary>Emits the three terminal shapes — count, match-only and includes — and their assembly SELECTs.</summary>
internal static class ShapeEmitter
{
    /// <summary>
    /// Emits the Count shape: COUNT_BIG(DISTINCT m.Sid1) over the match CTE. Row caps, offsets and keyset
    /// boundaries are ignored, since a count is of the whole result set rather than a page of it. So is the
    /// sort, unless the shape is <see cref="ResultShape.Count.CurrentSortPhase"/>, which applies the phase's
    /// key join and its MissingPrimary filter.
    /// </summary>
    internal static void EmitCountOnlyShape(
        QueryPlan plan,
        SqlTextWriter writer,
        List<CteBody> cteBodies,
        List<EmittedSqlParameter> parameters)
    {
        WriteCteHeader(writer, plan, cteBodies);
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
    internal static void EmitMatchOnlyShape(
        QueryPlan plan,
        SqlTextWriter writer,
        List<CteBody> cteBodies,
        List<EmittedSqlParameter> parameters,
        ResourceVisibility visibility)
    {
        var top = SelectBlock.RenderTop(plan.Top);
        var projectionCols = ProjectionColumns(plan.Projection);
        var projectionJoinFilter = projectionCols.Length > 0 ? ResourceRowFilter(visibility, "r.") : string.Empty;
        var sortJoins = EmitSortJoins(plan.Sort);
        var sortColumns = EmitSortSelectColumns(plan.Sort);

        var whereClauses = BuildMatchWhereClauses(plan.MatchSpec, parameters, out var seekClauseIndex);

        WriteCteHeader(writer, plan, cteBodies);
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
            writer.Append($"\nOFFSET {EmitParam(new SqlParameterRef(offsetPage.Offset), parameters)} ROWS FETCH NEXT {EmitParam(new SqlParameterRef(offsetPage.FetchCount), parameters)} ROWS ONLY");
        }
    }

    /// <summary>
    /// Emits the includes shape: match-page CTE, include-stage CTEs, and the assembly stitching them into one
    /// (T1, Sid1, IsMatch, IsPartial) result. Two assemblies: the ordinary path unions each stage's limit
    /// companion and orders matches-first; the IncludesOnly path budgets once over the unlimited stage bodies
    /// and orders by (T1, Sid1) to resume from a boundary.
    /// </summary>
    internal static void EmitIncludesShape(
        QueryPlan plan,
        IReadOnlyList<IncludeStage> includes,
        SqlTextWriter writer,
        List<CteBody> cteBodies,
        List<EmittedSqlParameter> parameters,
        ResourceVisibility visibility)
    {
        WriteCteHeader(writer, plan, cteBodies);

        // Bind the boundary here — after the match-page CTE, before the stage loop — so it takes the first
        // stage-level @pN, preserving the leading-ordinal invariant EmitCteBodies documents. Include CTEs bind
        // no parameters; the predicate it feeds is emitted later by EmitGlobalIncludesPage.
        (string Type, string Surrogate)? resumeParams = plan is { IncludesOnly: true, IncludeBoundary: { } boundary }
            ? (EmitParam(new SqlParameterRef(boundary.TypeId), parameters), EmitParam(new SqlParameterRef(boundary.SurrogateId), parameters))
            : null;

        var matchSeedLabel = CteName(plan, plan.IncludeSeed!.Value.Index);

        for (var i = 0; i < includes.Count; i++)
        {
            WriteIncludeStageCtes(writer, plan, includes[i], i, visibility, plan.IncludesOnly, matchSeedLabel);
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
    /// Emits the outer global-page SELECT for an IncludesOnly page: <c>SELECT DISTINCT TOP (Limit + 1)
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
    /// Writes an include stage's CTEs. The ordinary path writes two — the unlimited body and its
    /// limit-applying companion. The IncludesOnly path writes only the body: its budget is applied once,
    /// globally, by <see cref="EmitGlobalIncludesPage"/>, so a per-stage limit companion would apply the
    /// budget twice.
    /// </summary>
    private static void WriteIncludeStageCtes(
        SqlTextWriter writer,
        QueryPlan plan,
        IncludeStage stage,
        int index,
        ResourceVisibility visibility,
        bool includesOnly,
        string matchSeedLabel)
    {
        writer.Append(",\n");
        using (writer.Section(IncludeLabel(index), SqlRangeKind.Include))
        {
            writer.Append($"{IncludeLabel(index)} AS (\n{EmitIncludeStage(plan, stage, visibility, includesOnly, matchSeedLabel)}\n)");
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
    /// Whether a shape must join dbo.Resource: true when any plan feature references an <c>r.</c> column.
    /// Centralised so a missing shape is a runtime bind error, not a test failure. Internal (not private) so
    /// PlanExplainer's matchPage row reads the identical decision instead of a second copy that can drift.
    /// </summary>
    /// <param name="plan">The query plan being emitted.</param>
    /// <param name="includesProjection">
    /// Whether the calling shape projects through this join. False for CountOnly and the includes match arm.
    /// </param>
    internal static bool NeedsResourceJoin(QueryPlan plan, bool includesProjection)
        => plan.OuterPredicate is not null
            || plan.SearchParameterHash is not null
            || (includesProjection && plan.Projection is { Columns.Count: > 0 });

    /// <summary>
    /// Joins already-rendered WHERE fragments with " AND ", wrapping the one at <paramref name="seekClauseIndex"/>
    /// (if any) in its own "seek" section so the keyset-seek predicate stays traceable within the outer "where" section.
    /// </summary>
    internal static void WriteAndJoinedClauses(SqlTextWriter writer, List<string> clauses, int? seekClauseIndex)
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
}
