using Ignixa.Search.Sql.Ast;

namespace Ignixa.Search.Sql.Builders;

/// <summary>Emits predicate trees as WHERE fragments and binds the @pN parameters they reference.</summary>
internal static class PredicateEmitter
{
    /// <summary>Renders a predicate tree to a WHERE fragment, fully parenthesizing And/Or so precedence never depends on context.</summary>
    /// <param name="predicate">The predicate tree to render.</param>
    /// <param name="parameters">Accumulates the bound parameters the fragment references.</param>
    /// <param name="qualifier">
    /// Alias prefix (including the dot) before every column, or empty for a single-table CTE body. The outer
    /// query must qualify with <c>r.</c> because the resource join and a ResourceId sort join (rid0) are both
    /// dbo.Resource — an unqualified column is ambiguous (Msg 209).
    /// </param>
    internal static string EmitPredicate(Predicate predicate, List<EmittedSqlParameter> parameters, string qualifier = "") => predicate switch
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
    /// The alias the outer query's <c>dbo.Resource</c> join uses. <see cref="ShapeEmitter.NeedsResourceJoin"/> guarantees
    /// the join exists whenever an outer predicate does, so qualifying with it is always valid and unambiguous.
    /// </summary>
    internal const string ResourceJoinQualifier = "r.";

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
    internal static string EmitParam(SqlParameterRef value, List<EmittedSqlParameter> parameters)
    {
        var name = $"@p{parameters.Count}";
        parameters.Add(new EmittedSqlParameter(name, value.Value));
        return name;
    }

    /// <summary>Renders a " COLLATE ..." suffix, or empty when the predicate has no explicit collation.</summary>
    private static string EmitCollation(string? collation) => collation is null ? string.Empty : $" COLLATE {collation}";
}
