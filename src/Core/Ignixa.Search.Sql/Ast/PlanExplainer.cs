using System.Globalization;
using System.Text;
using Ignixa.Search.Expressions;
using Ignixa.Search.Sql.Builders;
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
    /// Renders the same content <see cref="Print(QueryPlan)"/> does, one <see cref="PlanExplainRow"/> per line,
    /// label kept apart from body so tooling can address a row. <see cref="Print(QueryPlan)"/> is defined in
    /// terms of this so the two can never disagree. The match CTE is the row whose display and canonical
    /// labels differ.
    /// </summary>
    public static IReadOnlyList<PlanExplainRow> Describe(QueryPlan plan)
    {
        QueryPlanValidator.Validate(plan);

        var rows = new List<PlanExplainRow>();

        // Shared across every body that binds parameters, in traversal order: @pN numbering here must
        // match the emitted SQL's, so no row may render its body out of sequence.
        var parameterOrdinal = 0;
        var hasMatchPageCte = plan.Ctes.Any(cte => cte is CteDefinition.MatchPage);

        for (var i = 0; i < plan.Ctes.Count; i++)
        {
            var isRoot = i == plan.Match.Index;

            // The match CTE prints as "root" because that reads better in a plan dump, but it is emitted
            // as cte{i} like every other CTE. Carrying both keeps the display name from being mistaken
            // for something a consumer can join on.
            var cte = plan.Ctes[i];
            var canonicalLabel = ExplainCteLabel(cte, i);
            var label = isRoot ? "root" : canonicalLabel;
            var top = isRoot && !hasMatchPageCte ? plan.MatchSpec.Top : null;
            var body = PrintCte(cte, top, ref parameterOrdinal);
            if (isRoot && !hasMatchPageCte && plan.MatchSpec.OuterPredicate is { } outerPredicate)
            {
                body += $" WHERE {PrintPredicate(outerPredicate, ref parameterOrdinal)}";
            }

            rows.Add(new PlanExplainRow(
                label, canonicalLabel, KindOf(cte), body, ReferencedCteIndexesOf(cte)));
        }

        if (plan.MatchSpec.Sort is { } sort)
        {
            rows.Add(new PlanExplainRow("sort", "sort", PlanRowKind.SortSpec, PrintSortSpec(sort), []));
        }

        if (!hasMatchPageCte)
        {
            // EmitCountOnlyShape never binds the page or offset clause.
            if (!plan.CountOnly && plan.MatchSpec.Page is { } page)
            {
                rows.Add(new PlanExplainRow(
                    "page", "page", PlanRowKind.PageSpec, PrintPageSpec(page, ref parameterOrdinal), []));
            }

            if (plan.MatchSpec.SurrogateRange is not null)
            {
                rows.Add(new PlanExplainRow(
                    "surrogateRange", "surrogateRange", PlanRowKind.SurrogateRange, PrintSurrogateRange(ref parameterOrdinal), []));
            }

            if (plan.MatchSpec.SearchParameterHash is not null)
            {
                rows.Add(new PlanExplainRow(
                    "searchParameterHash", "searchParameterHash", PlanRowKind.SearchParameterHash, PrintSearchParameterHash(ref parameterOrdinal), []));
            }

            // Offset paging is mutually exclusive with Page and is not bound for CountOnly.
            if (!plan.CountOnly && plan.MatchSpec.OffsetPage is { } offsetPage)
            {
                rows.Add(new PlanExplainRow(
                    "offsetPage", "offsetPage", PlanRowKind.OffsetSpec, PrintOffsetSpec(offsetPage, ref parameterOrdinal), []));
            }
        }

        // Bound last among the ordinal-consuming rows: EmitIncludesShape calls EmitMatchPage -- which
        // binds everything above (OuterPredicate through OffsetPage) via BuildMatchWhereClauses plus its own
        // ORDER BY/OFFSET-FETCH -- to completion BEFORE binding the resume boundary, and only then starts the
        // stage loop. Rendering this row any earlier (it used to sit right after the CTE graph) claimed
        // ordinals that page/surrogateRange/searchParameterHash/offsetPage hadn't consumed yet in the real
        // SQL, so every row from here on named the wrong bound value whenever IncludesPage's Resume combined
        // with SurrogateRange or SearchParameterHash -- exactly the combination SqlBuilder's own
        // RejectUnsupportedCombinations guard message recommends ("bound the match set with SurrogateRange
        // and page the include rows with ResultShape.IncludesPage.Resume").
        if (plan.MatchSpec.IncludeBoundary is not null)
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

        if (plan.CountOnly)
        {
            rows.Add(new PlanExplainRow("countOnly", "countOnly", PlanRowKind.CountOnly, "true", []));
        }

        return rows;
    }

    private static string ExplainCteLabel(CteDefinition cte, int index) => cte switch
    {
        CteDefinition.MatchPage => "matchPage",
        CteDefinition.MatchSeed => "matchSeed",
        _ => CteLabel(index),
    };

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
        CteDefinition.MatchPage => PlanRowKind.MatchPageCte,
        CteDefinition.MatchSeed => PlanRowKind.MatchSeedCte,
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
        CteDefinition.MatchPage page => [page.Spec.Root.Index],
        CteDefinition.MatchSeed seed => [seed.Page.Index],
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
    /// Renders the export-sharding surrogate-id window SqlBuilder's <c>AppendSurrogateRangeClauses</c> binds
    /// -- always exactly two ordinals, start then end.
    /// </summary>
    private static string PrintSurrogateRange(ref int parameterOrdinal)
    {
        var startParam = $"@p{parameterOrdinal++}";
        var endParam = $"@p{parameterOrdinal++}";
        return $"SurrogateRange(start={startParam}, end={endParam})";
    }

    /// <summary>
    /// Renders the reindex-eligibility hash comparison SqlBuilder's <c>EmitSearchParameterHashClause</c>
    /// binds -- one ordinal, the hash a resource's own <c>SearchParamHash</c> must differ from.
    /// </summary>
    private static string PrintSearchParameterHash(ref int parameterOrdinal)
        => $"SearchParameterHash(hash=@p{parameterOrdinal++})";

    private static string PrintMatchPageCte(MatchPageSpec spec, ref int parameterOrdinal)
    {
        var top = spec.Top is { } n ? n.ToString(CultureInfo.InvariantCulture) : "none";
        var sortJoins = SortEmitter.EmitSortJoins(spec.Sort).Length > 0;
        var resourceJoin = spec.OuterPredicate is not null || spec.SearchParameterHash is not null;
        var body = $"MatchPageCte(top={top}, sortJoins={(sortJoins ? "true" : "false")}, resourceJoin={(resourceJoin ? "true" : "false")})";

        if (spec.OuterPredicate is { } outerPredicate)
        {
            body += $" WHERE {PrintPredicate(outerPredicate, ref parameterOrdinal)}";
        }

        if (spec.Page is { } page)
        {
            body += $" {PrintPageSpec(page, ref parameterOrdinal)}";
        }

        if (spec.SurrogateRange is not null)
        {
            body += $" {PrintSurrogateRange(ref parameterOrdinal)}";
        }

        if (spec.SearchParameterHash is not null)
        {
            body += $" {PrintSearchParameterHash(ref parameterOrdinal)}";
        }

        if (spec.OffsetPage is { } offsetPage)
        {
            body += $" {PrintOffsetSpec(offsetPage, ref parameterOrdinal)}";
        }

        return body;
    }

    private static string PrintMatchSeedCte(MatchPageSpec spec)
        => $"MatchSeedCte(limit={spec.OffsetPage!.Limit.ToString(CultureInfo.InvariantCulture)})";

    /// <summary>
    /// Renders the OFFSET/FETCH clause Emit binds for <see cref="OffsetSpec"/> -- two ordinals, offset then
    /// fetch count. Fetch count is <see cref="OffsetSpec.FetchCount"/> (the page size plus its probe row,
    /// when <see cref="OffsetSpec.ProbeExtraRow"/> is set), matching what Emit actually binds, not the
    /// caller-facing <see cref="OffsetSpec.Limit"/>.
    /// </summary>
    private static string PrintOffsetSpec(OffsetSpec offset, ref int parameterOrdinal)
    {
        var offsetParam = $"@p{parameterOrdinal++}";
        var fetchParam = $"@p{parameterOrdinal++}";
        return $"OffsetSpec(offset={offsetParam}, fetch={fetchParam})";
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
        CteDefinition.MatchPage page => PrintMatchPageCte(page.Spec, ref parameterOrdinal),
        CteDefinition.MatchSeed seed => PrintMatchSeedCte(seed.Spec),
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
