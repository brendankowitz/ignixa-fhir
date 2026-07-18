using System.Text;
using Ignixa.Search.Expressions;

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
            var line = $"{label} = {PrintCte(plan.Ctes[i], top, ref parameterOrdinal)}";
            if (isRoot && plan.OuterPredicate is not null)
            {
                line += $" WHERE {PrintPredicate(plan.OuterPredicate, ref parameterOrdinal)}";
            }

            lines.Add(line);
        }

        if (plan.Includes is { Count: > 0 } includes)
        {
            for (var i = 0; i < includes.Count; i++)
            {
                lines.Add($"inc{i} = {PrintIncludeStage(includes[i])}");
            }
        }

        if (plan.Sort is { } sort)
        {
            lines.Add($"sort = {PrintSortSpec(sort)}");
        }

        if (plan.Page is { } page)
        {
            lines.Add($"page = {PrintPageSpec(page, ref parameterOrdinal)}");
        }

        return string.Join('\n', lines);
    }

    private static string PrintSortSpec(SortSpec sort)
    {
        var keys = sort.Keys.Select(k =>
            $"{k.Kind}:{(k.SearchParamId is { } id ? id.ToString(System.Globalization.CultureInfo.InvariantCulture) : "-")} {(k.Direction == SortOrder.Ascending ? "ASC" : "DESC")}");
        return $"SortSpec([{string.Join(", ", keys)}], {sort.Phase})";
    }

    private static string PrintPageSpec(PageSpec page, ref int parameterOrdinal)
    {
        var boundary = new List<string>();
        for (var i = 0; i < page.Boundary.Count; i++)
        {
            boundary.Add($"@p{parameterOrdinal++}");
        }

        var typeParam = $"@p{parameterOrdinal++}";
        var sidParam = $"@p{parameterOrdinal++}";
        return $"PageSpec(boundary=[{string.Join(",", boundary)}], type={typeParam}, sid={sidParam})";
    }

    private static string PrintIncludeStage(IncludeStage stage)
    {
        var refParam = stage.ReferenceSearchParamId is { } id ? $"{id}" : "*";
        var seedTypes = stage.SeedTypeIds is null ? "*" : $"[{string.Join(",", stage.SeedTypeIds)}]";
        var outputTypes = stage.OutputTypeIds is null ? "*" : $"[{string.Join(",", stage.OutputTypeIds)}]";
        var seedStageLabels = stage.SeedStages.Select(s => $"inc{s}");
        var seeds = stage.SeedFromMatch ? seedStageLabels.Prepend("match") : seedStageLabels;
        var iterate = stage.Iterate ? " iterate" : string.Empty;
        return $"IncludeStage(ref={refParam}, seedTypes={seedTypes}, outputTypes={outputTypes}, seeds=[{string.Join(",", seeds)}], limit={stage.Limit}{iterate}, {stage.Direction})";
    }

    private static string PrintCte(CteDefinition cte, int? top, ref int parameterOrdinal) => cte switch
    {
        CteDefinition.ParamSource p =>
            $"{p.Table.TableName}[{p.ResourceTypeId},{p.SearchParamId}]  {PrintPredicate(p.Predicate, ref parameterOrdinal)}{PrintTop(top)}",
        CteDefinition.Intersect x =>
            $"Intersect(cte{x.Left.Index}, cte{x.Right.Index}){PrintTop(top)}",
        CteDefinition.Union u =>
            $"Union({string.Join(", ", u.Parts.Select(r => $"cte{r.Index}"))}){PrintTop(top)}",
        CteDefinition.ResourceSource rs => PrintResourceSource(rs, top, ref parameterOrdinal),
        CteDefinition.Except ex => $"Except(cte{ex.Left.Index}, cte{ex.Right.Index}){PrintTop(top)}",
        CteDefinition.ChainJoin cj =>
            $"ChainJoin(cte{cj.InnerMatch.Index}, ref={cj.ReferenceSearchParamId}, inner={cj.InnerResourceTypeId}, output=[{string.Join(",", cj.OutputResourceTypeIds)}], {cj.Direction}){PrintTop(top)}",
        CteDefinition.CompartmentSource cs =>
            $"CompartmentSource[{string.Join(",", cs.ResourceTypeIds)},{cs.SearchParamId}]  {PrintPredicate(cs.Predicate, ref parameterOrdinal)}{PrintTop(top)}",
        _ => throw new NotSupportedException($"No Explain() rendering for {cte.GetType().Name}."),
    };

    private static string PrintResourceSource(CteDefinition.ResourceSource rs, int? top, ref int parameterOrdinal)
    {
        // ResourceTypeId is a real bound parameter in Emit (EmitResourceSource), so this must consume
        // an ordinal too -- otherwise Explain()'s @pN numbering silently diverges from the emitted
        // SQL's real parameter numbering for any plan mixing a ResourceSource with another
        // parameterized CTE or an OuterPredicate. The literal ResourceTypeId is still shown inline
        // (not "@pN") because it reads better in a human-facing summary; only the counter is shared.
        // rs.Predicate (nested-scope resource-column filter, e.g. a chain target's _id=X), when
        // present, is a real predicate rendered the same way OuterPredicate is -- it also consumes
        // whatever ordinals PrintPredicate consumes internally.
        parameterOrdinal++;
        var predicateSuffix = rs.Predicate is null ? string.Empty : $" WHERE {PrintPredicate(rs.Predicate, ref parameterOrdinal)}";
        return $"ResourceSource[{rs.ResourceTypeId}]{predicateSuffix}{PrintTop(top)}";
    }

    private static string PrintPredicate(Predicate predicate, ref int parameterOrdinal) => predicate switch
    {
        Predicate.Equal e => $"{e.Column.Column} = @p{parameterOrdinal++}{PrintCollation(e.Collation)}",
        Predicate.Like l => $"{l.Column.Column} LIKE @p{parameterOrdinal++} ({l.Match}){PrintCollation(l.Collation)}",
        Predicate.And a => $"{PrintPredicate(a.Left, ref parameterOrdinal)} AND {PrintPredicate(a.Right, ref parameterOrdinal)}",
        Predicate.LessThan lt => $"{lt.Column.Column} < @p{parameterOrdinal++}",
        Predicate.LessThanOrEqual le => $"{le.Column.Column} <= @p{parameterOrdinal++}",
        Predicate.GreaterThan gt => $"{gt.Column.Column} > @p{parameterOrdinal++}",
        Predicate.GreaterThanOrEqual ge => $"{ge.Column.Column} >= @p{parameterOrdinal++}",
        Predicate.Or or => $"{PrintPredicate(or.Left, ref parameterOrdinal)} OR {PrintPredicate(or.Right, ref parameterOrdinal)}",
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
