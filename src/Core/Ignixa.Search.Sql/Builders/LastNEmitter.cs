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

        writer.Append(
            "IF NOT EXISTS (\n" +
            "    SELECT 1\n" +
            "    FROM dbo.LastNCodeGroupGeneration\n" +
            $"    WHERE ResourceTypeId = {spec.ResourceTypeId}\n" +
            $"      AND SearchParamId = {spec.CodeSearchParamId}\n" +
            "      AND State = 'Ready')\n" +
            "    THROW 50403, '$lastn materialization is not ready for this scope.', 1;\n\n");
        WriteCteHeader(writer, plan, cteBodies);
        writer.Append(
            ",\nlastn_candidates AS (\n" +
            candidates.Text +
            "\n),\n" +
            "groups AS (\n" +
            "    SELECT candidate.T1, candidate.Sid1,\n" +
            "           groupRow.GroupKind, groupRow.CodeGroupId, groupRow.TextCode\n" +
            "    FROM lastn_candidates candidate\n" +
            "    INNER JOIN dbo.LastNObservationCodeGroup groupRow\n" +
            "        ON groupRow.ResourceTypeId = candidate.T1\n" +
            $"       AND groupRow.SearchParamId = {spec.CodeSearchParamId}\n" +
            "       AND groupRow.ResourceSurrogateId = candidate.Sid1\n" +
            "),\n" +
            "effective_rows AS (\n" +
            "    SELECT groups.T1, groups.Sid1, groups.GroupKind, groups.CodeGroupId, groups.TextCode,\n" +
            "           dateRow.StartDateTime AS EffectiveStart\n" +
            "    FROM groups\n" +
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
            $"         MIN(TextCode) COLLATE {CaseSensitiveCollation}, MIN(EffectiveRank), Sid1 DESC;");
    }
}
