using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;
using static Ignixa.Search.Sql.Builders.CteEmitter;
using static Ignixa.Search.Sql.Builders.PredicateEmitter;

namespace Ignixa.Search.Sql.Builders;

/// <summary>
/// Emits the two halves of semantic search: the gate CTE body for a <see cref="CteDefinition.VectorMatchSource"/>,
/// and the ranking <c>CROSS APPLY</c> a <see cref="VectorRankSpec"/> adds to the match select.
/// </summary>
/// <remarks>
/// The gate and the ranking each bind the query embedding. Sharing one <c>@pN</c> would need emission state
/// threaded from the CTE header into the shape emitters; binding twice costs one duplicate parameter value per
/// search and keeps every emitter self-contained, which is what keeps <see cref="PlanExplainer"/>'s ordinal
/// walk honest.
/// </remarks>
internal static class VectorSearchEmitter
{
    /// <summary>The alias of the ranking <c>CROSS APPLY</c>, whose single column is <see cref="DistanceColumn"/>.</summary>
    internal const string RankAlias = "vr";

    /// <summary>The result column a ranked match row carries its distance in, last in the select list.</summary>
    internal const string DistanceColumn = "Distance";

    /// <summary>The ranking key as the match select reads it.</summary>
    internal const string RankedDistance = $"{RankAlias}.{DistanceColumn}";

    /// <summary>
    /// The embedding width <c>dbo.VectorSearchParam.Embedding</c> stores, read from the catalog so the emitted
    /// <c>CAST(… AS vector(n))</c> and Lower's dimension check cannot drift from the DDL.
    /// </summary>
    internal static int Dimensions { get; } = SqlCatalog.Default.Table("VectorSearchParam").Column("Embedding").MaxLength
        ?? throw new InvalidOperationException("dbo.VectorSearchParam.Embedding declares no vector width in the catalog.");

    /// <summary>
    /// Renders the gate: distinct (type, surrogate id) rows with a chunk within the threshold. Joins
    /// <c>dbo.Resource</c> under the plan's visibility because, unlike every other search-index table,
    /// <c>dbo.VectorSearchParam</c> rows do not always disappear with the version that produced them: a
    /// normal single-resource delete (<c>DeleteAsync</c>) removes them in the same transaction, but a
    /// failed or skipped post-merge vector write, a merge-path/bundle delete, or a write made while the
    /// feature was off can all leave rows behind for a version that is no longer current. The join hides
    /// exactly those. Binds the embedding, then the threshold.
    /// </summary>
    internal static string EmitGate(CteDefinition.VectorMatchSource gate, List<EmittedSqlParameter> parameters, ResourceVisibility visibility)
    {
        var clauses = new List<string>(4);

        // Type, parameter and model ids are schema surrogates, inlined like ParamSource's.
        if (gate.ResourceTypeId is { } typeId)
        {
            clauses.Add($"v.ResourceTypeId = {typeId}");
        }

        clauses.Add($"v.SearchParamId = {gate.SearchParamId}");
        clauses.Add($"v.EmbeddingModelId = {gate.EmbeddingModelId}");

        var embedding = EmitParam(gate.Embedding, parameters);
        clauses.Add($"{Distance("v", embedding)} <= {EmitParam(gate.MaxDistance, parameters)}");

        return new SelectBlock
        {
            Distinct = true,
            Columns = "v.ResourceTypeId AS T1, v.ResourceSurrogateId AS Sid1",
            From = "dbo.VectorSearchParam v",
            Joins =
            [
                "    INNER JOIN dbo.Resource r ON r.ResourceTypeId = v.ResourceTypeId AND r.ResourceSurrogateId = v.ResourceSurrogateId" +
                ResourceRowFilter(visibility, "r."),
            ],
            Where = clauses,
        }.Render();
    }

    /// <summary>
    /// Renders the ranking join appended after the match select's <c>FROM … m</c> (and any sort joins): each
    /// match row's minimum distance over its chunks, by clustered-key seek on the row's own identity. Binds
    /// the embedding. Empty when <paramref name="ranking"/> is null.
    /// </summary>
    internal static string EmitRankJoin(VectorRankSpec? ranking, List<EmittedSqlParameter> parameters)
    {
        if (ranking is not { Source: var gate })
        {
            return string.Empty;
        }

        var embedding = EmitParam(gate.Embedding, parameters);
        return $"\nCROSS APPLY (SELECT MIN({Distance("vsp", embedding)}) AS {DistanceColumn} " +
               "FROM dbo.VectorSearchParam vsp WHERE vsp.ResourceTypeId = m.T1 AND vsp.ResourceSurrogateId = m.Sid1 " +
               $"AND vsp.SearchParamId = {gate.SearchParamId} AND vsp.EmbeddingModelId = {gate.EmbeddingModelId}) {RankAlias}";
    }

    /// <summary>The <c>, vr.Distance AS Distance</c> select-list column for a ranked match select, or empty.</summary>
    internal static string RankSelectColumn(VectorRankSpec? ranking)
        => ranking is null ? string.Empty : $", {RankedDistance} AS {DistanceColumn}";

    private static string Distance(string alias, string embeddingParameter)
        => $"VECTOR_DISTANCE('cosine', {alias}.Embedding, CAST({embeddingParameter} AS vector({Dimensions})))";
}
