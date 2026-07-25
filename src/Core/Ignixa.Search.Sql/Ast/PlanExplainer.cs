using System.Text;
using Ignixa.Search.Expressions;
using static Ignixa.Search.Sql.Builders.SqlLabels;

namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// Renders a <see cref="QueryPlan"/> as human-readable text — the compiler's plan-shape golden-test
/// format. One-way by design: there is no parser and no round-trip.
/// </summary>
public static class PlanExplainer
{
    public static string Print(QueryPlan plan) => Print(Describe(plan));

    /// <summary>
    /// The flat text for rows already described. Callers holding both the rows and the printed form take
    /// this overload so <see cref="Describe"/> runs once — the two can then never disagree.
    /// </summary>
    public static string Print(IReadOnlyList<PlanExplainRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        return string.Join('\n', rows.Select(row => $"{row.Label} = {row.Body}"));
    }

    /// <summary>
    /// Renders the same content <see cref="Print"/> does, one <see cref="PlanExplainRow"/> per line, with
    /// the label kept apart from the body. Tooling needs the label to address a row (and to join it to the
    /// owning parameter via <see cref="Tracing.CteProvenance.CteIndex"/>); <see cref="Print"/> is defined in
    /// terms of this method so the two can never disagree.
    /// </summary>
    /// <remarks>
    /// Every row carries both the name it displays and the identifier it is addressable by — see
    /// <see cref="PlanExplainRow.CanonicalLabel"/>. The match CTE is the row where those differ.
    /// </remarks>
    public static IReadOnlyList<PlanExplainRow> Describe(QueryPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var rows = new List<PlanExplainRow>();

        // Shared across every body that binds parameters, in traversal order: @pN numbering here must
        // match the emitted SQL's, so no row may render its body out of sequence.
        var parameterOrdinal = 0;

        for (var i = 0; i < plan.Ctes.Count; i++)
        {
            var isRoot = i == plan.Match.Index;

            // The match CTE prints as "root" because that reads better in a plan dump, but it is emitted
            // as cte{i} like every other CTE. Carrying both keeps the display name from being mistaken
            // for something a consumer can join on.
            var label = isRoot ? "root" : CteLabel(i);
            var top = isRoot ? plan.Top : null;
            var body = PrintCte(plan.Ctes[i], top, ref parameterOrdinal);
            if (isRoot && plan.OuterPredicate is not null)
            {
                body += $" WHERE {PrintPredicate(plan.OuterPredicate, ref parameterOrdinal)}";
            }

            rows.Add(new PlanExplainRow(
                label, CteLabel(i), KindOf(plan.Ctes[i]), body, ReferencedCteIndexesOf(plan.Ctes[i])));
        }

        if (plan.Includes is { Count: > 0 } includes)
        {
            for (var i = 0; i < includes.Count; i++)
            {
                rows.Add(new PlanExplainRow(
                    IncludeLabel(i), IncludeLabel(i), PlanRowKind.IncludeStage, PrintIncludeStage(includes[i]), []));
            }
        }

        if (plan.Sort is { } sort)
        {
            rows.Add(new PlanExplainRow("sort", "sort", PlanRowKind.SortSpec, PrintSortSpec(sort), []));
        }

        if (plan.Page is { } page)
        {
            rows.Add(new PlanExplainRow(
                "page", "page", PlanRowKind.PageSpec, PrintPageSpec(page, ref parameterOrdinal), []));
        }

        if (plan.CountOnly)
        {
            rows.Add(new PlanExplainRow("countOnly", "countOnly", PlanRowKind.CountOnly, "true", []));
        }

        return rows;
    }

    /// <summary>
    /// Which <see cref="CteDefinition"/> case produced a row. Deliberately a separate switch from
    /// <see cref="PrintCte"/> rather than a tuple returned alongside the body: the two answer different
    /// questions (what it is vs how it reads), and the compiler still flags either one if a case is added.
    /// </summary>
    private static string KindOf(CteDefinition cte) => cte switch
    {
        CteDefinition.ParamSource => PlanRowKind.ParamSource,
        CteDefinition.Intersect => PlanRowKind.Intersect,
        CteDefinition.Union => PlanRowKind.Union,
        CteDefinition.ResourceSource => PlanRowKind.ResourceSource,
        CteDefinition.Except => PlanRowKind.Except,
        CteDefinition.ChainJoin => PlanRowKind.ChainJoin,
        CteDefinition.CompartmentSource => PlanRowKind.CompartmentSource,
        CteDefinition.NotReferencedSource => PlanRowKind.NotReferencedSource,
        CteDefinition.MultiTypeResourceSource => PlanRowKind.MultiTypeResourceSource,
        _ => throw new NotSupportedException($"No Explain() kind for {cte.GetType().Name}."),
    };

    /// <summary>
    /// The CTEs a structural node composes, in the order it names them — the same fields
    /// <see cref="PrintCte"/> renders into the body, kept as data so consumers need not parse it back out.
    /// </summary>
    /// <remarks>
    /// The leaf sources are listed explicitly rather than caught by a default arm, and an unknown case
    /// throws like <see cref="KindOf"/> does. A silent <c>[]</c> here would let a new composing
    /// <see cref="CteDefinition"/> report no children, which
    /// <see cref="Tracing.SearchCompiler"/> would turn into silently-wrong parameter provenance with
    /// nothing to fail.
    /// </remarks>
    internal static IReadOnlyList<int> ReferencedCteIndexesOf(CteDefinition cte) => cte switch
    {
        CteDefinition.Intersect x => [x.Left.Index, x.Right.Index],
        CteDefinition.Union u => [.. u.Parts.Select(r => r.Index)],
        CteDefinition.Except ex => [ex.Left.Index, ex.Right.Index],
        CteDefinition.ChainJoin cj => [cj.InnerMatch.Index],
        CteDefinition.ParamSource or CteDefinition.ResourceSource or CteDefinition.CompartmentSource or CteDefinition.NotReferencedSource or CteDefinition.MultiTypeResourceSource => [],
        _ => throw new NotSupportedException($"No Explain() CTE references for {cte.GetType().Name}."),
    };

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
        var seedStageLabels = stage.SeedStages.Select(IncludeLabel);
        var seeds = stage.SeedFromMatch ? seedStageLabels.Prepend("match") : seedStageLabels;
        var iterate = stage.Iterate ? " iterate" : string.Empty;
        return $"IncludeStage(ref={refParam}, seedTypes={seedTypes}, outputTypes={outputTypes}, seeds=[{string.Join(",", seeds)}], limit={stage.Limit}{iterate}, {stage.Direction})";
    }

    private static string PrintCte(CteDefinition cte, int? top, ref int parameterOrdinal) => cte switch
    {
        CteDefinition.ParamSource p =>
            $"{p.Table.TableName}[{p.ResourceTypeId},{p.SearchParamId}]{(p.Predicate is null ? string.Empty : $"  {PrintPredicate(p.Predicate, ref parameterOrdinal)}")}{PrintTop(top)}",
        CteDefinition.Intersect x =>
            $"Intersect({CteLabel(x.Left.Index)}, {CteLabel(x.Right.Index)}){PrintTop(top)}",
        CteDefinition.Union u =>
            $"Union({string.Join(", ", u.Parts.Select(r => CteLabel(r.Index)))}){PrintTop(top)}",
        CteDefinition.ResourceSource rs => PrintResourceSource(rs, top, ref parameterOrdinal),
        CteDefinition.Except ex => $"Except({CteLabel(ex.Left.Index)}, {CteLabel(ex.Right.Index)}){PrintTop(top)}",
        CteDefinition.ChainJoin cj =>
            $"ChainJoin({CteLabel(cj.InnerMatch.Index)}, ref={cj.ReferenceSearchParamId}, inner={cj.InnerResourceTypeId}, output=[{string.Join(",", cj.OutputResourceTypeIds)}], {cj.Direction}){PrintTop(top)}",
        CteDefinition.CompartmentSource cs =>
            $"CompartmentSource[{string.Join(",", cs.ResourceTypeIds)},{cs.SearchParamId}]  {PrintPredicate(cs.Predicate, ref parameterOrdinal)}{PrintTop(top)}",
        CteDefinition.NotReferencedSource nr => PrintNotReferencedSource(nr, top, ref parameterOrdinal),
        CteDefinition.MultiTypeResourceSource mts => PrintMultiTypeResourceSource(mts, top),
        _ => throw new NotSupportedException($"No Explain() rendering for {cte.GetType().Name}."),
    };

    private static string PrintNotReferencedSource(CteDefinition.NotReferencedSource nr, int? top, ref int parameterOrdinal)
    {
        // The target ResourceTypeId is a bound parameter in Emit (EmitNotReferencedSource binds it as
        // @pN, exactly as ResourceSource does), so this must consume an ordinal too or Explain()'s @pN
        // numbering diverges from the emitted SQL. It is still shown inline for readability, like
        // ResourceSource; only the counter is shared. The source-type and ref-param ids are inlined
        // literals in Emit and consume no ordinal.
        parameterOrdinal++;
        var qualifiers = new List<string>();
        if (nr.SourceResourceTypeId is { } sourceTypeId)
        {
            qualifiers.Add($"source={sourceTypeId}");
        }

        if (nr.ReferenceSearchParamId is { } refParamId)
        {
            qualifiers.Add($"ref={refParamId}");
        }

        var suffix = qualifiers.Count == 0 ? string.Empty : $" not referenced by {string.Join(" ", qualifiers)}";
        return $"NotReferencedSource[{nr.TargetResourceTypeId}]{suffix}{PrintTop(top)}";
    }

    private static string PrintMultiTypeResourceSource(CteDefinition.MultiTypeResourceSource mts, int? top)
    {
        // ResourceTypeIds are emitted as literals in SqlBuilder (not bound parameters), so no ordinal is
        // consumed here — parameter numbering in the plan summary stays aligned with the emitted SQL.
        var typeList = mts.ResourceTypeIds.Count == 0
            ? "*"
            : string.Join(",", mts.ResourceTypeIds);
        return $"MultiTypeResourceSource[{typeList}]{PrintTop(top)}";
    }

    private static string PrintResourceSource(CteDefinition.ResourceSource rs, int? top, ref int parameterOrdinal)    {
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

    /// <summary>
    /// How <see cref="Predicate.False"/> reads in an explained plan. Deliberately the same text the SQL
    /// emitter produces, rather than a friendlier <c>false</c>: a plan and its SQL are read side by side in
    /// a trace, and the emitter has no choice — <c>1 = 0</c> is the portable SQL literal for an
    /// unsatisfiable predicate, while <c>false</c> is not valid in a T-SQL WHERE clause. Two spellings of
    /// one node make the reader decide whether they are the same thing, and make a trace ungreppable.
    /// </summary>
    internal const string UnsatisfiableRendering = "1 = 0";

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
        Predicate.Not not => $"NOT ({PrintPredicate(not.Operand, ref parameterOrdinal)})",
        Predicate.IsNull isNull => $"{isNull.Column.Column} IS NULL",
        Predicate.False => UnsatisfiableRendering,
        Predicate.PrefixOfParameter pop => $"{pop.Column.Column} PREFIX_OF @p{parameterOrdinal++}{PrintCollation(pop.Collation)}",
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
