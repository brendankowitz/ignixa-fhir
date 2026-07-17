#pragma warning disable CA1724

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
        var withClause = $";WITH {string.Join(",\n", cteBlocks)}\n";
        var sql = plan.OuterPredicate is null
            ? withClause + $"SELECT {top}T1, Sid1 FROM cte{plan.Match.Index}"
            : withClause +
              $"SELECT {top}m.T1, m.Sid1 FROM cte{plan.Match.Index} m\n" +
              $"INNER JOIN dbo.Resource r ON r.ResourceTypeId = m.T1 AND r.ResourceSurrogateId = m.Sid1\n" +
              $"WHERE {EmitPredicate(plan.OuterPredicate, parameters)}";

        return new EmittedSql(sql, parameters);
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
        _ => throw new NotSupportedException($"No Emit for {cte.GetType().Name}."),
    };

    private static string EmitParamSource(CteDefinition.ParamSource p, List<EmittedSqlParameter> parameters)
        => $"    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
           $"    FROM {p.Table.SchemaName}.{p.Table.TableName}\n" +
           $"    WHERE ResourceTypeId = {p.ResourceTypeId} AND SearchParamId = {p.SearchParamId} AND {EmitPredicate(p.Predicate, parameters)}";

    private static string EmitResourceSource(CteDefinition.ResourceSource rs, List<EmittedSqlParameter> parameters)
        => $"    SELECT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
           $"    FROM dbo.Resource\n" +
           $"    WHERE ResourceTypeId = {EmitParam(new SqlParameterRef(rs.ResourceTypeId), parameters)} AND IsHistory = 0 AND IsDeleted = 0";

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
