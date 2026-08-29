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
        string textHistory = plan.EffectiveVisibility.IsHistory switch
        {
            false => " AND textRow.IsHistory = 0",
            true => " AND textRow.IsHistory = 1",
            null => string.Empty,
        };

        WriteCteHeader(writer, plan, cteBodies);
        writer.Append(
            ",\nlastn_candidates AS (\n" +
            candidates.Text +
            "\n),\n" +
            "coded_membership AS (\n" +
            "    SELECT DISTINCT candidate.T1, candidate.Sid1, codeRow.SystemId,\n" +
            "           COALESCE(codeRow.CodeOverflow, codeRow.Code) AS CodeValue\n" +
            "    FROM lastn_candidates candidate\n" +
            "    INNER JOIN dbo.TokenSearchParam codeRow\n" +
            "        ON codeRow.ResourceTypeId = candidate.T1\n" +
            "       AND codeRow.ResourceSurrogateId = candidate.Sid1\n" +
            $"       AND codeRow.SearchParamId = {spec.CodeSearchParamId}\n" +
            $"       AND candidate.T1 = {spec.ResourceTypeId}\n" +
            "),\n" +
            "code_nodes AS (\n" +
            "    SELECT DENSE_RANK() OVER (\n" +
            "               ORDER BY CASE WHEN SystemId IS NULL THEN 0 ELSE 1 END, SystemId, CodeValue) AS NodeId,\n" +
            "           SystemId, CodeValue\n" +
            "    FROM (SELECT DISTINCT SystemId, CodeValue FROM coded_membership) nodes\n" +
            "),\n" +
            "code_edges AS (\n" +
            "    SELECT DISTINCT fromNode.NodeId AS FromNodeId, toNode.NodeId AS ToNodeId\n" +
            "    FROM coded_membership fromCode\n" +
            "    INNER JOIN coded_membership toCode\n" +
            "        ON toCode.T1 = fromCode.T1 AND toCode.Sid1 = fromCode.Sid1\n" +
            "    INNER JOIN code_nodes fromNode\n" +
            "        ON (fromNode.SystemId = fromCode.SystemId OR (fromNode.SystemId IS NULL AND fromCode.SystemId IS NULL))\n" +
            "       AND fromNode.CodeValue = fromCode.CodeValue\n" +
            "    INNER JOIN code_nodes toNode\n" +
            "        ON (toNode.SystemId = toCode.SystemId OR (toNode.SystemId IS NULL AND toCode.SystemId IS NULL))\n" +
            "       AND toNode.CodeValue = toCode.CodeValue\n" +
            "),\n" +
            "code_reach AS (\n" +
            "    SELECT NodeId AS RootNodeId, NodeId,\n" +
            "           CAST(',' + CONVERT(varchar(20), NodeId) + ',' AS varchar(max)) AS Visited\n" +
            "    FROM code_nodes\n" +
            "    UNION ALL\n" +
            "    SELECT reach.RootNodeId, edge.ToNodeId,\n" +
            "           CAST(reach.Visited + CONVERT(varchar(20), edge.ToNodeId) + ',' AS varchar(max))\n" +
            "    FROM code_reach reach\n" +
            "    INNER JOIN code_edges edge ON edge.FromNodeId = reach.NodeId\n" +
            "    WHERE CHARINDEX(',' + CONVERT(varchar(20), edge.ToNodeId) + ',', reach.Visited) = 0\n" +
            "),\n" +
            "node_components AS (\n" +
            "    SELECT RootNodeId AS NodeId, MIN(NodeId) AS CodeGroupId\n" +
            "    FROM code_reach\n" +
            "    GROUP BY RootNodeId\n" +
            "),\n" +
            "coded_groups AS (\n" +
            "    SELECT DISTINCT membership.T1, membership.Sid1, component.CodeGroupId\n" +
            "    FROM coded_membership membership\n" +
            "    INNER JOIN code_nodes node\n" +
            "        ON (node.SystemId = membership.SystemId OR (node.SystemId IS NULL AND membership.SystemId IS NULL))\n" +
            "       AND node.CodeValue = membership.CodeValue\n" +
            "    INNER JOIN node_components component ON component.NodeId = node.NodeId\n" +
            "),\n" +
            "text_groups AS (\n" +
            $"    SELECT DISTINCT candidate.T1, candidate.Sid1, textRow.Text COLLATE {CaseSensitiveCollation} AS TextCode\n" +
            "    FROM lastn_candidates candidate\n" +
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
            $"         MIN(TextCode) COLLATE {CaseSensitiveCollation}, MIN(EffectiveRank), Sid1 DESC\n" +
            "OPTION (MAXRECURSION 0)");
    }
}
