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
            "\n),\n" +
            "coded_membership AS (\n" +
            "SELECT DISTINCT candidate.T1, candidate.Sid1,\n" +
            "       DENSE_RANK() OVER (\n" +
            "           ORDER BY CASE WHEN codeRow.SystemId IS NULL THEN 0 ELSE 1 END,\n" +
            "                    codeRow.SystemId, CONCAT(codeRow.Code, codeRow.CodeOverflow)) AS NodeId\n" +
            "FROM lastn_candidates candidate\n" +
            "    INNER JOIN dbo.TokenSearchParam codeRow\n" +
            "        ON codeRow.ResourceTypeId = candidate.T1\n" +
            "       AND codeRow.ResourceSurrogateId = candidate.Sid1\n" +
            $"       AND codeRow.SearchParamId = {spec.CodeSearchParamId}\n" +
            $"       AND candidate.T1 = {spec.ResourceTypeId}\n" +
            "),\n" +
            "code_nodes AS (\n" +
            "    SELECT DISTINCT NodeId FROM coded_membership\n" +
            "),\n" +
            "observation_roots AS (\n" +
            "    SELECT T1, Sid1, MIN(NodeId) AS NodeId\n" +
            "    FROM coded_membership\n" +
            "    GROUP BY T1, Sid1\n" +
            "),\n" +
            // A star connects all translations without constructing a clique per Observation.
            "code_links AS (\n" +
            "    SELECT DISTINCT root.NodeId AS FromNodeId, member.NodeId AS ToNodeId\n" +
            "    FROM observation_roots root\n" +
            "    INNER JOIN coded_membership member ON member.T1 = root.T1 AND member.Sid1 = root.Sid1\n" +
            "    WHERE root.NodeId <> member.NodeId\n" +
            "),\n" +
            "code_edges AS (\n" +
            "    SELECT FromNodeId, ToNodeId FROM code_links\n" +
            "    UNION ALL\n" +
            "    SELECT ToNodeId, FromNodeId FROM code_links\n" +
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
            // Only the smallest reachable root matters; visited paths terminate cycles without truncation.
            "    WHERE edge.ToNodeId > reach.RootNodeId\n" +
            "      AND CHARINDEX(',' + CONVERT(varchar(20), edge.ToNodeId) + ',', reach.Visited) = 0\n" +
            "),\n" +
            "node_components AS (\n" +
            "    SELECT NodeId, MIN(RootNodeId) AS CodeGroupId\n" +
            "    FROM code_reach\n" +
            "    GROUP BY NodeId\n" +
            "),\n" +
            "coded_groups AS (\n" +
            "    SELECT DISTINCT membership.T1, membership.Sid1, node.CodeGroupId\n" +
            "    FROM coded_membership membership\n" +
            "    INNER JOIN node_components node\n" +
            "        ON node.NodeId = membership.NodeId\n" +
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
            "OPTION (MAXRECURSION 0);");
    }
}
