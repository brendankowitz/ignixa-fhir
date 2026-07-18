#pragma warning disable CA1724

using Ignixa.Search.Expressions;

namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// Turns a QueryPlan into parameterized T-SQL text -- deterministic (same plan -> byte-identical SQL).
/// Every CteDefinition entry, including Intersect/Union, becomes its own named CTE, so Match can point
/// at any depth of nesting without special-casing the outer SELECT. No user value is ever inlined into
/// SQL text -- every SqlParameterRef becomes a named parameter (see design doc's AST invariant).
/// </summary>
public static class Emit
{
    public static EmittedSql Run(QueryPlan plan)
    {
        var parameters = new List<EmittedSqlParameter>();
        var cteBlocks = new List<string>();

        for (var i = 0; i < plan.Ctes.Count; i++)
        {
            cteBlocks.Add($"cte{i} AS (\n{EmitCte(plan.Ctes[i], parameters)}\n)");
        }

        var top = plan.Top is { } n ? $"TOP ({n}) " : string.Empty;

        if (plan.Includes is not { Count: > 0 } includes)
        {
            var withClause = $";WITH {string.Join(",\n", cteBlocks)}\n";
            var sortJoins = EmitSortJoins(plan.Sort);
            var sortColumns = EmitSortSelectColumns(plan.Sort);
            var orderBy = $"\nORDER BY {EmitOrderBy(plan.Sort)}";

            var whereClauses = new List<string>();
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
                whereClauses.Add(EmitSeekPredicate(plan.Sort, page, parameters));
            }

            string sql;
            if (whereClauses.Count == 0)
            {
                sql = withClause + $"SELECT {top}m.T1, m.Sid1{sortColumns} FROM cte{plan.Match.Index} m{sortJoins}{orderBy}";
            }
            else
            {
                var resourceJoin = plan.OuterPredicate is null
                    ? string.Empty
                    : "\nINNER JOIN dbo.Resource r ON r.ResourceTypeId = m.T1 AND r.ResourceSurrogateId = m.Sid1";
                sql = withClause +
                      $"SELECT {top}m.T1, m.Sid1{sortColumns} FROM cte{plan.Match.Index} m{sortJoins}{resourceJoin}\n" +
                      $"WHERE {string.Join(" AND ", whereClauses)}{orderBy}";
            }

            return new EmittedSql(sql, parameters);
        }

        var matchSortJoins = EmitSortJoins(plan.Sort);
        var matchSortColumns = EmitSortSelectColumns(plan.Sort);
        var activeSortKeyCount = ActiveKeyIndices(plan.Sort).Count;
        var cteOrderBy = $"\n    ORDER BY {EmitOrderBy(plan.Sort)}";

        var matchWhereClauses = new List<string>();
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
            matchWhereClauses.Add(EmitSeekPredicate(plan.Sort, matchPage, parameters));
        }

        var matchResourceJoin = plan.OuterPredicate is null
            ? string.Empty
            : "\n    INNER JOIN dbo.Resource r ON r.ResourceTypeId = m.T1 AND r.ResourceSurrogateId = m.Sid1";
        var matchWhere = matchWhereClauses.Count > 0
            ? $"\n    WHERE {string.Join(" AND ", matchWhereClauses)}"
            : string.Empty;

        cteBlocks.Add(
            $"cteMatchPage AS (\n" +
            $"    SELECT {top}m.T1, m.Sid1{matchSortColumns}\n" +
            $"    FROM cte{plan.Match.Index} m{matchSortJoins}{matchResourceJoin}{matchWhere}{cteOrderBy}\n" +
            $")");

        for (var i = 0; i < includes.Count; i++)
        {
            var stage = includes[i];
            cteBlocks.Add($"inc{i} AS (\n{EmitIncludeStage(stage)}\n)");
            cteBlocks.Add(
                $"inc{i}lim AS (\n" +
                $"    SELECT TOP ({stage.Limit}) T1, Sid1,\n" +
                $"           CASE WHEN COUNT_BIG(*) OVER() > {stage.Limit} THEN 1 ELSE 0 END AS IsPartial\n" +
                $"    FROM inc{i}\n" +
                $")");
        }

        var nullSortColumns = string.Concat(Enumerable.Repeat(", NULL", activeSortKeyCount));
        var matchSortColumnRefs = string.Concat(Enumerable.Range(0, activeSortKeyCount).Select(o => $", SortValue{o}"));

        var unionBlocks = new List<string>
        {
            $"SELECT T1, Sid1, CAST(1 AS bit) AS IsMatch, CAST(0 AS bit) AS IsPartial{matchSortColumnRefs} FROM cteMatchPage",
        };
        for (var i = 0; i < includes.Count; i++)
        {
            unionBlocks.Add(
                $"SELECT i.T1, i.Sid1, CAST(0 AS bit), i.IsPartial{nullSortColumns} FROM inc{i}lim i\n" +
                $"WHERE NOT EXISTS (SELECT 1 FROM cteMatchPage m WHERE m.T1 = i.T1 AND m.Sid1 = i.Sid1)");
        }

        var includeSql = $";WITH {string.Join(",\n", cteBlocks)}\n" +
                          $"{string.Join("\nUNION ALL\n", unionBlocks)}\n" +
                          $"ORDER BY {EmitOuterOrderByForIncludes(plan.Sort)}";

        return new EmittedSql(includeSql, parameters);
    }

    private static string EmitCte(CteDefinition cte, List<EmittedSqlParameter> parameters) => cte switch
    {
        CteDefinition.ParamSource p => EmitParamSource(p, parameters),
        CteDefinition.Intersect x =>
            $"    SELECT cte{x.Left.Index}.T1, cte{x.Left.Index}.Sid1\n" +
            $"    FROM cte{x.Left.Index}\n" +
            $"    INNER JOIN cte{x.Right.Index} ON cte{x.Left.Index}.T1 = cte{x.Right.Index}.T1 AND cte{x.Left.Index}.Sid1 = cte{x.Right.Index}.Sid1",
        CteDefinition.Union u =>
            string.Join("\n    UNION\n", u.Parts.Select(r => $"    SELECT T1, Sid1 FROM cte{r.Index}")),
        CteDefinition.ResourceSource rs => EmitResourceSource(rs, parameters),
        CteDefinition.Except ex =>
            $"    SELECT cte{ex.Left.Index}.T1, cte{ex.Left.Index}.Sid1\n" +
            $"    FROM cte{ex.Left.Index}\n" +
            $"    WHERE NOT EXISTS (\n" +
            $"        SELECT 1 FROM cte{ex.Right.Index}\n" +
            $"        WHERE cte{ex.Right.Index}.T1 = cte{ex.Left.Index}.T1 AND cte{ex.Right.Index}.Sid1 = cte{ex.Left.Index}.Sid1)",
        CteDefinition.ChainJoin cj => EmitChainJoin(cj, parameters),
        CteDefinition.CompartmentSource cs => EmitCompartmentSource(cs, parameters),
        _ => throw new NotSupportedException($"No Emit for {cte.GetType().Name}."),
    };

    private static string EmitParamSource(CteDefinition.ParamSource p, List<EmittedSqlParameter> parameters)
        => $"    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
           $"    FROM {p.Table.SchemaName}.{p.Table.TableName}\n" +
           $"    WHERE ResourceTypeId = {p.ResourceTypeId} AND SearchParamId = {p.SearchParamId} AND {EmitPredicate(p.Predicate, parameters)}";

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
                $"    INNER JOIN cte{cj.InnerMatch.Index} m\n" +
                $"        ON m.T1 = r.ResourceTypeId AND m.Sid1 = r.ResourceSurrogateId\n" +
                $"    WHERE rsp.SearchParamId = {cj.ReferenceSearchParamId}\n" +
                $"      AND rsp.ReferenceResourceTypeId = {cj.InnerResourceTypeId}\n" +
                $"      AND {outputFilter}\n" +
                $"      AND rsp.BaseUri IS NULL",
            ChainDirection.Reverse =>
                $"    SELECT DISTINCT r.ResourceTypeId AS T1, r.ResourceSurrogateId AS Sid1\n" +
                $"    FROM dbo.ReferenceSearchParam rsp\n" +
                $"    INNER JOIN cte{cj.InnerMatch.Index} m\n" +
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

    private static string OutputTypeColumn(ChainDirection direction) => direction switch
    {
        ChainDirection.Forward => "rsp.ResourceTypeId",
        ChainDirection.Reverse => "rsp.ReferenceResourceTypeId",
        _ => throw new NotSupportedException($"Unknown ChainDirection '{direction}'."),
    };

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

    private static string EmitMissingPrimaryFilter(SortSpec sort)
    {
        var key = sort.Keys[0];
        var table = key.Kind == SortKeyKind.String ? "StringSearchParam" : "DateTimeSearchParam";
        return $"NOT EXISTS (SELECT 1 FROM dbo.{table} s WHERE s.ResourceTypeId = m.T1 AND s.ResourceSurrogateId = m.Sid1 AND s.SearchParamId = {key.SearchParamId})";
    }

    private static IReadOnlyList<int> ActiveKeyIndices(SortSpec? sort)
        => sort is null
            ? []
            : sort.Phase == SortPhase.Valued
                ? Enumerable.Range(0, sort.Keys.Count).ToList()
                : Enumerable.Range(1, sort.Keys.Count - 1).ToList();

    /// <summary>
    /// The F1 invariant (design §3.3): this is the ONLY place a sort key's value expression is
    /// rendered. EmitOrderBy and EmitSeekPredicate both call this -- neither hand-writes an
    /// ISNULL(...)/sentinel string independently, so the ORDER BY and seek-predicate expressions for
    /// a given key can never drift out of sync.
    /// </summary>
    private static string SortValueExpr(SortSpec sort, int index)
    {
        var key = sort.Keys[index];
        if (key.Kind == SortKeyKind.LastUpdated)
        {
            return "m.Sid1";
        }

        var column = key.Kind == SortKeyKind.String ? "Text" : "StartDateTime";
        var raw = $"sk{index}.{column}";

        var isGuaranteedNonNull = index == 0 && sort.Phase == SortPhase.Valued;
        if (isGuaranteedNonNull)
        {
            return raw;
        }

        var sentinel = key.Kind == SortKeyKind.String ? "N''" : "'0001-01-01T00:00:00.0000000'";
        return $"ISNULL({raw}, {sentinel})";
    }

    private static string EmitOrderBy(SortSpec? sort)
    {
        var activeIndices = ActiveKeyIndices(sort);
        var terms = activeIndices.Select(i =>
            $"{SortValueExpr(sort!, i)} {(sort!.Keys[i].Direction == SortOrder.Ascending ? "ASC" : "DESC")}");
        return string.Join(", ", terms.Append("m.T1 ASC").Append("m.Sid1 ASC"));
    }

    private static string EmitOuterOrderByForIncludes(SortSpec? sort)
    {
        var activeIndices = ActiveKeyIndices(sort);
        var terms = activeIndices.Select((idx, ordinal) =>
            $"SortValue{ordinal} {(sort!.Keys[idx].Direction == SortOrder.Ascending ? "ASC" : "DESC")}");
        return string.Join(", ", terms.Prepend("IsMatch DESC").Append("T1 ASC").Append("Sid1 ASC"));
    }

    private static string EmitSortSelectColumns(SortSpec? sort)
    {
        var activeIndices = ActiveKeyIndices(sort);
        return activeIndices.Count == 0
            ? string.Empty
            : ", " + string.Join(", ", activeIndices.Select((idx, ordinal) => $"{SortValueExpr(sort!, idx)} AS SortValue{ordinal}"));
    }

    private static string EmitSeekPredicate(SortSpec? sort, PageSpec page, List<EmittedSqlParameter> parameters)
    {
        var activeIndices = ActiveKeyIndices(sort);
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

        return branches.Count == 1 ? branches[0] : string.Join("\n       OR ", branches);
    }

    private static string EmitCompartmentSource(CteDefinition.CompartmentSource cs, List<EmittedSqlParameter> parameters)
        => $"    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
           $"    FROM dbo.ReferenceSearchParam\n" +
           $"    WHERE SearchParamId = {cs.SearchParamId}\n" +
           $"      AND {EmitTypeInFilter("ResourceTypeId", cs.ResourceTypeIds)}\n" +
           $"      AND {EmitPredicate(cs.Predicate, parameters)}";

    private static string EmitResourceSource(CteDefinition.ResourceSource rs, List<EmittedSqlParameter> parameters)
    {
        var predicateClause = rs.Predicate is null ? string.Empty : $" AND {EmitPredicate(rs.Predicate, parameters)}";
        return $"    SELECT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
               $"    FROM dbo.Resource\n" +
               $"    WHERE ResourceTypeId = {EmitParam(new SqlParameterRef(rs.ResourceTypeId), parameters)} AND IsHistory = 0 AND IsDeleted = 0{predicateClause}";
    }

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
               $"    WHERE {string.Join("\n      AND ", whereClauses)}";
    }

    private static string EmitTypeInFilter(string column, IReadOnlyList<short> typeIds)
    {
        var filter = string.Join(" OR ", typeIds.Select(id => $"{column} = {id}"));
        return typeIds.Count > 1 ? $"({filter})" : filter;
    }

    private static string EmitSeedExists(IncludeStage stage, string correlationAlias)
    {
        var branches = new List<string>();
        if (stage.SeedFromMatch)
        {
            branches.Add($"SELECT 1 FROM cteMatchPage m WHERE m.T1 = {correlationAlias}.ResourceTypeId AND m.Sid1 = {correlationAlias}.ResourceSurrogateId");
        }

        foreach (var seedStageIndex in stage.SeedStages)
        {
            branches.Add($"SELECT 1 FROM inc{seedStageIndex}lim m WHERE m.T1 = {correlationAlias}.ResourceTypeId AND m.Sid1 = {correlationAlias}.ResourceSurrogateId");
        }

        return $"EXISTS (\n        {string.Join("\n        UNION ALL\n        ", branches)}\n    )";
    }

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
        _ => throw new NotSupportedException($"No Emit for {predicate.GetType().Name}."),
    };

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

    private static string EmitParam(SqlParameterRef value, List<EmittedSqlParameter> parameters)
    {
        var name = $"@p{parameters.Count}";
        parameters.Add(new EmittedSqlParameter(name, value.Value));
        return name;
    }

    private static string EmitCollation(string? collation) => collation is null ? string.Empty : $" COLLATE {collation}";
}
