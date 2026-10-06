using System.Data;
using System.Globalization;
using Ignixa.Abstractions;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Domain.Models;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Data.SqlClient;
using Microsoft.Data.SqlClient.Server;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

/// <summary>
/// Schema version 4: <c>dbo.VectorSearchParam</c>, <c>dbo.EmbeddingModel</c>, their table-valued-parameter
/// types, and <c>dbo.MergeVectorSearchParams</c> / <c>dbo.GetOrCreateEmbeddingModel</c>. Exercises the DDL
/// and stored procedures directly against a freshly deployed tenant database.
/// <para>
/// Task 6 owns the C# writer (<c>SqlServerVectorIndexWriter</c>) that calls these procedures from the
/// merge path; it does not exist yet. Until then, these tests call the procedures directly with hand-built
/// TVP rows -- exactly the shape <c>SqlServerVectorIndexWriter</c> will produce -- and seed
/// <c>dbo.VectorSearchParam</c> rows directly by INSERT where a test needs one to already exist (the
/// hard/soft-delete and DeleteHistory sweep tests), since nothing yet writes them through the normal merge
/// path.
/// </para>
/// </summary>
public class VectorSearchParamSchemaTests : IAsyncLifetime
{
    private TestTenantDatabase _database = null!;

    public async Task InitializeAsync() => _database = await TestTenantDatabase.CreateSqlServerFhirRepositoryAsync();

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task GivenFreshDatabase_WhenDeployed_ThenVectorTableAndProcsExist()
    {
        (await _database.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.tables WHERE name = 'VectorSearchParam'")).ShouldBe(1);
        (await _database.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.tables WHERE name = 'EmbeddingModel'")).ShouldBe(1);
        (await _database.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.table_types WHERE name = 'VectorSearchParamList'")).ShouldBe(1);
        (await _database.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.table_types WHERE name = 'VectorResourceList'")).ShouldBe(1);
        (await _database.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.procedures WHERE name = 'MergeVectorSearchParams'")).ShouldBe(1);
        (await _database.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.procedures WHERE name = 'GetOrCreateEmbeddingModel'")).ShouldBe(1);

        // Proves Embedding is really the native `vector` type, not merely an NVARCHAR/VARBINARY column
        // that happens to be named Embedding.
        (await _database.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM   sys.columns c
                   INNER JOIN sys.types t ON c.user_type_id = t.user_type_id
            WHERE  c.object_id = OBJECT_ID('dbo.VectorSearchParam')
                   AND c.name = 'Embedding'
                   AND t.name = 'vector'
            """)).ShouldBe(1);
    }

    [Fact]
    public async Task GivenEvaluatedResourceWithVectors_WhenMerged_ThenRowsInsertedWithVectorType()
    {
        var (resourceTypeId, surrogateId) = await CreatePatientAsync("vector-merge-1");
        var modelId = await GetOrCreateEmbeddingModelAsync("test-model", 1536);

        await MergeVectorSearchParamsAsync(
            evaluated: [(resourceTypeId, surrogateId)],
            vectors:
            [
                new VectorRow(resourceTypeId, surrogateId, SearchParamId: 1, modelId, ChunkOrdinal: 0,
                    SourceTextCompressed: [1, 2, 3], SourceTextHash: FixedHash(1), EmbeddingJson: BuildEmbeddingJson(0.01f)),
            ]);

        var rowCount = await _database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.VectorSearchParam WHERE ResourceSurrogateId = {surrogateId}");
        rowCount.ShouldBe(1);

        // VECTOR_DISTANCE against itself is ~0 only if Embedding round-tripped as a real vector(1536),
        // not as opaque text the server merely stored.
        var selfDistance = await _database.ExecuteScalarAsync<double>(
            $"SELECT CAST(VECTOR_DISTANCE('cosine', Embedding, Embedding) AS FLOAT) FROM dbo.VectorSearchParam WHERE ResourceSurrogateId = {surrogateId}");
        selfDistance.ShouldBe(0d, tolerance: 0.0001);
    }

    [Fact]
    public async Task GivenEvaluatedResourceWithNoVectors_WhenMerged_ThenPriorVersionRowsDeleted()
    {
        var (resourceTypeId, surrogateId) = await CreatePatientAsync("vector-merge-2");
        var modelId = await GetOrCreateEmbeddingModelAsync("test-model", 1536);

        await MergeVectorSearchParamsAsync(
            evaluated: [(resourceTypeId, surrogateId)],
            vectors:
            [
                new VectorRow(resourceTypeId, surrogateId, SearchParamId: 1, modelId, ChunkOrdinal: 0,
                    SourceTextCompressed: [1, 2, 3], SourceTextHash: FixedHash(1), EmbeddingJson: BuildEmbeddingJson(0.01f)),
            ]);
        (await _database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.VectorSearchParam WHERE ResourceSurrogateId = {surrogateId}")).ShouldBe(
            1, "without a row to remove, the assertion after the second merge cannot fail");

        // The resource's semantic text became empty on update: the evaluated resource is still current,
        // but carries no vector rows this time.
        await MergeVectorSearchParamsAsync(
            evaluated: [(resourceTypeId, surrogateId)],
            vectors: []);

        (await _database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.VectorSearchParam WHERE ResourceSurrogateId = {surrogateId}")).ShouldBe(0);
    }

    [Fact]
    public async Task GivenSurrogateNowHistory_WhenMerged_ThenNoRowsInsertedAndNewerVersionVectorsUntouched()
    {
        var resourceId = "vector-merge-race";
        var (resourceTypeId, v1SurrogateId) = await CreatePatientAsync(resourceId);
        // A second PUT: v1 becomes history, a new current row (v2) is created.
        await _database.Repository.CreateOrUpdateAsync(
            BuildPatientWrapper(resourceId), CancellationToken.None);
        var v2SurrogateId = await _database.ExecuteScalarAsync<long>(
            $"SELECT ResourceSurrogateId FROM dbo.Resource WHERE ResourceId = '{resourceId}' AND IsHistory = 0");
        v2SurrogateId.ShouldNotBe(v1SurrogateId);

        var modelId = await GetOrCreateEmbeddingModelAsync("test-model", 1536);

        // v2's own (hypothetical, not-yet-built) writer already persisted its vectors.
        await MergeVectorSearchParamsAsync(
            evaluated: [(resourceTypeId, v2SurrogateId)],
            vectors:
            [
                new VectorRow(resourceTypeId, v2SurrogateId, SearchParamId: 1, modelId, ChunkOrdinal: 0,
                    SourceTextCompressed: [9, 9, 9], SourceTextHash: FixedHash(9), EmbeddingJson: BuildEmbeddingJson(0.09f)),
            ]);

        // A late writer for v1 (the now-historical surrogate) races in -- its evaluated surrogate is no
        // longer current, so it must be skipped entirely, touching neither v1 nor v2's rows.
        await MergeVectorSearchParamsAsync(
            evaluated: [(resourceTypeId, v1SurrogateId)],
            vectors:
            [
                new VectorRow(resourceTypeId, v1SurrogateId, SearchParamId: 1, modelId, ChunkOrdinal: 0,
                    SourceTextCompressed: [1, 1, 1], SourceTextHash: FixedHash(1), EmbeddingJson: BuildEmbeddingJson(0.01f)),
            ]);

        (await _database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.VectorSearchParam WHERE ResourceSurrogateId = {v1SurrogateId}")).ShouldBe(
            0, "the stale surrogate's late write must insert nothing");
        (await _database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.VectorSearchParam WHERE ResourceSurrogateId = {v2SurrogateId}")).ShouldBe(
            1, "v2's own vectors must survive a stale v1 merge untouched");
    }

    [Fact]
    public async Task GivenVectors_WhenHardDeleted_ThenRowsRemoved()
    {
        const string ResourceId = "vector-hard-delete-1";
        var (resourceTypeId, surrogateId) = await CreatePatientAsync(ResourceId);
        await InsertVectorRowDirectAsync(resourceTypeId, surrogateId);

        (await _database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.VectorSearchParam WHERE ResourceSurrogateId = {surrogateId}")).ShouldBe(
            1, "without a row to remove, the assertion after the delete cannot fail");

        await _database.Repository.HardDeleteResourceAsync(resourceTypeId, ResourceId, CancellationToken.None);

        (await _database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.VectorSearchParam WHERE ResourceSurrogateId = {surrogateId}")).ShouldBe(0);
    }

    [Fact]
    public async Task GivenVectors_WhenSoftDeleted_ThenRowsRemoved()
    {
        const string ResourceId = "vector-soft-delete-1";
        var (resourceTypeId, surrogateId) = await CreatePatientAsync(ResourceId);
        await InsertVectorRowDirectAsync(resourceTypeId, surrogateId);

        (await _database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.VectorSearchParam WHERE ResourceSurrogateId = {surrogateId}")).ShouldBe(
            1, "without a row to remove, the assertion after the delete cannot fail");

        await _database.Repository.DeleteAsync(
            new ResourceKey("Patient", ResourceId), new ResourceRequest("DELETE", $"Patient/{ResourceId}"), null, CancellationToken.None);

        (await _database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.VectorSearchParam WHERE ResourceSurrogateId = {surrogateId}")).ShouldBe(0);
    }

    [Fact]
    public async Task GivenVectorsOnHistoryVersions_WhenDeleteHistory_ThenRowsRemoved()
    {
        // v1's vector rows are inserted directly (not through MergeVectorSearchParams), to simulate the
        // realistic orphan scenario this sweep exists for: a later update (v2) whose semantic text
        // evaluated to nothing writes no vectors at all -- not even an empty merge call -- leaving v1's
        // rows as true orphans once v1 becomes history. Running v2's vectors through
        // MergeVectorSearchParams here would itself delete v1's rows (by design: a real post-merge write
        // deletes every version's vectors before inserting the new set), which would prove nothing about
        // DeleteHistory's own sweep.
        const string ResourceId = "vector-delete-history-1";
        var (resourceTypeId, v1SurrogateId) = await CreatePatientAsync(ResourceId);
        await InsertVectorRowRawAsync(resourceTypeId, v1SurrogateId);

        await _database.Repository.CreateOrUpdateAsync(BuildPatientWrapper(ResourceId), CancellationToken.None);
        var v2SurrogateId = await _database.ExecuteScalarAsync<long>(
            $"SELECT ResourceSurrogateId FROM dbo.Resource WHERE ResourceId = '{ResourceId}' AND IsHistory = 0");
        await InsertVectorRowRawAsync(resourceTypeId, v2SurrogateId);

        (await _database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.VectorSearchParam WHERE ResourceSurrogateId = {v1SurrogateId}")).ShouldBe(
            1, "without a row to remove, the assertion after the sweep cannot fail");

        await _database.ExecuteNonQueryAsync("EXEC dbo.DeleteHistory @DeleteResources = 0, @Reset = 1, @DisableLogEvent = 1");

        (await _database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.VectorSearchParam WHERE ResourceSurrogateId = {v1SurrogateId}")).ShouldBe(
            0, "the history version's vectors must be swept");
        (await _database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.VectorSearchParam WHERE ResourceSurrogateId = {v2SurrogateId}")).ShouldBe(
            1, "the current version's vectors must survive a history-only sweep");
    }

    [Fact]
    public async Task GivenSameModelKeyTwice_WhenGetOrCreate_ThenSameId()
    {
        var firstId = await GetOrCreateEmbeddingModelAsync("same-model-key", 1536);
        var secondId = await GetOrCreateEmbeddingModelAsync("same-model-key", 1536);

        secondId.ShouldBe(firstId);
        (await _database.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.EmbeddingModel WHERE ModelKey = 'same-model-key'")).ShouldBe(1);
    }

    [Fact]
    public async Task GivenADifferentDimensionsForAnExistingModelKey_WhenGetOrCreate_ThenThrows()
    {
        await GetOrCreateEmbeddingModelAsync("conflicting-model-key", 1536);

        await Should.ThrowAsync<SqlException>(() => GetOrCreateEmbeddingModelAsync("conflicting-model-key", 768));
    }

    private async Task<(short ResourceTypeId, long SurrogateId)> CreatePatientAsync(string resourceId)
    {
        await _database.Repository.CreateOrUpdateAsync(BuildPatientWrapper(resourceId), CancellationToken.None);
        var resourceTypeId = await _database.ExecuteScalarAsync<short>(
            "SELECT ResourceTypeId FROM dbo.ResourceType WHERE Name = 'Patient'");
        var surrogateId = await _database.ExecuteScalarAsync<long>(
            $"SELECT ResourceSurrogateId FROM dbo.Resource WHERE ResourceId = '{resourceId}' AND IsHistory = 0");
        return (resourceTypeId, surrogateId);
    }

    private static ResourceWrapper BuildPatientWrapper(string resourceId) =>
        new("Patient", resourceId, "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse($$"""{"resourceType":"Patient","id":"{{resourceId}}"}"""),
            new ResourceRequest("PUT", $"Patient/{resourceId}"));

    private Task InsertVectorRowDirectAsync(short resourceTypeId, long surrogateId) =>
        MergeVectorSearchParamsAsync(
            evaluated: [(resourceTypeId, surrogateId)],
            vectors:
            [
                new VectorRow(resourceTypeId, surrogateId, SearchParamId: 1, EmbeddingModelId: 1, ChunkOrdinal: 0,
                    SourceTextCompressed: [1, 2, 3], SourceTextHash: FixedHash(1), EmbeddingJson: BuildEmbeddingJson(0.01f)),
            ]);

    /// <summary>
    /// Inserts a dbo.VectorSearchParam row by plain INSERT, bypassing MergeVectorSearchParams entirely.
    /// Needed wherever a test must seed vectors on a resource version that is ABOUT to become (or already
    /// is) history: running that insert through the real procedure would have deleted the row being set up
    /// as much as DeleteHistory's sweep is supposed to -- see
    /// GivenVectorsOnHistoryVersions_WhenDeleteHistory_ThenRowsRemoved for the scenario this avoids.
    /// </summary>
    private Task InsertVectorRowRawAsync(short resourceTypeId, long surrogateId) =>
        _database.ExecuteNonQueryAsync(
            $"""
            INSERT INTO dbo.VectorSearchParam
                (ResourceTypeId, ResourceSurrogateId, SearchParamId, EmbeddingModelId, ChunkOrdinal, SourceTextCompressed, SourceTextHash, Embedding)
            VALUES
                ({resourceTypeId}, {surrogateId}, 1, 1, 0, 0x010203, 0x{Convert.ToHexString(FixedHash(1))}, '{BuildEmbeddingJson(0.01f)}')
            """);

    private static byte[] FixedHash(byte seed)
    {
        var hash = new byte[32];
        Array.Fill(hash, seed);
        return hash;
    }

    private static string BuildEmbeddingJson(float seed)
    {
        var values = Enumerable.Range(0, 1536).Select(i => (seed + (i * 0.0001f)).ToString("R", CultureInfo.InvariantCulture));
        return "[" + string.Join(",", values) + "]";
    }

    private async Task<short> GetOrCreateEmbeddingModelAsync(string modelKey, short dimensions)
    {
        await using var connection = new SqlConnection(_database.ConnectionString);
        await connection.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = "dbo.GetOrCreateEmbeddingModel";
        command.CommandType = CommandType.StoredProcedure;
        command.Parameters.Add(new SqlParameter("@ModelKey", SqlDbType.VarChar, 256) { Value = modelKey });
        command.Parameters.Add(new SqlParameter("@Dimensions", SqlDbType.SmallInt) { Value = dimensions });
        var output = new SqlParameter("@EmbeddingModelId", SqlDbType.SmallInt) { Direction = ParameterDirection.Output };
        command.Parameters.Add(output);
        await command.ExecuteNonQueryAsync(CancellationToken.None);
        return (short)output.Value!;
    }

    private sealed record VectorRow(
        short ResourceTypeId,
        long ResourceSurrogateId,
        short SearchParamId,
        short EmbeddingModelId,
        short ChunkOrdinal,
        byte[] SourceTextCompressed,
        byte[] SourceTextHash,
        string EmbeddingJson);

    private static readonly SqlMetaData[] EvaluatedMetadata =
    [
        new SqlMetaData("ResourceTypeId", SqlDbType.SmallInt),
        new SqlMetaData("ResourceSurrogateId", SqlDbType.BigInt),
    ];

    private static readonly SqlMetaData[] VectorMetadata =
    [
        new SqlMetaData("ResourceTypeId", SqlDbType.SmallInt),
        new SqlMetaData("ResourceSurrogateId", SqlDbType.BigInt),
        new SqlMetaData("SearchParamId", SqlDbType.SmallInt),
        new SqlMetaData("EmbeddingModelId", SqlDbType.SmallInt),
        new SqlMetaData("ChunkOrdinal", SqlDbType.SmallInt),
        new SqlMetaData("SourceTextCompressed", SqlDbType.VarBinary, -1),
        new SqlMetaData("SourceTextHash", SqlDbType.Binary, 32),
        new SqlMetaData("Embedding", SqlDbType.NVarChar, -1),
    ];

    private static SqlDataRecord BuildEvaluatedRecord(short resourceTypeId, long resourceSurrogateId)
    {
        var record = new SqlDataRecord(EvaluatedMetadata);
        record.SetInt16(0, resourceTypeId);
        record.SetInt64(1, resourceSurrogateId);
        return record;
    }

    private static SqlDataRecord BuildVectorRecord(VectorRow row)
    {
        var record = new SqlDataRecord(VectorMetadata);
        record.SetInt16(0, row.ResourceTypeId);
        record.SetInt64(1, row.ResourceSurrogateId);
        record.SetInt16(2, row.SearchParamId);
        record.SetInt16(3, row.EmbeddingModelId);
        record.SetInt16(4, row.ChunkOrdinal);
        record.SetBytes(5, 0, row.SourceTextCompressed, 0, row.SourceTextCompressed.Length);
        record.SetBytes(6, 0, row.SourceTextHash, 0, row.SourceTextHash.Length);
        record.SetString(7, row.EmbeddingJson);
        return record;
    }

    private async Task MergeVectorSearchParamsAsync(
        IReadOnlyList<(short ResourceTypeId, long ResourceSurrogateId)> evaluated,
        IReadOnlyList<VectorRow> vectors)
    {
        await using var connection = new SqlConnection(_database.ConnectionString);
        await connection.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = "dbo.MergeVectorSearchParams";
        command.CommandType = CommandType.StoredProcedure;
        command.Parameters.Add(new SqlParameter("@Evaluated", SqlDbType.Structured)
        {
            TypeName = "dbo.VectorResourceList",
            Value = evaluated.Select(e => BuildEvaluatedRecord(e.ResourceTypeId, e.ResourceSurrogateId)).ToList(),
        });
        command.Parameters.Add(new SqlParameter("@Vectors", SqlDbType.Structured)
        {
            TypeName = "dbo.VectorSearchParamList",
            // SQL Client requires NULL (not an empty list) for a TVP carrying zero rows.
            Value = vectors.Count == 0 ? null : vectors.Select(BuildVectorRecord).ToList(),
        });
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }
}
