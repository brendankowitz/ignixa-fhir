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
        var sql = $";WITH {string.Join(",\n", cteBlocks)}\n" +
                  $"SELECT {top}T1, Sid1 FROM cte{plan.Match.Index}";

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
        _ => throw new NotSupportedException($"No Emit for {cte.GetType().Name}."),
    };

    private static string EmitParamSource(CteDefinition.ParamSource p, List<EmittedSqlParameter> parameters)
        => $"    SELECT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
           $"    FROM {p.Table.SchemaName}.{p.Table.TableName}\n" +
           $"    WHERE SearchParamId = {p.SearchParamId} AND {EmitPredicate(p.Predicate, parameters)}";

    private static string EmitPredicate(Predicate predicate, List<EmittedSqlParameter> parameters) => predicate switch
    {
        Predicate.Equal e => $"{e.Column.Column} = {EmitParam(e.Value, parameters)}{EmitCollation(e.Collation)}",
        Predicate.Like l => $"{l.Column.Column} LIKE {EmitParam(EscapeLike(l), parameters)} ESCAPE '\\'{EmitCollation(l.Collation)}",
        Predicate.And a => $"({EmitPredicate(a.Left, parameters)} AND {EmitPredicate(a.Right, parameters)})",
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
