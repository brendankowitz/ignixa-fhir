using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Ignixa.DataLayer.SqlServer.Compression;
using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.DataLayer.SqlServer.SemanticSearch;
using Ignixa.DataLayer.SqlServer.Tests.Fixtures;
using Ignixa.Domain.Models;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.ValueSets.Normative;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IO;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

/// <summary>
/// Task 6: <see cref="SqlServerVectorIndexWriter"/> and <see cref="SqlServerEmbeddingModelRegistry"/>,
/// exercised through <see cref="SqlServerMergeRepository.MergeResourcesAsync"/> exactly as production
/// wiring calls them (<c>SqlServerRepositoryFactory.CreateRepository</c>). <see
/// cref="VectorSearchParamSchemaTests"/> already covers <c>dbo.MergeVectorSearchParams</c> /
/// <c>dbo.GetOrCreateEmbeddingModel</c> directly with hand-built TVP rows; these tests instead prove the
/// C# writer produces those rows correctly from <see cref="ResourceWrapper.VectorIndices"/> and that
/// <see cref="SqlServerMergeRepository"/> never lets a vector-persistence failure fail the resource write
/// it follows.
/// </summary>
#pragma warning disable CA1001 // _cache's only disposable is a SemaphoreSlim; disposed in DisposeAsync.
public class SqlServerVectorIndexWriterTests : IAsyncLifetime
#pragma warning restore CA1001
{
    private const string SemanticSearchParamUrl = "http://example.org/fhir/SearchParameter/test-semantic";
    private const string UnregisteredSemanticSearchParamUrl = "http://example.org/fhir/SearchParameter/not-registered-semantic";
    private const string EmbeddingModelKey = "test-model|v1";

    private TestTenantDatabase _database = null!;
    private SqlServerSearchIndexReferenceDataCache _cache = null!;
    private GzipResourceCompressor _compressor = null!;
    private RecordingLogger<SqlServerMergeRepository> _mergeLogger = null!;
    private SqlServerMergeRepository _repository = null!;

    public async Task InitializeAsync()
    {
        _database = await TestTenantDatabase.CreateEmptyAsync();
        await RegisterSearchParamAsync(SemanticSearchParamUrl);

        _cache = new SqlServerSearchIndexReferenceDataCache(
            _database.SqlExecutionService, _database.TenantId, NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance);
        await _cache.PreloadResourceTypesAsync(CancellationToken.None);

        _compressor = new GzipResourceCompressor(new RecyclableMemoryStreamManager());
        var extensionUpdater = new SqlServerPostMergeExtensionUpdater(
            _database.SqlExecutionService, _database.TenantId, NullLogger<SqlServerPostMergeExtensionUpdater>.Instance);
        var embeddingModelRegistry = new SqlServerEmbeddingModelRegistry(
            _database.SqlExecutionService, _database.TenantId, NullLogger<SqlServerEmbeddingModelRegistry>.Instance);
        var vectorIndexWriter = new SqlServerVectorIndexWriter(
            _database.SqlExecutionService, _database.TenantId, _compressor, _cache, embeddingModelRegistry,
            NullLogger<SqlServerVectorIndexWriter>.Instance);

        _mergeLogger = new RecordingLogger<SqlServerMergeRepository>();
        _repository = new SqlServerMergeRepository(
            _database.SqlExecutionService, _database.TenantId, _compressor, _cache, extensionUpdater,
            _mergeLogger, vectorIndexWriter);
    }

    public async Task DisposeAsync()
    {
        _cache.Dispose();
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task GivenCreateWithSemanticText_WhenMerged_ThenVectorRowsMatchChunks()
    {
        var wrapper = BuildWrapper(
            "vector-writer-create-1",
            VectorEntry(SemanticSearchParamUrl,
                Chunk(0, "chest pain and nausea for three days", Embedding(0.01f)),
                Chunk(1, "no fever, no shortness of breath", Embedding(0.02f))));

        var surrogateId = await MergeAsync(wrapper);

        (await VectorRowCountAsync(surrogateId)).ShouldBe(2);

        var ordinals = await _database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(DISTINCT ChunkOrdinal) FROM dbo.VectorSearchParam WHERE ResourceSurrogateId = {surrogateId}");
        ordinals.ShouldBe(2);

        // VECTOR_DISTANCE against itself is ~0 only if Embedding round-tripped as a real vector(1536), not
        // opaque text the server merely stored.
        var selfDistances = await _database.ExecuteScalarAsync<double>(
            $"""
            SELECT MAX(CAST(VECTOR_DISTANCE('cosine', Embedding, Embedding) AS FLOAT))
            FROM dbo.VectorSearchParam WHERE ResourceSurrogateId = {surrogateId}
            """);
        selfDistances.ShouldBe(0d, tolerance: 0.0001);
    }

    /// <summary>Review Focus 4: an update whose semantic text becomes empty must delete the prior vectors.</summary>
    [Fact]
    public async Task GivenUpdateRemovingText_WhenMerged_ThenNoVectorRowsRemain()
    {
        const string ResourceId = "vector-writer-remove-text-1";
        var createWrapper = BuildWrapper(ResourceId,
            VectorEntry(SemanticSearchParamUrl, Chunk(0, "some semantic text", Embedding(0.03f))));
        var v1SurrogateId = await MergeAsync(createWrapper);
        (await VectorRowCountAsync(v1SurrogateId)).ShouldBe(1, "without a row to remove, this test cannot prove removal");

        // The resource's semantic text evaluated to empty on this update (not null -- semantic indexing
        // DID run, it just found nothing), so VectorIndices is an empty, non-null list.
        var updateWrapper = createWrapper with
        {
            VersionId = "2",
            Resource = ResourceJsonNode.Parse($$"""{"resourceType":"Patient","id":"{{ResourceId}}","active":true}"""),
            VectorIndices = [],
            SearchIndices = null,
        };
        var v2SurrogateId = await MergeAsync(updateWrapper);
        v2SurrogateId.ShouldNotBe(v1SurrogateId);

        (await VectorRowCountAsync(v1SurrogateId)).ShouldBe(0, "the prior version's vectors must be deleted");
        (await VectorRowCountAsync(v2SurrogateId)).ShouldBe(0);
    }

    [Fact]
    public async Task GivenVectorIndicesNull_WhenMerged_ThenExistingVectorRowsUntouched()
    {
        const string ResourceId = "vector-writer-untouched-1";
        var createWrapper = BuildWrapper(ResourceId,
            VectorEntry(SemanticSearchParamUrl, Chunk(0, "some semantic text", Embedding(0.04f))));
        var v1SurrogateId = await MergeAsync(createWrapper);
        (await VectorRowCountAsync(v1SurrogateId)).ShouldBe(1, "without a row to remove, this test cannot prove it survives");

        // VectorIndices is null (not evaluated at all -- e.g. this write path doesn't run semantic
        // indexing), so this resource must not even appear in @Evaluated; its prior vectors must survive.
        var updateWrapper = createWrapper with
        {
            VersionId = "2",
            Resource = ResourceJsonNode.Parse($$"""{"resourceType":"Patient","id":"{{ResourceId}}","active":true}"""),
            VectorIndices = null,
            SearchIndices = null,
        };
        await MergeAsync(updateWrapper);

        (await VectorRowCountAsync(v1SurrogateId)).ShouldBe(1, "untouched means untouched, even though v1 is now history");
    }

    [Fact]
    public async Task GivenSemanticEntry_WhenMerged_ThenNoStringSearchParamRow()
    {
        var searchParameter = BuildSemanticSearchParameter(SemanticSearchParamUrl);
        var wrapper = BuildWrapper(
            "vector-writer-no-string-row-1",
            VectorEntry(SemanticSearchParamUrl, Chunk(0, "chest pain and nausea", Embedding(0.05f)))) with
        {
            SearchIndices = [new SearchIndexEntry(searchParameter, new StringSearchValue("chest pain and nausea"))],
        };

        var surrogateId = await MergeAsync(wrapper);

        (await VectorRowCountAsync(surrogateId)).ShouldBe(1);
        (await _database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.StringSearchParam WHERE ResourceSurrogateId = {surrogateId}")).ShouldBe(0);
    }

    [Fact]
    public async Task GivenWriterThrows_WhenMerged_ThenResourceCommittedAndErrorLogged()
    {
        // The URL below is never registered in dbo.SearchParam, so SqlServerVectorIndexWriter's
        // SearchParamId lookup fails with InvalidOperationException -- proving a writer failure is caught
        // by SqlServerMergeRepository (logged, not rethrown) rather than silently dropped.
        var wrapper = BuildWrapper(
            "vector-writer-throws-1",
            VectorEntry(UnregisteredSemanticSearchParamUrl, Chunk(0, "orphaned semantic text", Embedding(0.06f))));

        var surrogateId = await MergeAsync(wrapper);

        (await _database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.Resource WHERE ResourceSurrogateId = {surrogateId}")).ShouldBe(
            1, "the core resource write must commit even though vector persistence failed");

        var error = _mergeLogger.Errors.ShouldHaveSingleItem();
        error.ShouldContain(_database.TenantId.ToString(CultureInfo.InvariantCulture));
        error.ShouldContain(surrogateId.ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task GivenPassage_WhenStored_ThenDecompressedPassageAndHashRoundTrip()
    {
        const string Passage = "Patient reports intermittent chest pain and nausea over the past three days.";
        var wrapper = BuildWrapper(
            "vector-writer-passage-roundtrip-1",
            VectorEntry(SemanticSearchParamUrl, Chunk(0, Passage, Embedding(0.07f))));

        var surrogateId = await MergeAsync(wrapper);

        var compressed = await _database.ExecuteScalarBytesAsync(
            $"SELECT SourceTextCompressed FROM dbo.VectorSearchParam WHERE ResourceSurrogateId = {surrogateId}");
        var storedHash = await _database.ExecuteScalarBytesAsync(
            $"SELECT SourceTextHash FROM dbo.VectorSearchParam WHERE ResourceSurrogateId = {surrogateId}");

        compressed.ShouldNotBeNull();
        storedHash.ShouldNotBeNull();

        var decompressed = _compressor.DecompressBytes(compressed);
        Encoding.UTF8.GetString(decompressed.Span).ShouldBe(Passage);

        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(Passage));
        storedHash.ShouldBe(expectedHash);
    }

    /// <summary>
    /// Verifies "R"-format floats that render in exponent notation (e.g. "1E-05") survive
    /// <c>CAST(... AS vector(1536))</c> and back out through <c>CAST(Embedding AS nvarchar(max))</c>.
    /// </summary>
    [Fact]
    public async Task GivenEmbeddingWithExponentFormatFloats_WhenWritten_ThenStoredValuesRoundTrip()
    {
        var embedding = new float[1536];
        for (var i = 0; i < embedding.Length; i++)
        {
            embedding[i] = ((i % 200) - 100) * 0.0001f;
        }
        // Force specific exponent-notation renderings of float.ToString("R") at known positions.
        embedding[0] = 1e-5f;
        embedding[1] = -1e-5f;
        embedding[2] = 1.2345e-6f;
        embedding[3] = 0f;
        embedding[4] = -0f;
        embedding[5] = 9.999999e-5f;

        var wrapper = BuildWrapper(
            "vector-writer-exponent-roundtrip-1",
            VectorEntry(SemanticSearchParamUrl, new VectorChunk(0, "exponent round trip passage", embedding)));

        var surrogateId = await MergeAsync(wrapper);

        var embeddingJson = await _database.ExecuteScalarAsync<string>(
            $"SELECT CAST(Embedding AS NVARCHAR(MAX)) FROM dbo.VectorSearchParam WHERE ResourceSurrogateId = {surrogateId}");

        var roundTripped = embeddingJson.Trim('[', ']')
            .Split(',')
            .Select(token => float.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture))
            .ToArray();

        roundTripped.Length.ShouldBe(embedding.Length);
        for (var i = 0; i < embedding.Length; i++)
        {
            roundTripped[i].ShouldBe(embedding[i], tolerance: 1e-6, customMessage: $"index {i}");
        }
    }

    /// <summary>
    /// Pins the retry-once-on-1205 contract documented in <c>MergeVectorSearchParams.sql</c>'s header,
    /// against a REAL SQL Server deadlock rather than a fabricated <see cref="SqlException"/> -- there is
    /// no public <see cref="SqlException"/> constructor, and the codebase's own precedent for forcing
    /// error 1205 in a test (<c>DeadlockAllocationRecoveryTests.GivenADeadlockVictim_...</c>) induces a
    /// genuine deadlock via a trigger-and-blocking-transaction pair rather than reflection-fabricating the
    /// exception; a substituted <see cref="ISqlExecutionService"/> seam (NSubstitute) was considered and
    /// rejected -- <see cref="SqlServerSearchIndexReferenceDataCache"/> is sealed with no non-DB seeding
    /// hook, so a true unit-level substitution would require adding an interface to
    /// <see cref="SqlServerEmbeddingModelRegistry"/> purely to support a test double, which the coding
    /// standard flags as a design smell (an interface created only for a test double). This test instead
    /// reproduces the exact lock-order race <c>MergeVectorSearchParams.sql</c>'s header describes --
    /// connection B holds a lock the writer's INSERT trigger needs, then requests the
    /// <c>dbo.Resource</c> lock the writer already holds, closing the cycle -- and proves the writer's
    /// own retry (not a second test-driven call) is what makes <see cref="SqlServerVectorIndexWriter.WriteAsync"/>
    /// still succeed afterward.
    /// </summary>
    [Fact]
    public async Task GivenDeadlockOnFirstAttempt_WhenWritten_ThenRetriedOnce()
    {
        var (resourceTypeId, surrogateId) = await CreatePlainPatientAsync("vector-writer-deadlock-1");

        await _database.ExecuteNonQueryAsync("""
            CREATE TABLE dbo.VectorDeadlockGate (Id INT NOT NULL PRIMARY KEY, Value INT NOT NULL);
            INSERT INTO dbo.VectorDeadlockGate VALUES (1, 0);
            """);
        await _database.ExecuteNonQueryAsync($"""
            CREATE TRIGGER dbo.DeadlockOneVector ON dbo.VectorSearchParam AFTER INSERT AS
            BEGIN
              IF EXISTS (SELECT 1 FROM inserted WHERE ResourceSurrogateId = {surrogateId})
              BEGIN
                SET DEADLOCK_PRIORITY LOW;
                UPDATE dbo.VectorDeadlockGate SET Value = Value + 1 WHERE Id = 1;
              END
            END
            """);

        var writerLogger = new RecordingLogger<SqlServerVectorIndexWriter>();
        var embeddingModelRegistry = new SqlServerEmbeddingModelRegistry(
            _database.SqlExecutionService, _database.TenantId, NullLogger<SqlServerEmbeddingModelRegistry>.Instance);
        var writer = new SqlServerVectorIndexWriter(
            _database.SqlExecutionService, _database.TenantId, _compressor, _cache, embeddingModelRegistry, writerLogger);

        await using var blocker = new SqlConnection(_database.ConnectionString);
        await blocker.OpenAsync();
        await using var transaction = (SqlTransaction)await blocker.BeginTransactionAsync();
        using (var gate = new SqlCommand(
            "SET DEADLOCK_PRIORITY HIGH; UPDATE dbo.VectorDeadlockGate SET Value = Value + 1 WHERE Id = 1;",
            blocker, transaction))
        {
            await gate.ExecuteNonQueryAsync();
        }

        var evaluated = new List<(short ResourceTypeId, long ResourceSurrogateId, IReadOnlyList<VectorIndexEntry> Entries)>
        {
            (resourceTypeId, surrogateId, [VectorEntry(SemanticSearchParamUrl, Chunk(0, "deadlock passage", Embedding(0.08f)))]),
        };
        var write = writer.WriteAsync(evaluated, CancellationToken.None);

        await WaitForBlockedWriteAsync(blocker.ServerProcessId);

        // CA2100 suppressed: resourceTypeId/surrogateId are server-generated values from this test's own
        // setup, never external input -- matching the suppression rationale used throughout this fixture
        // and DeadlockAllocationRecoveryTests.cs for test-controlled SQL text.
#pragma warning disable CA2100
        using (var cycle = new SqlCommand(
            $"SELECT COUNT(*) FROM dbo.Resource WITH (UPDLOCK, HOLDLOCK) WHERE ResourceTypeId = {resourceTypeId} AND ResourceSurrogateId = {surrogateId};",
            blocker, transaction))
#pragma warning restore CA2100
        {
            cycle.CommandTimeout = 30;
            await cycle.ExecuteScalarAsync();
        }
        await transaction.CommitAsync();

        // The writer's own internal retry -- not a second call from this test -- must be what makes this
        // complete without throwing.
        await write;

        writerLogger.Messages(LogLevel.Warning).ShouldHaveSingleItem().ShouldContain("deadlocked");
        (await VectorRowCountAsync(surrogateId)).ShouldBe(1);
    }

    private async Task WaitForBlockedWriteAsync(int blockerSessionId)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(20))
        {
            var blocked = await _database.ExecuteScalarAsync<int>(
                $"SELECT COUNT(*) FROM sys.dm_exec_requests WHERE database_id = DB_ID() AND blocking_session_id = {blockerSessionId};");
            if (blocked > 0)
            {
                return;
            }
            await Task.Delay(25);
        }
        throw new TimeoutException("The vector writer did not reach the SQL deadlock gate.");
    }

    private async Task<(short ResourceTypeId, long SurrogateId)> CreatePlainPatientAsync(string resourceId)
    {
        var wrapper = new ResourceWrapper(
            "Patient", resourceId, "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse($$"""{"resourceType":"Patient","id":"{{resourceId}}"}"""),
            new ResourceRequest("PUT", $"Patient/{resourceId}"));
        var surrogateId = await MergeAsync(wrapper);
        var resourceTypeId = await _database.ExecuteScalarAsync<short>(
            "SELECT ResourceTypeId FROM dbo.ResourceType WHERE Name = 'Patient'");
        return (resourceTypeId, surrogateId);
    }

    private async Task RegisterSearchParamAsync(string url) =>
        await _database.ExecuteNonQueryAsync(
            $"INSERT INTO dbo.SearchParam (Uri, Status, LastUpdated, IsPartiallySupported) VALUES ('{url}', 'active', SYSDATETIMEOFFSET(), 0)");

    private async Task<long> MergeAsync(ResourceWrapper wrapper)
    {
        var (transactionId, _) = await _repository.BeginTransactionAsync(resourceCount: 1, CancellationToken.None);
        await _repository.MergeResourcesAsync(transactionId, singleTransaction: true, [wrapper], [0], CancellationToken.None);
        await _repository.CommitTransactionAsync(transactionId, cancellationToken: CancellationToken.None);
        return await _database.ExecuteScalarAsync<long>(
            $"SELECT ResourceSurrogateId FROM dbo.Resource WHERE ResourceId = '{wrapper.ResourceId}' AND IsHistory = 0");
    }

    private Task<int> VectorRowCountAsync(long surrogateId) =>
        _database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM dbo.VectorSearchParam WHERE ResourceSurrogateId = {surrogateId}");

    private static ResourceWrapper BuildWrapper(string resourceId, params VectorIndexEntry[] entries) =>
        new(
            "Patient", resourceId, "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse($$"""{"resourceType":"Patient","id":"{{resourceId}}"}"""),
            new ResourceRequest("PUT", $"Patient/{resourceId}"))
        {
            VectorIndices = entries,
        };

    private static VectorIndexEntry VectorEntry(string url, params VectorChunk[] chunks) =>
        new(new Uri(url), EmbeddingModelKey, chunks);

    private static VectorChunk Chunk(short ordinal, string passage, float[] embedding) =>
        new(ordinal, passage, embedding);

    private static SearchParameterInfo BuildSemanticSearchParameter(string url) =>
        new("test-semantic", "test-semantic", SearchParamType.Special, new Uri(url),
            vectorConfig: new VectorSearchConfig(VectorTextExtractionPolicy.Concatenate, 8000, 0m, null, null));

    /// <summary>
    /// A deterministic, distinguishable-by-seed 1536-dimension embedding. Not L2-normalized (nothing here
    /// needs a unit vector); VECTOR_DISTANCE('cosine', v, v) is 0 for any non-zero vector against itself.
    /// </summary>
    private static float[] Embedding(float seed) =>
        Enumerable.Range(0, 1536).Select(i => seed + (i * 0.0001f)).ToArray();
}
