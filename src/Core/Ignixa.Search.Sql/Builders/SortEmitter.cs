using Ignixa.Search.Expressions;
using Ignixa.Search.Sql.Ast;
using static Ignixa.Search.Sql.Builders.PredicateEmitter;

namespace Ignixa.Search.Sql.Builders;

/// <summary>Emits the sort-key joins, value expressions, ORDER BY clauses and keyset seek predicate.</summary>
internal static class SortEmitter
{
    /// <summary>
    /// Renders the joins to each sort key's search-param table (INNER for the primary key, LEFT for
    /// tie-breakers), filtered to the IsMin/IsMax row for the key's direction. Internal (not private) so
    /// PlanExplainer's matchPage row can check whether this actually emits anything -- LastUpdated/
    /// ResourceType keys and the MissingPrimary phase's own primary key need no join, so "does the plan
    /// sort" is not the same question as "does matchPage carry a sort join."
    /// </summary>
    internal static string EmitSortJoins(SortSpec? sort)
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
            var join = SortKeyEmitter.For(key.Kind).Join(key, i, isPrimary: i == 0);
            if (join is not null)
            {
                joins.Add(join);
            }
        }

        return string.Concat(joins);
    }

    /// <summary>Renders the NOT EXISTS filter that selects rows missing the primary sort key, used in the MissingPrimary phase in place of its join.</summary>
    internal static string EmitMissingPrimaryFilter(SortSpec sort)
    {
        var key = sort.Keys[0];
        if (key.Kind == SortKeyKind.LastUpdated || key.SearchParamId is null)
        {
            throw new NotSupportedException(
                "A MissingPrimary sort phase requires a search-parameter primary key. LastUpdated, " +
                "ResourceType and ResourceId are non-nullable resource columns, so they are never missing and " +
                "have no second segment. Sort on a search parameter, or use SortPhase.Valued.");
        }

        if (key.Kind == SortKeyKind.Aggregated)
        {
            return $"NOT EXISTS (SELECT 1 FROM {key.Table!.SchemaName}.{key.Table.TableName} s WHERE s.ResourceTypeId = m.T1 AND s.ResourceSurrogateId = m.Sid1 AND s.SearchParamId = {key.SearchParamId})";
        }

        // Only String and Date reach here: the guard above rejects LastUpdated, and ResourceType/ResourceId
        // carry no SearchParamId. The fallback preserves the original ternary's else branch for any kind that
        // somehow arrives with one.
        var table = SortKeyEmitter.For(key.Kind) is SearchParamSortKeyEmitter searchParam
            ? searchParam.Table
            : "DateTimeSearchParam";
        return $"NOT EXISTS (SELECT 1 FROM dbo.{table} s WHERE s.ResourceTypeId = m.T1 AND s.ResourceSurrogateId = m.Sid1 AND s.SearchParamId = {key.SearchParamId})";
    }

    /// <summary>The key indices that carry a value in the current phase: all keys when Valued, all but the primary when MissingPrimary.</summary>
    internal static IReadOnlyList<int> ActiveKeyIndices(SortSpec? sort)
        => sort is null
            ? []
            : Enumerable.Range(sort.Phase == SortPhase.Valued ? 0 : 1, sort.ActiveKeyCount).ToList();

    /// <summary>
    /// Renders a sort key's value expression — the raw column, or ISNULL(column, sentinel) where the value
    /// can be missing. This is the single place a key's value expression is produced, so the ORDER BY,
    /// SELECT, and seek-predicate renderings for a key can never drift apart.
    /// </summary>
    private static string SortValueExpr(SortSpec sort, int index)
    {
        var key = sort.Keys[index];
        return SortKeyEmitter.For(key.Kind)
            .ValueExpr(key, index, guaranteedNonNull: index == 0 && sort.Phase == SortPhase.Valued);
    }

    /// <summary>
    /// Renders the ORDER BY for the plain (no-includes) path: each active key's value and direction, then
    /// the (T1, Sid1) tiebreak. A custom sort drops the m.T1 tiebreak so every page orders by (sort keys…,
    /// Sid1) — see <see cref="SortSpec.HasCustomKey"/>.
    /// </summary>
    internal static string EmitOrderBy(SortSpec? sort)
    {
        var activeIndices = ActiveKeyIndices(sort);
        var terms = activeIndices.Select(i =>
            $"{SortValueExpr(sort!, i)} {(sort!.Keys[i].Direction == SortOrder.Ascending ? "ASC" : "DESC")}").ToList();

        // SortValueExpr renders LastUpdated as "m.Sid1" and ResourceType as "m.T1"; if either is an active
        // key, appending it again as the trailing tiebreak would reference it twice (SQL Server Msg 145).
        // Safe to drop: the key already fully determines that column. The tiebreak is unconditionally ASC,
        // so dropping it also preserves a descending _type / _lastUpdated key an appended ASC could not express.
        var hasLastUpdatedKey = activeIndices.Any(i => sort!.Keys[i].Kind == SortKeyKind.LastUpdated);
        var hasResourceTypeKey = activeIndices.Any(i => sort!.Keys[i].Kind == SortKeyKind.ResourceType);

        // Drop the m.T1 tiebreak for a custom sort: its keyset order is (sort keys…, Sid1), type-free, and a
        // typeless page's Sid1-only seek must reproduce it exactly. Keeping T1 in ORDER BY while seeking on
        // Sid1 alone would drop tied rows at the page seam (the legacy (sortValue, T1, Sid1) bug). Sid1's
        // global uniqueness makes what remains a total order.
        if (!hasResourceTypeKey && sort?.HasCustomKey is not true)
        {
            terms.Add("m.T1 ASC");
        }

        if (!hasLastUpdatedKey)
        {
            terms.Add("m.Sid1 ASC");
        }

        return string.Join(", ", terms);
    }

    /// <summary>
    /// Renders the final ORDER BY for the includes path: matches before includes (IsMatch DESC), then the
    /// projected SortValueN columns, then the (T1, Sid1) tiebreak. A custom sort drops the T1 tiebreak as in
    /// <see cref="EmitOrderBy"/>.
    /// </summary>
    internal static string EmitOuterOrderByForIncludes(SortSpec? sort)
        => $"IsMatch DESC, {EmitSortValueOrderBy(sort)}";

    /// <summary>
    /// Renders the match page's own ordering as read back through the SortValueN columns it projects, for
    /// consumers that select from the CTE rather than build it — the includes assembly's ORDER BY (via
    /// <see cref="EmitOuterOrderByForIncludes"/>) and the match-seed CTE's TOP (see
    /// <see cref="MatchPageEmitter.EmitMatchSeed"/>). The single producer of that ordering, so "the first N rows of the
    /// match page" cannot come to mean something different in one caller than the other.
    /// </summary>
    internal static string EmitSortValueOrderBy(SortSpec? sort)
    {
        var activeIndices = ActiveKeyIndices(sort);
        var terms = activeIndices.Select((idx, ordinal) =>
            $"SortValue{ordinal} {(sort!.Keys[idx].Direction == SortOrder.Ascending ? "ASC" : "DESC")}");
        if (sort?.HasCustomKey is not true)
        {
            terms = terms.Append("T1 ASC");
        }

        return string.Join(", ", terms.Append("Sid1 ASC"));
    }

    /// <summary>Renders the ", SortValueN AS ..." select-list columns that project each active key's value for the outer ORDER BY to read.</summary>
    internal static string EmitSortSelectColumns(SortSpec? sort)
    {
        var activeIndices = ActiveKeyIndices(sort);
        return activeIndices.Count == 0
            ? string.Empty
            : ", " + string.Join(", ", activeIndices.Select((idx, ordinal) => $"{SortValueExpr(sort!, idx)} AS SortValue{ordinal}"));
    }

    /// <summary>
    /// Renders the keyset-seek WHERE predicate that skips everything up to the page boundary: an OR of
    /// lexicographic branches over the active sort keys, then the surrogate-id tiebreak, in step with the
    /// ORDER BY. A typed <see cref="PageSpec"/> breaks the final tie on (T1, Sid1); a typeless one on Sid1 alone.
    /// The boundary's value count is checked against the phase by <see cref="PlanValidator"/>.
    /// </summary>
    internal static string EmitSeekPredicate(SortSpec? sort, PageSpec page, List<EmittedSqlParameter> parameters)
    {
        var activeIndices = ActiveKeyIndices(sort);

        var boundaryParams = page.Boundary.Select(b => EmitParam(b, parameters)).ToList();

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

        // Bind the type parameter (when present) before the surrogate id so a typed page keeps its historical
        // @pN ordinals; a typeless page binds no type parameter and omits the type column from the seek.
        if (page.BoundaryResourceTypeId is { } boundaryType)
        {
            var typeParam = EmitParam(boundaryType, parameters);
            var sidParam = EmitParam(page.BoundarySurrogateId, parameters);
            branches.Add($"({allEqualPrefix}m.T1 = {typeParam} AND m.Sid1 > {sidParam})");
            branches.Add($"({allEqualPrefix}m.T1 > {typeParam})");
        }
        else
        {
            var sidParam = EmitParam(page.BoundarySurrogateId, parameters);
            branches.Add($"({allEqualPrefix}m.Sid1 > {sidParam})");
        }

        return branches.Count == 1
            ? branches[0]
            : $"({string.Join("\n       OR ", branches)})";
    }
}
