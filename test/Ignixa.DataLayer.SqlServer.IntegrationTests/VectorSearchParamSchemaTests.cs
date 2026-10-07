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
/// Schema version 5: <c>dbo.VectorSearchParam</c>, <c>dbo.EmbeddingModel</c>, their table-valued-parameter
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

    /// <summary>
    /// Pins MergeVectorSearchParams.sql's "delete every version" behavior directly: the DELETE joins
    /// dbo.VectorSearchParam to dbo.Resource by (ResourceTypeId, ResourceSurrogateId) and then to the
    /// evaluated/current set by ResourceId -- not by matching the evaluated surrogate itself -- so a prior
    /// version's orphaned rows are removed even though that prior surrogate was never in <c>@Evaluated</c>.
    /// Every other test in this file that exercises the no-vectors-on-update path
    /// (<see cref="GivenEvaluatedResourceWithNoVectors_WhenMerged_ThenPriorVersionRowsDeleted"/>) only ever
    /// seeds and evaluates the SAME surrogate, so it stays green even if the delete were narrowed to match
    /// on ResourceSurrogateId instead of ResourceId -- this test seeds v1's vectors raw, lets v2 become
    /// current, and evaluates only v2, which the surrogate-matching mutation would NOT delete.
    /// </summary>
    [Fact]
    public async Task GivenVectorsOnAPriorVersion_WhenMergedForTheCurrentVersionWithNoVectors_ThenThePriorVersionsVectorsAreDeleted()
    {
        const string ResourceId = "vector-merge-prior-version-1";
        var (resourceTypeId, v1SurrogateId) = await CreatePatientAsync(ResourceId);
        await InsertVectorRowRawAsync(resourceTypeId, v1SurrogateId);

        await _database.Repository.CreateOrUpdateAsync(BuildPatientWrapper(ResourceId), CancellationToken.None);
        var v2SurrogateId = await _database.ExecuteScalarAsync<long>(
            $"SELECT ResourceSurrogateId FROM dbo.Resource WHERE ResourceId = '{ResourceId}' AND IsHistory = 0");
        v2SurrogateId.ShouldNotBe(v1SurrogateId);

        (await _database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.VectorSearchParam WHERE ResourceSurrogateId = {v1SurrogateId}")).ShouldBe(
            1, "without a row to remove, this test cannot prove the delete matched by ResourceId");

        // v2 is the evaluated/current surrogate but carries no vectors of its own (its semantic text
        // evaluated to empty on this update). Only v1's (the prior, now-historical version's) rows exist
        // to be deleted -- v2's own surrogate was never written to dbo.VectorSearchParam at all.
        await MergeVectorSearchParamsAsync(evaluated: [(resourceTypeId, v2SurrogateId)], vectors: []);

        (await _database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.VectorSearchParam WHERE ResourceSurrogateId = {v1SurrogateId}")).ShouldBe(
            0, "the prior version's vectors must be deleted even though only v2's surrogate was evaluated");
    }

    /// <summary>
    /// Pins the UPDLOCK, HOLDLOCK race guard directly, rather than only through its externally-visible
    /// "stale surrogate" consequence (<see
    /// cref="GivenSurrogateNowHistory_WhenMerged_ThenNoRowsInsertedAndNewerVersionVectorsUntouched"/>, which
    /// would also pass if the hint were removed and replaced by nothing, purely because that test's
    /// concurrent write happens to land before the read). Also exercises the <c>@InitialTranCount > 0</c>
    /// branch: connection A calls the procedure inside its OWN already-open transaction, so the procedure
    /// must neither BEGIN nor COMMIT one of its own, and the lock it takes must outlive the EXEC and persist
    /// until connection A's caller-owned transaction ends.
    /// </summary>
    [Fact]
    public async Task GivenAnOpenOuterTransaction_WhenMergeVectorSearchParamsRunsInsideIt_ThenTheCurrentResourceRowStaysLockedUntilTheOuterTransactionEnds()
    {
        var (resourceTypeId, surrogateId) = await CreatePatientAsync("vector-lock-guard-1");

        await using var connectionA = new SqlConnection(_database.ConnectionString);
        await connectionA.OpenAsync(CancellationToken.None);
        await using var transactionA = (SqlTransaction)await connectionA.BeginTransactionAsync(CancellationToken.None);

        using (var command = connectionA.CreateCommand())
        {
            command.Transaction = transactionA;
            command.CommandText = "dbo.MergeVectorSearchParams";
            command.CommandType = CommandType.StoredProcedure;
            command.Parameters.Add(new SqlParameter("@Evaluated", SqlDbType.Structured)
            {
                TypeName = "dbo.VectorResourceList",
                Value = new List<SqlDataRecord> { BuildEvaluatedRecord(resourceTypeId, surrogateId) },
            });
            command.Parameters.Add(new SqlParameter("@Vectors", SqlDbType.Structured)
            {
                TypeName = "dbo.VectorSearchParamList",
                // SQL Client requires NULL (not an empty list) for a TVP carrying zero rows.
                Value = null,
            });
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }

        try
        {
            await using var connectionB = new SqlConnection(_database.ConnectionString);
            await connectionB.OpenAsync(CancellationToken.None);
            using (var lockTimeoutCommand = connectionB.CreateCommand())
            {
                lockTimeoutCommand.CommandText = "SET LOCK_TIMEOUT 500";
                await lockTimeoutCommand.ExecuteNonQueryAsync(CancellationToken.None);
            }

            using var updateCommand = connectionB.CreateCommand();
            updateCommand.CommandText =
                "UPDATE dbo.Resource SET IsHistory = 1 WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId = @ResourceSurrogateId";
            updateCommand.Parameters.Add("@ResourceTypeId", SqlDbType.SmallInt).Value = resourceTypeId;
            updateCommand.Parameters.Add("@ResourceSurrogateId", SqlDbType.BigInt).Value = surrogateId;

            var ex = await Should.ThrowAsync<SqlException>(() => updateCommand.ExecuteNonQueryAsync(CancellationToken.None));
            ex.Number.ShouldBe(1222, "SQL Server's lock-request-timeout error number");
        }
        finally
        {
            await transactionA.RollbackAsync(CancellationToken.None);
        }
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

    /// <summary>
    /// Pins the SELECT-first fix: an insert-first GetOrCreateEmbeddingModel burns one IDENTITY value per
    /// call for an EXISTING key too (the INSERT attempt consumes the next identity before its own
    /// uniqueness violation is caught), so five lookups of the same key would leave the next genuinely new
    /// key at id 6, not id 2. IDENT_CURRENT is asserted directly rather than inferred solely from the new
    /// key's id, so a failure names the actual counter value rather than requiring the reader to do that
    /// arithmetic themselves.
    /// </summary>
    [Fact]
    public async Task GivenAnExistingModelKeyLookedUpRepeatedly_WhenANewKeyIsThenCreated_ThenNoIdentityValuesWereBurnedByTheLookups()
    {
        var firstId = await GetOrCreateEmbeddingModelAsync("repeated-lookup-key", 1536);
        firstId.ShouldBe((short)1);

        for (var i = 0; i < 4; i++)
        {
            (await GetOrCreateEmbeddingModelAsync("repeated-lookup-key", 1536)).ShouldBe(firstId);
        }

        (await _database.ExecuteScalarAsync<int>("SELECT CAST(IDENT_CURRENT('dbo.EmbeddingModel') AS INT)")).ShouldBe(
            1, "five lookups of an existing key must not advance the IDENTITY counter");

        var secondId = await GetOrCreateEmbeddingModelAsync("new-model-key-after-lookups", 1536);

        secondId.ShouldBe(
            (short)2,
            "an insert-first design would have burned four IDENTITY values on the repeated lookups above, " +
            "landing the next genuinely new key at 6, not 2");
    }

    [Fact]
    public async Task GivenADifferentDimensionsForAnExistingModelKey_WhenGetOrCreate_ThenThrows()
    {
        await GetOrCreateEmbeddingModelAsync("conflicting-model-key", 1536);

        await Should.ThrowAsync<SqlException>(() => GetOrCreateEmbeddingModelAsync("conflicting-model-key", 768));
    }

    /// <summary>
    /// Pins FK_VectorSearchParam_EmbeddingModel (schema v5): a bare INSERT naming an EmbeddingModelId with
    /// no corresponding dbo.EmbeddingModel row must fail loudly rather than orphan the row. This is exactly
    /// the failure mode a stale, process-lifetime id cache would otherwise hide -- see
    /// SqlServerEmbeddingModelRegistry's remarks on why its cache lives on the tenant-scoped reference data
    /// cache instead.
    /// </summary>
    [Fact]
    public async Task GivenANonexistentEmbeddingModelId_WhenAVectorRowIsInserted_ThenTheForeignKeyRejectsIt()
    {
        var (resourceTypeId, surrogateId) = await CreatePatientAsync("vector-fk-violation-1");

        (await _database.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.EmbeddingModel")).ShouldBe(
            0, "the nonexistent id below must not accidentally collide with a row this test itself created");

        const short NonexistentEmbeddingModelId = 999;
        var ex = await Should.ThrowAsync<SqlException>(() => _database.ExecuteNonQueryAsync(
            $"""
            INSERT INTO dbo.VectorSearchParam
                (ResourceTypeId, ResourceSurrogateId, SearchParamId, EmbeddingModelId, ChunkOrdinal, SourceTextCompressed, SourceTextHash, Embedding)
            VALUES
                ({resourceTypeId}, {surrogateId}, 1, {NonexistentEmbeddingModelId}, 0, 0x010203, 0x{Convert.ToHexString(FixedHash(1))}, '{BuildEmbeddingJson(0.01f)}')
            """));

        ex.Number.ShouldBe(547, "SQL Server's foreign-key-violation error number");
        (await _database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.VectorSearchParam WHERE ResourceSurrogateId = {surrogateId}")).ShouldBe(0);
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

    private async Task InsertVectorRowDirectAsync(short resourceTypeId, long surrogateId)
    {
        var modelId = await GetOrCreateEmbeddingModelAsync("test-model", 1536);
        await MergeVectorSearchParamsAsync(
            evaluated: [(resourceTypeId, surrogateId)],
            vectors:
            [
                new VectorRow(resourceTypeId, surrogateId, SearchParamId: 1, modelId, ChunkOrdinal: 0,
                    SourceTextCompressed: [1, 2, 3], SourceTextHash: FixedHash(1), EmbeddingJson: BuildEmbeddingJson(0.01f)),
            ]);
    }

    /// <summary>
    /// Inserts a dbo.VectorSearchParam row by plain INSERT, bypassing MergeVectorSearchParams entirely.
    /// Needed wherever a test must seed vectors on a resource version that is ABOUT to become (or already
    /// is) history: running that insert through the real procedure would have deleted the row being set up
    /// as much as DeleteHistory's sweep is supposed to -- see
    /// GivenVectorsOnHistoryVersions_WhenDeleteHistory_ThenRowsRemoved for the scenario this avoids.
    /// EmbeddingModelId must still reference a real dbo.EmbeddingModel row -- FK_VectorSearchParam_EmbeddingModel
    /// (schema v5) rejects a bare literal the same as production code would.
    /// </summary>
    private async Task InsertVectorRowRawAsync(short resourceTypeId, long surrogateId)
    {
        var modelId = await GetOrCreateEmbeddingModelAsync("test-model", 1536);
        await _database.ExecuteNonQueryAsync(
            $"""
            INSERT INTO dbo.VectorSearchParam
                (ResourceTypeId, ResourceSurrogateId, SearchParamId, EmbeddingModelId, ChunkOrdinal, SourceTextCompressed, SourceTextHash, Embedding)
            VALUES
                ({resourceTypeId}, {surrogateId}, 1, {modelId}, 0, 0x010203, 0x{Convert.ToHexString(FixedHash(1))}, '{BuildEmbeddingJson(0.01f)}')
            """);
    }

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
