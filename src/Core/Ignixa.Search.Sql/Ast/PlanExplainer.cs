using System.Text;

namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// Renders a QueryPlan as human-readable text -- the compiler's plan-shape golden-test format.
/// Read-only by design: no parser, no round-trip (design doc's Explain() rationale -- a parseable
/// plan DSL would need a printer AND a parser, and would import SQL's semantics into a FHIR-meaning
/// layer for no benefit).
/// </summary>
public static class PlanExplainer
{
    public static string Print(QueryPlan plan)
    {
        var lines = new List<string>();
        var parameterOrdinal = 0;

        for (var i = 0; i < plan.Ctes.Count; i++)
        {
            var isRoot = i == plan.Match.Index;
            var label = isRoot ? "root" : $"cte{i}";
            var top = isRoot ? plan.Top : null;
            lines.Add($"{label} = {PrintCte(plan.Ctes[i], top, ref parameterOrdinal)}");
        }

        return string.Join('\n', lines);
    }

    private static string PrintCte(CteDefinition cte, int? top, ref int parameterOrdinal) => cte switch
    {
        CteDefinition.ParamSource p =>
            $"{p.Table.TableName}[{p.SearchParamId}]  {PrintPredicate(p.Predicate, ref parameterOrdinal)}{PrintTop(top)}",
        CteDefinition.Intersect x =>
            $"Intersect(cte{x.Left.Index}, cte{x.Right.Index}){PrintTop(top)}",
        CteDefinition.Union u =>
            $"Union({string.Join(", ", u.Parts.Select(r => $"cte{r.Index}"))}){PrintTop(top)}",
        _ => throw new NotSupportedException($"No Explain() rendering for {cte.GetType().Name}."),
    };

    private static string PrintPredicate(Predicate predicate, ref int parameterOrdinal) => predicate switch
    {
        Predicate.Equal e => $"{e.Column.Column} = @p{parameterOrdinal++}{PrintCollation(e.Collation)}",
        Predicate.Like l => $"{l.Column.Column} LIKE @p{parameterOrdinal++} ({l.Match}){PrintCollation(l.Collation)}",
        Predicate.And a => $"{PrintPredicate(a.Left, ref parameterOrdinal)} AND {PrintPredicate(a.Right, ref parameterOrdinal)}",
        _ => throw new NotSupportedException($"No Explain() rendering for {predicate.GetType().Name}."),
    };

    private static string PrintCollation(string? collation)
    {
        if (collation is null) return string.Empty;
        if (collation.EndsWith("_CS_AS", StringComparison.Ordinal)) return " collate CS_AS";
        if (collation.EndsWith("_CI_AI", StringComparison.Ordinal)) return " collate CI_AI";
        return $" collate {collation}";
    }

    private static string PrintTop(int? top) => top is null ? string.Empty : $" top {top}";
}
