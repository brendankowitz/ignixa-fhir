using Ignixa.Search.Sql.Ast;
using static Ignixa.Search.Sql.Builders.CteEmitter;
using static Ignixa.Search.Sql.Builders.SqlLabels;

namespace Ignixa.Search.Sql.Builders;

/// <summary>Emits the per-stage include CTE bodies and their seed/constraint correlation clauses.</summary>
internal static class IncludeEmitter
{
    /// <summary>
    /// Renders one include stage: the ReferenceSearchParam/Resource join for its direction, filtered by
    /// reference param and type ids, seeded from the match page and/or earlier stages via EXISTS. The ordinary
    /// path selects <c>TOP (Limit + 1)</c> ordered by (T1, Sid1); the IncludesOnly path drops both. The body is
    /// never filtered by the resume boundary — it seeds downstream <c>:iterate</c> stages (<see cref="EmitSeedExists"/>).
    /// </summary>
    internal static string EmitIncludeStage(
        QueryPlan plan,
        IncludeStage stage,
        ResourceVisibility visibility,
        bool includesOnly,
        string matchSeedLabel)
    {
        var (selectColumns, seedTypeColumn, outputTypeColumn, outputSurrogateColumn, seedCorrelationAlias) = stage.Direction switch
        {
            IncludeDirection.Forward => ("r.ResourceTypeId AS T1, r.ResourceSurrogateId AS Sid1", "rsp.ResourceTypeId", "r.ResourceTypeId", "r.ResourceSurrogateId", "rsp"),
            IncludeDirection.Reverse => ("rsp.ResourceTypeId AS T1, rsp.ResourceSurrogateId AS Sid1", "r.ResourceTypeId", "rsp.ResourceTypeId", "rsp.ResourceSurrogateId", "r"),
            _ => throw new NotSupportedException($"Unknown IncludeDirection '{stage.Direction}'."),
        };

        var whereClauses = new List<string>();
        if (stage.ReferenceSearchParamId is { } paramId)
        {
            whereClauses.Add($"rsp.SearchParamId = {paramId}");
        }

        if (stage.SeedTypeIds is { Count: > 0 } seedTypeIds)
        {
            whereClauses.Add(EmitTypeInFilter(seedTypeColumn, seedTypeIds));
        }

        if (stage.OutputTypeIds is { Count: > 0 } outputTypeIds)
        {
            whereClauses.Add(EmitTypeInFilter(outputTypeColumn, outputTypeIds));
        }

        whereClauses.Add("rsp.BaseUri IS NULL");
        whereClauses.Add(EmitSeedExists(stage, seedCorrelationAlias, includesOnly, matchSeedLabel));

        foreach (var constraint in stage.Constraints ?? [])
        {
            whereClauses.Add(EmitConstraintGuard(plan, constraint, outputTypeColumn, outputSurrogateColumn));
        }

        var rowFilter = ResourceRowFilter(visibility, "r.");

        // Own-line placement, so the helper's leading space is replaced by this line's indentation.
        // An inline caller must not trim it; see ResourceRowFilter's remarks.
        var rowFilterLine = rowFilter.Length > 0 ? $"       {rowFilter.TrimStart()}\n" : string.Empty;

        // Drop the per-stage TOP and its ORDER BY for the IncludesOnly page: the budget is applied once
        // globally, and a CTE ORDER BY without TOP is illegal T-SQL anyway.
        var topClause = includesOnly ? string.Empty : $"TOP ({stage.Limit + 1}) ";
        var orderByClause = includesOnly ? string.Empty : "\n    ORDER BY T1 ASC, Sid1 ASC";

        return $"    SELECT DISTINCT {topClause}{selectColumns}\n" +
               $"    FROM dbo.ReferenceSearchParam rsp\n" +
               $"    INNER JOIN dbo.Resource r\n" +
               $"        ON r.ResourceTypeId = rsp.ReferenceResourceTypeId\n" +
               $"       AND r.ResourceId = rsp.ReferenceResourceId\n" +
               rowFilterLine +
               $"    WHERE {string.Join("\n      AND ", whereClauses)}" +
               orderByClause;
    }

    /// <summary>Renders the EXISTS clause correlating an include row back to its seeds — the match page and/or earlier stages.</summary>
    /// <param name="stage">The stage whose seeds are being correlated.</param>
    /// <param name="correlationAlias">Alias of the include row being tested.</param>
    /// <param name="includesOnly">
    /// Which label an earlier stage is read through: the ordinary path seeds from the limit companion
    /// (<see cref="IncludeLimitLabel"/>); an IncludesOnly page seeds from the stage body (<see cref="IncludeLabel"/>),
    /// unfiltered by the resume boundary so an <c>:iterate</c> stage on page 2 still sees page-1 targets.
    /// </param>
    /// <param name="matchSeedLabel">
    /// Which label the match seed is read through: <see cref="MatchSeed"/> when the page over-fetches a
    /// has-more probe row that must not pull includes of its own, otherwise <see cref="MatchPage"/> itself.
    /// </param>
    private static string EmitSeedExists(IncludeStage stage, string correlationAlias, bool includesOnly, string matchSeedLabel)
    {
        var branches = new List<string>();
        if (stage.SeedFromMatch)
        {
            branches.Add($"SELECT 1 FROM {matchSeedLabel} m WHERE m.T1 = {correlationAlias}.ResourceTypeId AND m.Sid1 = {correlationAlias}.ResourceSurrogateId");
        }

        foreach (var seedStageIndex in stage.SeedStages)
        {
            var seedLabel = includesOnly ? IncludeLabel(seedStageIndex) : IncludeLimitLabel(seedStageIndex);
            branches.Add($"SELECT 1 FROM {seedLabel} m WHERE m.T1 = {correlationAlias}.ResourceTypeId AND m.Sid1 = {correlationAlias}.ResourceSurrogateId");
        }

        return $"EXISTS (\n        {string.Join("\n        UNION ALL\n        ", branches)}\n    )";
    }

    /// <summary>
    /// Renders one access-constraint guard on an include stage: a row of the constrained type must appear in
    /// the constraint CTE, while a row of any other type passes untouched. The leading "type &lt;&gt; id OR"
    /// keeps a multi-type or wildcard stage from dropping rows the constraint does not govern.
    /// </summary>
    private static string EmitConstraintGuard(
        QueryPlan plan,
        IncludeConstraint constraint,
        string outputTypeColumn,
        string outputSurrogateColumn)
        => $"({outputTypeColumn} <> {constraint.ConstraintTypeId} OR EXISTS (" +
           $"SELECT 1 FROM {CteName(plan, constraint.ConstraintCteIndex)} ac " +
           $"WHERE ac.T1 = {outputTypeColumn} AND ac.Sid1 = {outputSurrogateColumn}))";
}
