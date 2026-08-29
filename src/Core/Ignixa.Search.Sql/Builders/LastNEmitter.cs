using Ignixa.Search.Sql.Ast;
using static Ignixa.Search.Sql.Builders.CteEmitter;
using static Ignixa.Search.Sql.Builders.PredicateEmitter;

namespace Ignixa.Search.Sql.Builders;

/// <summary>Emits the post-filter grouping and tie-inclusive ranking pipeline for Observation <c>$lastn</c>.</summary>
internal static class LastNEmitter
{
    private const string CaseSensitiveCollation = "Latin1_General_100_CS_AS";

    internal static void Emit(
        QueryPlan plan,
        ResultShape.LastN shape,
        SqlTextWriter writer,
        List<CteBody> cteBodies,
        List<EmittedSqlParameter> parameters)
    {
        LastNSpec spec = shape.Spec;
        CteBody candidates = MatchPageEmitter.EmitMatchPage(plan.MatchSpec, parameters);
        string maximum = EmitParam(new SqlParameterRef(spec.Maximum), parameters);
        string textHistory = plan.EffectiveVisibility.IsHistory == false
            ? " AND textRow.IsHistory = 0"
            : string.Empty;

        WriteCteHeader(writer, plan, cteBodies);
        writer.Append(
            ",\nlastn_candidates AS (\n" +
            candidates.Text +
            "\n)\n" +
            "SELECT T1, Sid1\n" +
            "INTO #lastn_candidates\n" +
            "FROM lastn_candidates;\n" +
            "CREATE UNIQUE CLUSTERED INDEX IX_LastNCandidates ON #lastn_candidates (T1, Sid1);\n\n" +
            "SELECT DISTINCT candidate.T1, candidate.Sid1,\n" +
            "       DENSE_RANK() OVER (\n" +
            "           ORDER BY CASE WHEN codeRow.SystemId IS NULL THEN 0 ELSE 1 END,\n" +
            "                    codeRow.SystemId, CONCAT(codeRow.Code, codeRow.CodeOverflow)) AS NodeId\n" +
            "INTO #coded_membership\n" +
            "FROM #lastn_candidates candidate\n" +
            "    INNER JOIN dbo.TokenSearchParam codeRow\n" +
            "        ON codeRow.ResourceTypeId = candidate.T1\n" +
            "       AND codeRow.ResourceSurrogateId = candidate.Sid1\n" +
            $"       AND codeRow.SearchParamId = {spec.CodeSearchParamId}\n" +
            $"       AND candidate.T1 = {spec.ResourceTypeId};\n" +
            "CREATE CLUSTERED INDEX IX_CodedMembership ON #coded_membership (T1, Sid1);\n\n" +
            "SELECT membership.NodeId, membership.NodeId AS ComponentId\n" +
            "INTO #code_nodes\n" +
            "FROM #coded_membership membership\n" +
            "GROUP BY membership.NodeId;\n" +
            "CREATE UNIQUE CLUSTERED INDEX IX_CodeNodes ON #code_nodes (NodeId);\n\n" +
            "SELECT DISTINCT fromCode.NodeId AS FromNodeId, toCode.NodeId AS ToNodeId\n" +
            "INTO #code_edges\n" +
            "FROM #coded_membership fromCode\n" +
            "INNER JOIN #coded_membership toCode\n" +
            "    ON toCode.T1 = fromCode.T1 AND toCode.Sid1 = fromCode.Sid1;\n" +
            "CREATE UNIQUE CLUSTERED INDEX IX_CodeEdges ON #code_edges (FromNodeId, ToNodeId);\n\n" +
            "DECLARE @lastnLabelsChanged int = 1;\n" +
            "WHILE @lastnLabelsChanged > 0\n" +
            "BEGIN\n" +
            "    UPDATE target\n" +
            "    SET ComponentId = neighbors.ComponentId\n" +
            "    FROM #code_nodes target\n" +
            "    INNER JOIN (\n" +
            "        SELECT edge.ToNodeId AS NodeId, MIN(source.ComponentId) AS ComponentId\n" +
            "        FROM #code_edges edge\n" +
            "        INNER JOIN #code_nodes source ON source.NodeId = edge.FromNodeId\n" +
            "        GROUP BY edge.ToNodeId\n" +
            "    ) neighbors ON neighbors.NodeId = target.NodeId\n" +
            "    WHERE neighbors.ComponentId < target.ComponentId;\n" +
            "    SET @lastnLabelsChanged = @@ROWCOUNT;\n" +
            "END;\n\n" +
            ";WITH " +
            "coded_groups AS (\n" +
            "    SELECT DISTINCT membership.T1, membership.Sid1, node.ComponentId AS CodeGroupId\n" +
            "    FROM #coded_membership membership\n" +
            "    INNER JOIN #code_nodes node\n" +
            "        ON node.NodeId = membership.NodeId\n" +
            "),\n" +
            "text_groups AS (\n" +
            $"    SELECT DISTINCT candidate.T1, candidate.Sid1, textRow.Text COLLATE {CaseSensitiveCollation} AS TextCode\n" +
            "    FROM #lastn_candidates candidate\n" +
            "    INNER JOIN dbo.TokenText textRow\n" +
            "        ON textRow.ResourceTypeId = candidate.T1\n" +
            "       AND textRow.ResourceSurrogateId = candidate.Sid1\n" +
            $"       AND textRow.SearchParamId = {spec.CodeSearchParamId}\n" +
            $"       AND candidate.T1 = {spec.ResourceTypeId}{textHistory}\n" +
            "    WHERE NOT EXISTS (\n" +
            "        SELECT 1\n" +
            "        FROM dbo.TokenSearchParam coded\n" +
            "        WHERE coded.ResourceTypeId = candidate.T1\n" +
            "          AND coded.ResourceSurrogateId = candidate.Sid1\n" +
            $"          AND coded.SearchParamId = {spec.CodeSearchParamId})\n" +
            "),\n" +
            "all_groups AS (\n" +
            "    SELECT T1, Sid1, CAST(0 AS tinyint) AS GroupKind, CodeGroupId,\n" +
            $"           CAST(NULL AS nvarchar(400)) COLLATE {CaseSensitiveCollation} AS TextCode\n" +
            "    FROM coded_groups\n" +
            "    UNION ALL\n" +
            "    SELECT T1, Sid1, CAST(1 AS tinyint), CAST(NULL AS bigint), TextCode\n" +
            "    FROM text_groups\n" +
            "),\n" +
            "effective_rows AS (\n" +
            "    SELECT groups.T1, groups.Sid1, groups.GroupKind, groups.CodeGroupId, groups.TextCode,\n" +
            "           dateRow.StartDateTime AS EffectiveStart\n" +
            "    FROM all_groups groups\n" +
            "    LEFT JOIN dbo.DateTimeSearchParam dateRow\n" +
            "        ON dateRow.ResourceTypeId = groups.T1\n" +
            "       AND dateRow.ResourceSurrogateId = groups.Sid1\n" +
            $"       AND dateRow.SearchParamId = {spec.EffectiveDateSearchParamId}\n" +
            "       AND dateRow.IsMax = 1\n" +
            "),\n" +
            "ranked AS (\n" +
            "    SELECT T1, Sid1, GroupKind, CodeGroupId, TextCode, EffectiveStart,\n" +
            "           RANK() OVER (\n" +
            "            PARTITION BY GroupKind, CodeGroupId, TextCode\n" +
            "            ORDER BY CASE WHEN EffectiveStart IS NULL THEN 1 ELSE 0 END,\n" +
            "                     EffectiveStart DESC,\n" +
            "                     CASE WHEN EffectiveStart IS NULL THEN Sid1 END DESC) AS EffectiveRank\n" +
            "    FROM effective_rows\n" +
            ")\n" +
            "SELECT T1, Sid1\n" +
            "FROM ranked\n" +
            $"WHERE EffectiveRank <= {maximum}\n" +
            "GROUP BY T1, Sid1\n" +
            "ORDER BY MIN(GroupKind), MIN(CodeGroupId),\n" +
            $"         MIN(TextCode) COLLATE {CaseSensitiveCollation}, MIN(EffectiveRank), Sid1 DESC;\n" +
            "DROP TABLE #code_edges, #code_nodes, #coded_membership, #lastn_candidates;");
    }
}
