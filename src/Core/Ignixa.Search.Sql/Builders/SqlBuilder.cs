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
    /// Renders a plan to SQL and its bound parameters. Emits one of three shapes: a COUNT_BIG SELECT when
    /// CountOnly, a plain (T1, Sid1) select (with optional sort/paging) when there are no includes, or a
    /// match-page CTE plus per-stage include CTEs unioned into a (T1, Sid1, IsMatch, IsPartial) result.
    /// </summary>
    public static EmittedSql Run(QueryPlan plan, EmitOptions? options = null)
    {
        var parameters = new List<EmittedSqlParameter>();
        var writer = new SqlTextWriter(options?.IncludeTextRanges ?? false);
        var cteBlocks = new List<string>();

        for (var i = 0; i < plan.Ctes.Count; i++)
        {
            cteBlocks.Add($"{CteLabel(i)} AS (\n{EmitCte(plan.Ctes[i], parameters)}\n)");
        }

        if (plan.CountOnly)
        {
            writer.Append(";WITH ");
            writer.AppendJoin(",\n", cteBlocks, CteLabel, SqlRangeKind.Cte);
            writer.Append("\n");

            var countSortJoins = plan.CountPhaseScoped ? EmitSortJoins(plan.Sort) : string.Empty;
            writer.Append($"SELECT COUNT_BIG(DISTINCT m.Sid1) FROM {CteLabel(plan.Match.Index)} m{countSortJoins}");

            var countWhereClauses = new List<string>();
            if (plan.OuterPredicate is not null)
            {
                countWhereClauses.Add(EmitPredicate(plan.OuterPredicate, parameters));
            }

            if (plan.CountPhaseScoped && plan.Sort is { Phase: SortPhase.MissingPrimary })
            {
                countWhereClauses.Add(EmitMissingPrimaryFilter(plan.Sort));
            }

            if (countWhereClauses.Count > 0)
            {
                var resourceJoin = plan.OuterPredicate is null
                    ? string.Empty
                    : "\nINNER JOIN dbo.Resource r ON r.ResourceTypeId = m.T1 AND r.ResourceSurrogateId = m.Sid1";
                writer.Append(resourceJoin);
                writer.Append("\nWHERE ");
                using (writer.Section(Where, SqlRangeKind.Where))
                {
                    writer.Append(string.Join(" AND ", countWhereClauses));
                }
            }

            return new EmittedSql(writer.ToString(), parameters, writer.Ranges);
        }

        var top = plan.Top is { } n ? $"TOP ({n}) " : string.Empty;

        if (plan.Includes is not { Count: > 0 } includes)
        {
            var sortJoins = EmitSortJoins(plan.Sort);
            var sortColumns = EmitSortSelectColumns(plan.Sort);
            var orderByText = EmitOrderBy(plan.Sort);

            var whereClauses = new List<string>();
            int? seekClauseIndex = null;
            if (plan.OuterPredicate is not null)
            {
                whereClauses.Add(EmitPredicate(plan.OuterPredicate, parameters));
            }

            if (plan.Sort is { Phase: SortPhase.MissingPrimary })
            {
                whereClauses.Add(EmitMissingPrimaryFilter(plan.Sort));
            }

            if (plan.Page is { } page)
            {
                seekClauseIndex = whereClauses.Count;
                whereClauses.Add(EmitSeekPredicate(plan.Sort, page, parameters));
            }

            writer.Append(";WITH ");
            writer.AppendJoin(",\n", cteBlocks, CteLabel, SqlRangeKind.Cte);
            writer.Append("\n");
            writer.Append($"SELECT {top}m.T1, m.Sid1{sortColumns} FROM {CteLabel(plan.Match.Index)} m{sortJoins}");

            if (whereClauses.Count > 0)
            {
                var resourceJoin = plan.OuterPredicate is null
                    ? string.Empty
                    : "\nINNER JOIN dbo.Resource r ON r.ResourceTypeId = m.T1 AND r.ResourceSurrogateId = m.Sid1";
                writer.Append(resourceJoin);
                writer.Append("\nWHERE ");
                using (writer.Section(Where, SqlRangeKind.Where))
                {
                    WriteAndJoinedClauses(writer, whereClauses, seekClauseIndex);
                }
            }

            writer.Append("\nORDER BY ");
            using (writer.Section(OrderBy, SqlRangeKind.OrderBy))
            {
                writer.Append(orderByText);
            }

            if (plan.OffsetPage is { } offsetPage)
            {
                writer.Append($"\nOFFSET {EmitParam(new SqlParameterRef(offsetPage.Offset), parameters)} ROWS FETCH NEXT {EmitParam(new SqlParameterRef(offsetPage.Limit), parameters)} ROWS ONLY");
            }

            return new EmittedSql(writer.ToString(), parameters, writer.Ranges);
        }

        var matchSortJoins = EmitSortJoins(plan.Sort);
        var matchSortColumns = EmitSortSelectColumns(plan.Sort);
        var activeSortKeyCount = ActiveKeyIndices(plan.Sort).Count;

        // A CTE's own ORDER BY is only legal T-SQL alongside TOP (SQL Server Msg 1033) -- when plan.Top
        // is null, cteMatchPage has no TOP and so must have no ORDER BY of its own either. The outer
        // final UNION ALL's ORDER BY (EmitOuterOrderByForIncludes, below) is a plain top-level SELECT,
        // always legal regardless of TOP, and is unaffected by this.
        var cteOrderBy = plan.Top is not null || plan.OffsetPage is not null
            ? $"\n    ORDER BY {EmitOrderBy(plan.Sort)}"
            : string.Empty;

        var matchWhereClauses = new List<string>();
        int? matchSeekClauseIndex = null;
        if (plan.OuterPredicate is not null)
        {
            matchWhereClauses.Add(EmitPredicate(plan.OuterPredicate, parameters));
        }

        if (plan.Sort is { Phase: SortPhase.MissingPrimary } missingPhaseSort)
        {
            matchWhereClauses.Add(EmitMissingPrimaryFilter(missingPhaseSort));
        }

        if (plan.Page is { } matchPage)
        {
            matchSeekClauseIndex = matchWhereClauses.Count;
            matchWhereClauses.Add(EmitSeekPredicate(plan.Sort, matchPage, parameters));
        }

        var matchResourceJoin = plan.OuterPredicate is null
            ? string.Empty
            : "\n    INNER JOIN dbo.Resource r ON r.ResourceTypeId = m.T1 AND r.ResourceSurrogateId = m.Sid1";

        var incBlocks = new List<string>();
        var incLimBlocks = new List<string>();
        for (var i = 0; i < includes.Count; i++)
        {
            var stage = includes[i];
            incBlocks.Add(EmitIncludeStage(stage));
            incLimBlocks.Add(
                $"    SELECT TOP ({stage.Limit}) T1, Sid1,\n" +
                $"           CASE WHEN COUNT_BIG(*) OVER() > {stage.Limit} THEN 1 ELSE 0 END AS IsPartial\n" +
                $"    FROM {IncludeLabel(i)}\n" +
                $"    ORDER BY T1 ASC, Sid1 ASC");
        }

        var nullSortColumns = string.Concat(Enumerable.Repeat(", NULL", activeSortKeyCount));
        var matchSortColumnRefs = string.Concat(Enumerable.Range(0, activeSortKeyCount).Select(o => $", SortValue{o}"));

        var unionBlocks = new List<string>
        {
            $"SELECT T1, Sid1, CAST(1 AS bit) AS IsMatch, CAST(0 AS bit) AS IsPartial{matchSortColumnRefs} FROM {MatchPage}",
        };
        for (var i = 0; i < includes.Count; i++)
        {
            unionBlocks.Add(
                $"SELECT i.T1, i.Sid1, CAST(0 AS bit), i.IsPartial{nullSortColumns} FROM {IncludeLimitLabel(i)} i\n" +
                $"WHERE NOT EXISTS (SELECT 1 FROM {MatchPage} m WHERE m.T1 = i.T1 AND m.Sid1 = i.Sid1)");
        }

        writer.Append(";WITH ");
        writer.AppendJoin(",\n", cteBlocks, CteLabel, SqlRangeKind.Cte);
        writer.Append(",\n");
        using (writer.Section(MatchPage, SqlRangeKind.MatchPage))
        {
            writer.Append(
                $"{MatchPage} AS (\n" +
                $"    SELECT {top}m.T1, m.Sid1{matchSortColumns}\n" +
                $"    FROM {CteLabel(plan.Match.Index)} m{matchSortJoins}{matchResourceJoin}");

            if (matchWhereClauses.Count > 0)
            {
                writer.Append("\n    WHERE ");
                using (writer.Section(Where, SqlRangeKind.Where))
                {
                    WriteAndJoinedClauses(writer, matchWhereClauses, matchSeekClauseIndex);
                }
            }

            writer.Append(cteOrderBy);
            if (plan.OffsetPage is { } matchOffsetPage)
            {
                writer.Append($"\n    OFFSET {EmitParam(new SqlParameterRef(matchOffsetPage.Offset), parameters)} ROWS FETCH NEXT {EmitParam(new SqlParameterRef(matchOffsetPage.Limit), parameters)} ROWS ONLY");
            }

            writer.Append("\n)");
        }

        for (var i = 0; i < includes.Count; i++)
        {
            writer.Append(",\n");
            using (writer.Section(IncludeLabel(i), SqlRangeKind.Include))
            {
                writer.Append($"{IncludeLabel(i)} AS (\n{incBlocks[i]}\n)");
            }

            writer.Append(",\n");
            using (writer.Section(IncludeLimitLabel(i), SqlRangeKind.IncludeLimit))
            {
                writer.Append($"{IncludeLimitLabel(i)} AS (\n{incLimBlocks[i]}\n)");
            }
        }

        writer.Append("\n");

        // The final UNION ALL stitches the match page to every include stage, so like the other
        // structural sections it belongs to no single plan row. Sectioned anyway: until it was, this
        // stretch carried no range at all and could not be addressed even as structure.
        using (writer.Section(Assembly, SqlRangeKind.Assembly))
        {
            writer.Append(string.Join("\nUNION ALL\n", unionBlocks));
        }

        writer.Append("\nORDER BY ");
        using (writer.Section(OrderBy, SqlRangeKind.OrderBy))
        {
            writer.Append(EmitOuterOrderByForIncludes(plan.Sort));
        }

        return new EmittedSql(writer.ToString(), parameters, writer.Ranges);
    }

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
    private static string EmitCte(CteDefinition cte, List<EmittedSqlParameter> parameters) => cte switch
    {
        CteDefinition.ParamSource p => EmitParamSource(p, parameters),
        CteDefinition.Intersect x =>
            $"    SELECT {CteLabel(x.Left.Index)}.T1, {CteLabel(x.Left.Index)}.Sid1\n" +
            $"    FROM {CteLabel(x.Left.Index)}\n" +
            $"    INNER JOIN {CteLabel(x.Right.Index)} ON {CteLabel(x.Left.Index)}.T1 = {CteLabel(x.Right.Index)}.T1 AND {CteLabel(x.Left.Index)}.Sid1 = {CteLabel(x.Right.Index)}.Sid1",
        CteDefinition.Union u =>
            string.Join("\n    UNION\n", u.Parts.Select(r => $"    SELECT T1, Sid1 FROM {CteLabel(r.Index)}")),
        CteDefinition.ResourceSource rs => EmitResourceSource(rs, parameters),
        CteDefinition.Except ex =>
            $"    SELECT {CteLabel(ex.Left.Index)}.T1, {CteLabel(ex.Left.Index)}.Sid1\n" +
            $"    FROM {CteLabel(ex.Left.Index)}\n" +
            $"    WHERE NOT EXISTS (\n" +
            $"        SELECT 1 FROM {CteLabel(ex.Right.Index)}\n" +
            $"        WHERE {CteLabel(ex.Right.Index)}.T1 = {CteLabel(ex.Left.Index)}.T1 AND {CteLabel(ex.Right.Index)}.Sid1 = {CteLabel(ex.Left.Index)}.Sid1)",
        CteDefinition.ChainJoin cj => EmitChainJoin(cj, parameters),
        CteDefinition.CompartmentSource cs => EmitCompartmentSource(cs, parameters),
        CteDefinition.NotReferencedSource nr => EmitNotReferencedSource(nr, parameters),
        CteDefinition.TableExistsPredicate tep => EmitTableExistsPredicate(tep, parameters),
        CteDefinition.VisibleSinceFilter vsf => EmitVisibleSinceFilter(vsf, parameters),
        CteDefinition.ReferencedTypeExpansion re => EmitReferencedTypeExpansion(re),
        _ => throw new NotSupportedException($"No Emit for {cte.GetType().Name}."),
    };

    /// <summary>Renders a ParamSource: distinct (type, surrogate id) rows from one search-param table filtered by SearchParamId and its optional predicate.</summary>
    private static string EmitParamSource(CteDefinition.ParamSource p, List<EmittedSqlParameter> parameters)
    {
        var predicateClause = p.Predicate is null ? string.Empty : $" AND {EmitPredicate(p.Predicate, parameters)}";

        // Most search-param tables hold rows for current versions only, so history is filtered once at
        // hydration. dbo.TokenText carries its own IsHistory column and does keep superseded rows, so a
        // query against it has to exclude them itself. Driven off the catalog rather than the table name:
        // the filter is required by any table that has the column, and the catalog is generated from DDL.
        var historyClause = p.Table.Columns.Any(c => c.Name == "IsHistory") ? " AND IsHistory = 0" : string.Empty;
        var typeFilter = p.ResourceTypeId is { } typeId ? $"ResourceTypeId = {typeId} AND " : string.Empty;

        return $"    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
               $"    FROM {p.Table.SchemaName}.{p.Table.TableName}\n" +
               $"    WHERE {typeFilter}SearchParamId = {p.SearchParamId}{historyClause}{predicateClause}";
    }

    /// <summary>Renders a chain as a join through dbo.ReferenceSearchParam and dbo.Resource, correlated to the inner match set, in the forward or reverse direction.</summary>
    private static string EmitChainJoin(CteDefinition.ChainJoin cj, List<EmittedSqlParameter> parameters)
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

        return cj.Direction switch
        {
            ChainDirection.Forward =>
                $"    SELECT DISTINCT rsp.ResourceTypeId AS T1, rsp.ResourceSurrogateId AS Sid1\n" +
                $"    FROM dbo.ReferenceSearchParam rsp\n" +
                $"    INNER JOIN dbo.Resource r\n" +
                $"        ON r.ResourceTypeId = rsp.ReferenceResourceTypeId\n" +
                $"       AND r.ResourceId = rsp.ReferenceResourceId\n" +
                $"       AND r.IsHistory = 0 AND r.IsDeleted = 0\n" +
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
                $"       AND r.IsHistory = 0 AND r.IsDeleted = 0\n" +
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
                "SortSpec.Phase == MissingPrimary with a LastUpdated (or otherwise SearchParamId-less) " +
                "primary key reached Emit -- _lastUpdated is a resource-column key derived from " +
                "ResourceSurrogateId, so it is never \"missing\" and has no MissingPrimary segment. " +
                "Lower.BuildSortSpec rejects this combination; QueryPlan is a public construction " +
                "surface, so this guard exists defensively rather than trusting every caller routes " +
                "through Lower.");
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
    /// (varchar for Token/Reference/Uri, decimal for Number/Quantity) -- see
    /// src/DataLayer/Ignixa.DataLayer.SqlServer.Database/Tables/{TokenSearchParam,NumberSearchParam,
    /// QuantitySearchParam,ReferenceSearchParam,UriSearchParam}.sql. nvarchar is included for parity with
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
            $"{SortValueExpr(sort!, i)} {(sort!.Keys[i].Direction == SortOrder.Ascending ? "ASC" : "DESC")}");
        return string.Join(", ", terms.Append("m.T1 ASC").Append("m.Sid1 ASC"));
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
    private static string EmitNotReferencedSource(CteDefinition.NotReferencedSource nr, List<EmittedSqlParameter> parameters)
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
               $"    WHERE r.ResourceTypeId = {EmitParam(new SqlParameterRef(nr.TargetResourceTypeId), parameters)} AND r.IsHistory = 0 AND r.IsDeleted = 0\n" +
               $"      AND NOT EXISTS (\n" +
               $"        SELECT 1\n" +
               $"        FROM dbo.ReferenceSearchParam rsp\n" +
               $"        WHERE rsp.ReferenceResourceId = r.ResourceId\n" +
               $"          AND rsp.ReferenceResourceTypeId = r.ResourceTypeId{innerFilters})";
    }

    /// <summary>Renders a TableExistsPredicate: distinct (type, surrogate id) rows from one raw table, with an optional additional predicate and no SearchParamId/ResourceTypeId filter.</summary>
    private static string EmitTableExistsPredicate(CteDefinition.TableExistsPredicate tep, List<EmittedSqlParameter> parameters)
    {
        var whereClause = tep.Predicate is not null
            ? $"\n    WHERE {EmitPredicate(tep.Predicate, parameters)}"
            : string.Empty;
        return
            $"    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            $"    FROM {tep.Table.SchemaName}.{tep.Table.TableName}{whereClause}";
    }

    /// <summary>Renders a VisibleSinceFilter: resources visible in a transaction on or after Since, joined through dbo.Resource and dbo.Transactions on VisibleDate.</summary>
    private static string EmitVisibleSinceFilter(CteDefinition.VisibleSinceFilter vsf, List<EmittedSqlParameter> parameters)
        => "    SELECT DISTINCT r.ResourceTypeId AS T1, r.ResourceSurrogateId AS Sid1\n" +
           "    FROM dbo.Resource r\n" +
           "    INNER JOIN dbo.Transactions t ON r.TransactionId = t.SurrogateIdRangeFirstValue\n" +
           $"    WHERE t.VisibleDate >= {EmitParam(vsf.Since, parameters)}";

    /// <summary>Renders a ReferencedTypeExpansion: the referenced resources reachable via any outbound internal reference from the seed set, restricted to the output resource types. Mirrors ChainJoin's reverse topology but with no SearchParamId/source-type filter (all reference parameters, any source type).</summary>
    private static string EmitReferencedTypeExpansion(CteDefinition.ReferencedTypeExpansion re)
        => $"    SELECT DISTINCT r.ResourceTypeId AS T1, r.ResourceSurrogateId AS Sid1\n" +
           $"    FROM dbo.ReferenceSearchParam rsp\n" +
           $"    INNER JOIN {CteLabel(re.Seed.Index)} m\n" +
           $"        ON m.T1 = rsp.ResourceTypeId AND m.Sid1 = rsp.ResourceSurrogateId\n" +
           $"    INNER JOIN dbo.Resource r\n" +
           $"        ON r.ResourceTypeId = rsp.ReferenceResourceTypeId\n" +
           $"       AND r.ResourceId = rsp.ReferenceResourceId\n" +
           $"       AND r.IsHistory = 0 AND r.IsDeleted = 0\n" +
           $"    WHERE {EmitTypeInFilter("rsp.ReferenceResourceTypeId", re.OutputResourceTypeIds)}\n" +
           $"      AND rsp.BaseUri IS NULL";

    /// <summary>Renders a ResourceSource: current, non-deleted rows of dbo.Resource for one type, with an optional nested-scope predicate.</summary>
    private static string EmitResourceSource(CteDefinition.ResourceSource rs, List<EmittedSqlParameter> parameters)
    {
        var predicateClause = rs.Predicate is null ? string.Empty : $" AND {EmitPredicate(rs.Predicate, parameters)}";
        var typeFilter = rs.ResourceTypeId is { } typeId ? $"ResourceTypeId = {EmitParam(new SqlParameterRef(typeId), parameters)} AND " : string.Empty;
        return $"    SELECT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
               $"    FROM dbo.Resource\n" +
               $"    WHERE {typeFilter}IsHistory = 0 AND IsDeleted = 0{predicateClause}";
    }

    /// <summary>Renders one include stage: the ReferenceSearchParam/Resource join for its direction, filtered by reference param and type ids, seeded from the match page and/or earlier stages via EXISTS. Selects TOP(Limit+1) to detect truncation.</summary>
    private static string EmitIncludeStage(IncludeStage stage)
    {
        var (selectColumns, seedTypeColumn, outputTypeColumn, seedCorrelationAlias) = stage.Direction switch
        {
            IncludeDirection.Forward => ("r.ResourceTypeId AS T1, r.ResourceSurrogateId AS Sid1", "rsp.ResourceTypeId", "r.ResourceTypeId", "rsp"),
            IncludeDirection.Reverse => ("rsp.ResourceTypeId AS T1, rsp.ResourceSurrogateId AS Sid1", "r.ResourceTypeId", "rsp.ResourceTypeId", "r"),
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

        return $"    SELECT DISTINCT TOP ({stage.Limit + 1}) {selectColumns}\n" +
               $"    FROM dbo.ReferenceSearchParam rsp\n" +
               $"    INNER JOIN dbo.Resource r\n" +
               $"        ON r.ResourceTypeId = rsp.ReferenceResourceTypeId\n" +
               $"       AND r.ResourceId = rsp.ReferenceResourceId\n" +
               $"       AND r.IsHistory = 0 AND r.IsDeleted = 0\n" +
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
