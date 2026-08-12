using Ignixa.Search.Expressions;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;
using static Ignixa.Search.Sql.Builders.PredicateEmitter;
using static Ignixa.Search.Sql.Builders.SqlLabels;

namespace Ignixa.Search.Sql.Builders;

/// <summary>Emits the inner SELECT of each <see cref="CteDefinition"/> node kind.</summary>
internal static class CteEmitter
{
    /// <summary>
    /// Renders every <see cref="CteDefinition"/> body in plan order. Runs before any shape emits so the CTE's
    /// bound values take the leading @pN ordinals PlanExplainer reads back.
    /// </summary>
    internal static List<CteBody> EmitCteBodies(
        QueryPlan plan,
        List<EmittedSqlParameter> parameters,
        ResourceVisibility visibility)
    {
        var cteBodies = new List<CteBody>(plan.Ctes.Count);
        for (var i = 0; i < plan.Ctes.Count; i++)
        {
            cteBodies.Add(EmitCte(plan.Ctes[i], parameters, visibility));
        }

        return cteBodies;
    }

    /// <summary>
    /// The name a CTE is bound under. The match page and its trimmed seed carry stable names the include
    /// stages reference by hand, so they opt out of the positional cteN scheme the rest of the graph uses.
    /// </summary>
    internal static string CteName(QueryPlan plan, int index) => plan.Ctes[index] switch
    {
        CteDefinition.MatchPage => MatchPage,
        CteDefinition.MatchSeed => MatchSeed,
        _ => CteLabel(index),
    };

    /// <summary>The range kind a CTE's section is recorded under, matching the name it is bound to.</summary>
    private static string CteRangeKind(CteDefinition cte) => cte switch
    {
        CteDefinition.MatchPage => SqlRangeKind.MatchPage,
        CteDefinition.MatchSeed => SqlRangeKind.MatchSeed,
        _ => SqlRangeKind.Cte,
    };

    /// <summary>Writes the leading ";WITH " and the comma-separated CTE blocks, each in its own section.</summary>
    internal static void WriteCteHeader(SqlTextWriter writer, QueryPlan plan, IReadOnlyList<CteBody> cteBodies)
    {
        writer.Append(";WITH ");
        for (var i = 0; i < cteBodies.Count; i++)
        {
            if (i > 0)
            {
                writer.Append(",\n");
            }

            var name = CteName(plan, i);
            using (writer.Section(name, CteRangeKind(plan.Ctes[i])))
            {
                writer.Append($"{name} AS (\n");
                writer.Append(cteBodies[i].Text, cteBodies[i].Ranges);
                writer.Append("\n)");
            }
        }
    }

    /// <summary>Renders one CTE definition's inner SELECT by its node kind.</summary>
    private static CteBody EmitCte(CteDefinition cte, List<EmittedSqlParameter> parameters, ResourceVisibility visibility) => cte switch
    {
        CteDefinition.ParamSource p => new(EmitParamSource(p, parameters, visibility)),
        CteDefinition.Intersect x =>
            new(
                $"    SELECT {CteLabel(x.Left.Index)}.T1, {CteLabel(x.Left.Index)}.Sid1\n" +
                $"    FROM {CteLabel(x.Left.Index)}\n" +
                $"    INNER JOIN {CteLabel(x.Right.Index)} ON {CteLabel(x.Left.Index)}.T1 = {CteLabel(x.Right.Index)}.T1 AND {CteLabel(x.Left.Index)}.Sid1 = {CteLabel(x.Right.Index)}.Sid1"),
        CteDefinition.Union u =>
            new(string.Join("\n    UNION\n", u.Parts.Select(r => $"    SELECT T1, Sid1 FROM {CteLabel(r.Index)}"))),
        CteDefinition.ResourceSource rs => new(EmitResourceSource(rs, parameters, visibility)),
        CteDefinition.Except ex =>
            new(
                $"    SELECT {CteLabel(ex.Left.Index)}.T1, {CteLabel(ex.Left.Index)}.Sid1\n" +
                $"    FROM {CteLabel(ex.Left.Index)}\n" +
                $"    WHERE NOT EXISTS (\n" +
                $"        SELECT 1 FROM {CteLabel(ex.Right.Index)}\n" +
                $"        WHERE {CteLabel(ex.Right.Index)}.T1 = {CteLabel(ex.Left.Index)}.T1 AND {CteLabel(ex.Right.Index)}.Sid1 = {CteLabel(ex.Left.Index)}.Sid1)"),
        CteDefinition.ChainJoin cj => new(EmitChainJoin(cj, parameters, visibility)),
        CteDefinition.CompartmentSource cs => new(EmitCompartmentSource(cs, parameters)),
        CteDefinition.NotReferencedSource nr => new(EmitNotReferencedSource(nr, parameters, visibility)),
        CteDefinition.MultiTypeResourceSource mts => new(EmitMultiTypeResourceSource(mts, parameters, visibility)),
        CteDefinition.TableExistsPredicate tep => new(EmitTableExistsPredicate(tep, parameters, visibility)),
        CteDefinition.VisibleSinceFilter vsf => new(EmitVisibleSinceFilter(vsf, parameters, visibility)),
        CteDefinition.ReferencedTypeExpansion re => new(EmitReferencedTypeExpansion(re, visibility)),
        CteDefinition.MatchPage page => MatchPageEmitter.EmitMatchPage(page.Spec, parameters),
        CteDefinition.MatchSeed seed => MatchPageEmitter.EmitMatchSeed(seed),
        _ => throw new NotSupportedException($"No Emit for {cte.GetType().Name}."),
    };

    /// <summary>
    /// The projected column list, prefixed with ", " and qualified with the terminal join alias, or empty.
    /// An empty column list is treated as a null projection (identity-only output, no dangling comma).
    /// </summary>
    internal static string ProjectionColumns(ProjectionSpec? projection)
        => projection is null || projection.Columns.Count == 0
            ? string.Empty
            : ", " + string.Join(", ", projection.Columns.Select(c => $"r.[{c.Replace("]", "]]", StringComparison.Ordinal)}]"));

    /// <summary>
    /// The current-row filter for a dbo.Resource scan under a given visibility, prefixed with " AND " and the
    /// caller's column qualifier, or empty when neither axis is constrained. The leading space is load-bearing
    /// for inline callers; own-line callers trim it. Each axis is tri-state (<see cref="ResourceVisibility"/>):
    /// null emits no clause, false emits <c>= 0</c> (current/live), true emits <c>= 1</c> (superseded/deleted).
    /// </summary>
    internal static string ResourceRowFilter(ResourceVisibility visibility, string qualifier)
    {
        var clauses = new List<string>(2);
        if (visibility.IsHistory is { } isHistory)
        {
            clauses.Add($"{qualifier}IsHistory = {(isHistory ? 1 : 0)}");
        }

        if (visibility.IsDeleted is { } isDeleted)
        {
            clauses.Add($"{qualifier}IsDeleted = {(isDeleted ? 1 : 0)}");
        }

        return clauses.Count == 0 ? string.Empty : " AND " + string.Join(" AND ", clauses);
    }

    /// <summary>
    /// The unqualified <c>IsHistory = 0</c> clause a search-param index table needs under a given
    /// visibility, or empty when it needs none. Emitted only for a latest-only search (IsHistory == false)
    /// against a table that has the column (e.g. dbo.TokenText, which retains superseded rows); null and
    /// true render empty so the dbo.Resource scan alone selects the version. Mirrors the legacy generator.
    /// </summary>
    private static string SearchParamTableHistoryClause(TableDescriptor table, ResourceVisibility visibility)
        => visibility.IsHistory == false && table.Columns.Any(c => c.Name == "IsHistory")
            ? "IsHistory = 0"
            : string.Empty;

    /// <summary>Renders a ParamSource: distinct (type, surrogate id) rows from one search-param table filtered by SearchParamId and its optional predicate.</summary>
    private static string EmitParamSource(CteDefinition.ParamSource p, List<EmittedSqlParameter> parameters, ResourceVisibility visibility)
    {
        var predicateClause = p.Predicate is null ? string.Empty : $" AND {EmitPredicate(p.Predicate, parameters)}";

        var historyClause = SearchParamTableHistoryClause(p.Table, visibility) is { Length: > 0 } clause
            ? $" AND {clause}"
            : string.Empty;

        // A null ResourceTypeId is a system-level (cross-type) search: emit no type filter. The requested
        // types are narrowed by the plan's MultiTypeResourceSource base set this CTE is intersected with.
        var typeFilter = p.ResourceTypeId is { } typeId ? $"ResourceTypeId = {typeId} AND " : string.Empty;

        return $"    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
               $"    FROM {p.Table.SchemaName}.{p.Table.TableName}\n" +
               $"    WHERE {typeFilter}SearchParamId = {p.SearchParamId}{historyClause}{predicateClause}";
    }

    /// <summary>Renders a chain as a join through dbo.ReferenceSearchParam and dbo.Resource, correlated to the inner match set, in the forward or reverse direction.</summary>
    private static string EmitChainJoin(CteDefinition.ChainJoin cj, List<EmittedSqlParameter> parameters, ResourceVisibility visibility)
    {
        // Hand-rolled interpolation, not Predicate.Equal via EmitPredicate: every id ChainJoin carries must
        // render as a literal, but EmitPredicate's Equal arm calls EmitParam and would bind a real @pN,
        // breaking the parameter-ordinal invariant PlanExplainer relies on.
        var outputFilter = string.Join(
            " OR ",
            cj.OutputResourceTypeIds.Select(id => $"{OutputTypeColumn(cj.Direction)} = {id}"));
        if (cj.OutputResourceTypeIds.Count > 1)
        {
            outputFilter = $"({outputFilter})";
        }

        var rowFilter = ResourceRowFilter(visibility, "r.");

        // Own-line placement, so the helper's leading space is replaced by this line's indentation.
        // An inline caller must not trim it; see ResourceRowFilter's remarks.
        var rowFilterLine = rowFilter.Length > 0 ? $"       {rowFilter.TrimStart()}\n" : string.Empty;

        return cj.Direction switch
        {
            ChainDirection.Forward =>
                $"    SELECT DISTINCT rsp.ResourceTypeId AS T1, rsp.ResourceSurrogateId AS Sid1\n" +
                $"    FROM dbo.ReferenceSearchParam rsp\n" +
                $"    INNER JOIN dbo.Resource r\n" +
                $"        ON r.ResourceTypeId = rsp.ReferenceResourceTypeId\n" +
                $"       AND r.ResourceId = rsp.ReferenceResourceId\n" +
                rowFilterLine +
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
                rowFilterLine +
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

    /// <summary>Renders a CompartmentSource: rows of dbo.ReferenceSearchParam for the membership SearchParamId, any of the member resource types, and the fixed compartment reference.</summary>
    private static string EmitCompartmentSource(CteDefinition.CompartmentSource cs, List<EmittedSqlParameter> parameters)
        => $"    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
           $"    FROM dbo.ReferenceSearchParam\n" +
           $"    WHERE SearchParamId = {cs.SearchParamId}\n" +
           $"      AND {EmitTypeInFilter("ResourceTypeId", cs.ResourceTypeIds)}\n" +
           $"      AND {EmitPredicate(cs.Predicate, parameters)}";

    /// <summary>
    /// Renders a NotReferencedSource: current, non-deleted rows of dbo.Resource for the target type that no
    /// dbo.ReferenceSearchParam row points at, optionally narrowed to one source type and/or reference path.
    /// Only the target type is bound; the inner ids are schema surrogates, inlined like every other schema id.
    /// </summary>
    private static string EmitNotReferencedSource(CteDefinition.NotReferencedSource nr, List<EmittedSqlParameter> parameters, ResourceVisibility visibility)
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
               $"    WHERE r.ResourceTypeId = {EmitParam(new SqlParameterRef(nr.TargetResourceTypeId), parameters)}{ResourceRowFilter(visibility, "r.")}\n" +
               $"      AND NOT EXISTS (\n" +
               $"        SELECT 1\n" +
               $"        FROM dbo.ReferenceSearchParam rsp\n" +
               $"        WHERE rsp.ReferenceResourceId = r.ResourceId\n" +
               $"          AND rsp.ReferenceResourceTypeId = r.ResourceTypeId{innerFilters})";
    }

    /// <summary>
    /// Renders a TableExistsPredicate: distinct (type, surrogate id) rows from one raw table, with an
    /// optional additional predicate and no SearchParamId/ResourceTypeId filter. Visibility reaches it via
    /// <see cref="SearchParamTableHistoryClause"/>, not <see cref="ResourceRowFilter"/>, whose IsDeleted
    /// clause references a column no search-param table has.
    /// </summary>
    private static string EmitTableExistsPredicate(CteDefinition.TableExistsPredicate tep, List<EmittedSqlParameter> parameters, ResourceVisibility visibility)
    {
        var clauses = new List<string>(2);
        if (SearchParamTableHistoryClause(tep.Table, visibility) is { Length: > 0 } historyClause)
        {
            clauses.Add(historyClause);
        }

        if (tep.Predicate is not null)
        {
            clauses.Add(EmitPredicate(tep.Predicate, parameters));
        }

        var whereClause = clauses.Count == 0 ? string.Empty : $"\n    WHERE {string.Join(" AND ", clauses)}";
        return
            $"    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            $"    FROM {tep.Table.SchemaName}.{tep.Table.TableName}{whereClause}";
    }

    /// <summary>Renders a VisibleSinceFilter: resources visible in a transaction on or after Since, joined through dbo.Resource and dbo.Transactions on VisibleDate.</summary>
    private static string EmitVisibleSinceFilter(CteDefinition.VisibleSinceFilter vsf, List<EmittedSqlParameter> parameters, ResourceVisibility visibility)
        => "    SELECT DISTINCT r.ResourceTypeId AS T1, r.ResourceSurrogateId AS Sid1\n" +
           "    FROM dbo.Resource r\n" +
           "    INNER JOIN dbo.Transactions t ON r.TransactionId = t.SurrogateIdRangeFirstValue\n" +
           $"    WHERE t.VisibleDate >= {EmitParam(vsf.Since, parameters)}{ResourceRowFilter(visibility, "r.")}";

    /// <summary>Renders a ReferencedTypeExpansion: the referenced resources reachable via any outbound internal reference from the seed set, restricted to the output resource types. Mirrors ChainJoin's reverse topology but with no SearchParamId/source-type filter (all reference parameters, any source type).</summary>
    private static string EmitReferencedTypeExpansion(CteDefinition.ReferencedTypeExpansion re, ResourceVisibility visibility)
    {
        var rowFilter = ResourceRowFilter(visibility, "r.");

        // Own-line placement, so the helper's leading space is replaced by this line's indentation.
        // An inline caller must not trim it; see ResourceRowFilter's remarks.
        var rowFilterLine = rowFilter.Length > 0 ? $"       {rowFilter.TrimStart()}\n" : string.Empty;

        return $"    SELECT DISTINCT r.ResourceTypeId AS T1, r.ResourceSurrogateId AS Sid1\n" +
               $"    FROM dbo.ReferenceSearchParam rsp\n" +
               $"    INNER JOIN {CteLabel(re.Seed.Index)} m\n" +
               $"        ON m.T1 = rsp.ResourceTypeId AND m.Sid1 = rsp.ResourceSurrogateId\n" +
               $"    INNER JOIN dbo.Resource r\n" +
               $"        ON r.ResourceTypeId = rsp.ReferenceResourceTypeId\n" +
               $"       AND r.ResourceId = rsp.ReferenceResourceId\n" +
               rowFilterLine +
               $"    WHERE {EmitTypeInFilter("rsp.ReferenceResourceTypeId", re.OutputResourceTypeIds)}\n" +
               $"      AND rsp.BaseUri IS NULL";
    }

    /// <summary>
    /// Renders a ResourceSource: current, non-deleted rows of dbo.Resource for one type, with an optional
    /// nested-scope predicate. Binds its type id as a parameter (unlike the sibling emitters, which use
    /// literals); left as-is because converging on literals would shift downstream parameter ordinals.
    /// </summary>
    private static string EmitResourceSource(CteDefinition.ResourceSource rs, List<EmittedSqlParameter> parameters, ResourceVisibility visibility)
    {
        var predicateClause = rs.Predicate is null ? string.Empty : $" AND {EmitPredicate(rs.Predicate, parameters)}";
        return $"    SELECT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
               $"    FROM dbo.Resource\n" +
               $"    WHERE ResourceTypeId = {EmitParam(new SqlParameterRef(rs.ResourceTypeId), parameters)}{ResourceRowFilter(visibility, string.Empty)}{predicateClause}";
    }

    /// <summary>Renders a MultiTypeResourceSource: a dbo.Resource scan across a set of types, or every type when the set is empty.</summary>
    private static string EmitMultiTypeResourceSource(
        CteDefinition.MultiTypeResourceSource mts,
        List<EmittedSqlParameter> parameters,
        ResourceVisibility visibility)
    {
        // Type ids are literals, not bound parameters (matching ParamSource and ChainJoin). An empty list
        // means "every type"; emit no type filter. Unresolvable sentinel ids (-1) are kept intentionally:
        // they match no row, whereas dropping them could collapse an all-unknown list to a full-table scan.
        var clauses = new List<string>(4);
        if (mts.ResourceTypeIds.Count > 0)
        {
            clauses.Add($"ResourceTypeId IN ({string.Join(", ", mts.ResourceTypeIds)})");
        }

        if (visibility.IsHistory is { } isHistory)
        {
            clauses.Add($"IsHistory = {(isHistory ? 1 : 0)}");
        }

        if (visibility.IsDeleted is { } isDeleted)
        {
            clauses.Add($"IsDeleted = {(isDeleted ? 1 : 0)}");
        }

        if (mts.Predicate is not null)
        {
            clauses.Add(EmitPredicate(mts.Predicate, parameters));
        }

        var whereClause = clauses.Count == 0 ? string.Empty : $"    WHERE {string.Join(" AND ", clauses)}";

        return $"    SELECT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
               $"    FROM dbo.Resource\n" +
               whereClause;
    }

    /// <summary>Renders a "column = a OR column = b ..." type-id filter, parenthesized when there is more than one id.</summary>
    internal static string EmitTypeInFilter(string column, IReadOnlyList<short> typeIds)
    {
        var filter = string.Join(" OR ", typeIds.Select(id => $"{column} = {id}"));
        return typeIds.Count > 1 ? $"({filter})" : filter;
    }
}
