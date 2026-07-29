using System.Globalization;
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
    /// Renders the same content <see cref="Print"/> does, one <see cref="PlanExplainRow"/> per line, label
    /// kept apart from body so tooling can address a row. <see cref="Print"/> is defined in terms of this so
    /// the two can never disagree. The match CTE is the row whose display and canonical labels differ.
    /// </summary>
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

        // Emitted between the CTE graph and the include stages, matching where SqlBuilder binds it:
        // EmitIncludesShape binds the two boundary values after the match-page CTE and before the stage
        // loop, and include-stage CTEs bind nothing, so these are the first stage-level ordinals. Rendering
        // this row after the inc rows would read the same but claim the wrong @pN.
        if (plan.IncludeBoundary is not null)
        {
            rows.Add(new PlanExplainRow(
                "includeBoundary",
                "includeBoundary",
                PlanRowKind.IncludeBoundary,
                PrintIncludeBoundary(ref parameterOrdinal),
                []));
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
    /// Which <see cref="CteDefinition"/> case produced a row. A separate switch from <see cref="PrintCte"/>
    /// (what it is vs how it reads); the compiler still flags either one if a case is added.
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
        CteDefinition.TableExistsPredicate => PlanRowKind.TableExistsPredicate,
        CteDefinition.VisibleSinceFilter => PlanRowKind.VisibleSinceFilter,
        CteDefinition.ReferencedTypeExpansion => PlanRowKind.ReferencedTypeExpansion,
        _ => throw new NotSupportedException($"No Explain() kind for {cte.GetType().Name}."),
    };

    /// <summary>
    /// The CTEs a structural node composes, in naming order — the same fields <see cref="PrintCte"/> renders,
    /// kept as data. Leaf sources are listed explicitly and an unknown case throws: a silent <c>[]</c> would
    /// let a new composing node report no children, silently corrupting parameter provenance.
    /// </summary>
    internal static IReadOnlyList<int> ReferencedCteIndexesOf(CteDefinition cte) => cte switch
    {
        CteDefinition.Intersect x => [x.Left.Index, x.Right.Index],
        CteDefinition.Union u => [.. u.Parts.Select(r => r.Index)],
        CteDefinition.Except ex => [ex.Left.Index, ex.Right.Index],
        CteDefinition.ChainJoin cj => [cj.InnerMatch.Index],
        CteDefinition.ReferencedTypeExpansion re => [re.Seed.Index],
        CteDefinition.ParamSource or CteDefinition.ResourceSource or CteDefinition.CompartmentSource or CteDefinition.NotReferencedSource or CteDefinition.MultiTypeResourceSource
            or CteDefinition.TableExistsPredicate or CteDefinition.VisibleSinceFilter => [],
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

        // A typeless page (BoundaryResourceTypeId is null) binds no type parameter -- its seek compares only
        // the sort key(s) and the surrogate id -- so consume no ordinal for it, keeping the printed @pN
        // sequence aligned with the parameters Emit actually binds.
        var typeParam = page.BoundaryResourceTypeId is null ? "none" : $"@p{parameterOrdinal++}";
        var sidParam = $"@p{parameterOrdinal++}";
        return $"PageSpec(boundary=[{string.Join(",", boundary)}], type={typeParam}, sid={sidParam})";
    }

    /// <summary>
    /// Renders the <c>$includes</c> resume boundary as parameters, not values, like <see cref="PrintPageSpec"/>.
    /// Both components are always bound (no optional type), so two ordinals are consumed unconditionally.
    /// </summary>
    private static string PrintIncludeBoundary(ref int parameterOrdinal)
    {
        var typeParam = $"@p{parameterOrdinal++}";
        var sidParam = $"@p{parameterOrdinal++}";
        return $"IncludeBoundary(type={typeParam}, sid={sidParam})";
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
            $"{p.Table.TableName}[{PrintTypeScope(p.ResourceTypeId)},{p.SearchParamId}]{(p.Predicate is null ? string.Empty : $"  {PrintPredicate(p.Predicate, ref parameterOrdinal)}")}{PrintTop(top)}",
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
        CteDefinition.TableExistsPredicate tep =>
            $"TableExistsPredicate[{tep.Table.TableName}]{(tep.Predicate is null ? string.Empty : $"  {PrintPredicate(tep.Predicate, ref parameterOrdinal)}")}{PrintTop(top)}",
        CteDefinition.VisibleSinceFilter =>
            $"VisibleSinceFilter(@p{parameterOrdinal++}){PrintTop(top)}",
        CteDefinition.ReferencedTypeExpansion re =>
            $"ReferencedTypeExpansion({CteLabel(re.Seed.Index)}, output=[{string.Join(",", re.OutputResourceTypeIds)}]){PrintTop(top)}",
        _ => throw new NotSupportedException($"No Explain() rendering for {cte.GetType().Name}."),
    };

    private static string PrintNotReferencedSource(CteDefinition.NotReferencedSource nr, int? top, ref int parameterOrdinal)
    {
        // TargetResourceTypeId is a bound parameter in Emit, so consume an ordinal here too or Explain()'s
        // @pN numbering diverges from the emitted SQL. Shown inline for readability; only the counter is
        // shared. Source-type and ref-param ids are inline literals in Emit and consume no ordinal.
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

    /// <summary>Renders a CTE's resource-type scope: the literal id, or "*" for a cross-type
    /// (system-level) scope, matching <see cref="PrintMultiTypeResourceSource"/>'s spelling of the same
    /// idea. A null scope emits no type filter in SqlBuilder and so consumes no parameter ordinal.</summary>
    private static string PrintTypeScope(short? resourceTypeId)
        => resourceTypeId is { } id ? id.ToString(CultureInfo.InvariantCulture) : "*";

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
        // ResourceTypeId is a bound parameter in Emit, so consume an ordinal here too or Explain()'s @pN
        // numbering diverges from the emitted SQL. The literal id is still shown inline for readability;
        // only the counter is shared. rs.Predicate, when present, is rendered like OuterPredicate and
        // consumes whatever ordinals PrintPredicate consumes internally.
        parameterOrdinal++;
        var predicateSuffix = rs.Predicate is null ? string.Empty : $" WHERE {PrintPredicate(rs.Predicate, ref parameterOrdinal)}";
        return $"ResourceSource[{rs.ResourceTypeId}]{predicateSuffix}{PrintTop(top)}";
    }

    /// <summary>
    /// How <see cref="Predicate.False"/> reads in an explained plan — the same <c>1 = 0</c> the emitter
    /// produces, not a friendlier <c>false</c>, so plan and SQL stay greppable side by side (and <c>false</c>
    /// isn't valid in a T-SQL WHERE clause anyway).
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
