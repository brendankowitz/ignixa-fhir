using Ignixa.Abstractions;
using Ignixa.DataLayer.SqlServer.Compression;
using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.DataLayer.SqlServer.Search;
using Ignixa.DataLayer.SqlServer.SemanticSearch;
using Ignixa.Domain.Models;
using Ignixa.Search.Definition;
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql;
using Ignixa.Search.Sql.Ast;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Extensions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IO;
using Shouldly;
using Xunit;
using SearchComparator = Ignixa.Specification.ValueSets.Normative.SearchComparator;
using SearchParamType = Ignixa.Specification.ValueSets.Normative.SearchParamType;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

/// <summary>
/// Task 7: the semantic gate and distance ranking the compiler emits, executed against a real SQL Server 2025
/// <c>vector(1536)</c> column. Vectors are written through the production merge path
/// (<see cref="SqlServerMergeRepository"/> + <see cref="SqlServerVectorIndexWriter"/>) so the rows the query reads
/// are the rows the write path produces. Score and Bundle output belong to Task 8; this pins the row order and
/// the <c>Distance</c> column it is derived from.
/// </summary>
/// <remarks>
/// Embeddings lie on a circle in the first two dimensions, <c>v(θ) = cos θ·e0 + sin θ·e1</c>, with the query at
/// <c>θ = 0</c>, so each row's expected cosine distance is exactly <c>1 - cos θ</c>.
/// </remarks>
#pragma warning disable CA1001 // _cache's only disposable is a SemaphoreSlim; disposed in DisposeAsync.
public class SqlServerCompiledSearchServiceSemanticTests : IAsyncLifetime
#pragma warning restore CA1001
{
    private const string ModelKey = "test-embedding-model|1";

    private static readonly SearchParameterInfo SemanticParam = new(
        "test-semantic",
        "test-semantic",
        SearchParamType.Special,
        new Uri("http://example.org/fhir/SearchParameter/test-semantic"),
        vectorConfig: new VectorSearchConfig(VectorTextExtractionPolicy.Concatenate, 8000, 0m, null, null));

    private static readonly SearchParameterInfo StatusParam = new(
        "test-status", "test-status", SearchParamType.Token, new Uri("http://example.org/fhir/SearchParameter/test-status"));

    private TestTenantDatabase _database = null!;
    private SqlServerSearchIndexReferenceDataCache _cache = null!;
    private SqlServerMergeRepository _mergeRepository = null!;
    private SqlServerSymbolResolver _resolver = null!;
    private SqlServerCompiledSearchService _service = null!;

    public async Task InitializeAsync()
    {
        _database = await TestTenantDatabase.CreateEmptyAsync();
        foreach (var url in new[] { SemanticParam.Url!, StatusParam.Url! })
        {
            await _database.ExecuteNonQueryAsync(
                $"INSERT INTO dbo.SearchParam (Uri, Status, LastUpdated, IsPartiallySupported) VALUES ('{url}', 'active', SYSDATETIMEOFFSET(), 0)");
        }

        _cache = new SqlServerSearchIndexReferenceDataCache(
            _database.SqlExecutionService, _database.TenantId, NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance);
        await _cache.PreloadResourceTypesAsync(CancellationToken.None);

        var compressor = new GzipResourceCompressor(new RecyclableMemoryStreamManager());
        var vectorIndexWriter = new SqlServerVectorIndexWriter(
            _database.SqlExecutionService,
            _database.TenantId,
            compressor,
            _cache,
            new SqlServerEmbeddingModelRegistry(_database.SqlExecutionService, _database.TenantId, _cache, NullLogger<SqlServerEmbeddingModelRegistry>.Instance),
            NullLogger<SqlServerVectorIndexWriter>.Instance);
        _mergeRepository = new SqlServerMergeRepository(
            _database.SqlExecutionService,
            _database.TenantId,
            compressor,
            _cache,
            new SqlServerPostMergeExtensionUpdater(_database.SqlExecutionService, _database.TenantId, NullLogger<SqlServerPostMergeExtensionUpdater>.Instance),
            NullLogger<SqlServerMergeRepository>.Instance,
            vectorIndexWriter);

        _resolver = new SqlServerSymbolResolver(_cache);
        _service = new SqlServerCompiledSearchService(
            _database.SqlExecutionService,
            _database.TenantId,
            _resolver,
            new CompartmentDefinitionManager(FhirVersion.R4),
            new SearchParameterDefinitionManager(FhirVersion.R4.GetSchemaProvider(), NullLogger<SearchParameterDefinitionManager>.Instance),
            compressor,
            NullLogger.Instance);
    }

    public async Task DisposeAsync()
    {
        _cache.Dispose();
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task GivenSemanticAndStatusFilter_WhenCompiledSqlExecuted_ThenGatedRowsAreOrderedByMinimumChunkDistance()
    {
        // Arrange -- "two-chunks" is far on its first chunk but nearest on its second, so it ranks first only if
        // the distance is the minimum over chunks. "wrong-status" is nearer than everything but fails the token
        // filter; "far" (1 - cos 1.2 ≈ 0.64) fails the 0.3 gate.
        await MergeAsync("two-chunks", "final", ChunkAt(0, 1.5), ChunkAt(1, 0.02));
        await MergeAsync("near", "final", ChunkAt(0, 0.1));
        await MergeAsync("mid", "final", ChunkAt(0, 0.5));
        await MergeAsync("far", "final", ChunkAt(0, 1.2));
        await MergeAsync("wrong-status", "amended", ChunkAt(0, 0.05));

        var options = SemanticSearch(maxDistance: 0.3, StatusEquals("final"));

        // Act -- the compiled SQL itself, so the Distance column is visible.
        var compiled = (await new SearchSqlCompiler(_resolver).CreatePlanFromOptionsAsync(
            options,
            "Patient",
            new SearchPlanOptions { Shape = new ResultShape.Matches(new SearchPaging.Offset(new OffsetSpec(0, 10))) })).Compile();
        var rows = await ExecuteAsync(compiled);

        // Assert
        rows.Select(r => r.ResourceId).ShouldBe(["two-chunks", "near", "mid"]);
        rows[0].Distance.ShouldBe(1 - Math.Cos(0.02), tolerance: 1e-4);
        rows[1].Distance.ShouldBe(1 - Math.Cos(0.1), tolerance: 1e-4);
        rows[2].Distance.ShouldBe(1 - Math.Cos(0.5), tolerance: 1e-4);
    }

    [Fact]
    public async Task GivenSemanticAndStatusFilter_WhenSearchStreamAsyncCalled_ThenMatchesArriveInDistanceOrder()
    {
        await MergeAsync("mid", "final", ChunkAt(0, 0.5));
        await MergeAsync("near", "final", ChunkAt(0, 0.1));
        await MergeAsync("wrong-status", "amended", ChunkAt(0, 0.05));

        var results = new List<SearchEntryResult>();
        await foreach (var result in _service.SearchStreamAsync(SemanticSearch(maxDistance: 2, StatusEquals("final")), CancellationToken.None))
        {
            results.Add(result);
        }

        results.Select(r => r.ResourceId).ShouldBe(["near", "mid"]);
        results.ShouldAllBe(r => r.SearchMode == SearchEntryMode.Match);
    }

    [Fact]
    public async Task GivenVectorsLeftOnASupersededVersion_WhenSearched_ThenTheHistoryVersionIsNotReturned()
    {
        // Arrange -- v2 is written without evaluating semantic text (VectorIndices null), which is exactly the
        // state a failed or skipped vector write leaves: v1's vectors survive under v1's surrogate id. The gate
        // must not hand that history surrogate back as a match.
        var v1SurrogateId = await MergeAsync("superseded", "final", ChunkAt(0, 0));
        await MergeWrapperAsync(Wrapper("superseded", "final", vectorIndices: null) with
        {
            VersionId = "2",
            Resource = ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"superseded","active":true}"""),
        });
        (await _database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.VectorSearchParam WHERE ResourceSurrogateId = {v1SurrogateId}")).ShouldBe(
            1, "without a lingering history vector this test proves nothing");

        var results = new List<SearchEntryResult>();
        await foreach (var result in _service.SearchStreamAsync(SemanticSearch(maxDistance: 2), CancellationToken.None))
        {
            results.Add(result);
        }

        results.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenAModelWithNoVectors_WhenSearched_ThenNothingMatchesAndNoModelRowIsCreated()
    {
        await MergeAsync("near", "final", ChunkAt(0, 0.1));
        var prepared = new PreparedVectorQuery(At(0), "never-written-model|9", MaxDistance: 2);
        var options = new SearchOptions
        {
            ResourceType = "Patient",
            Expression = new VectorSearchExpression(SemanticParam, "chest pain").WithPrepared(prepared),
        };

        var results = new List<SearchEntryResult>();
        await foreach (var result in _service.SearchStreamAsync(options, CancellationToken.None))
        {
            results.Add(result);
        }

        results.ShouldBeEmpty();
        (await _database.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.EmbeddingModel WHERE ModelKey = 'never-written-model|9'")).ShouldBe(0);
    }

    private static SearchOptions SemanticSearch(double maxDistance, params Expression[] filters)
    {
        var semantic = new VectorSearchExpression(SemanticParam, "chest pain, nausea")
            .WithPrepared(new PreparedVectorQuery(At(0), ModelKey, maxDistance));
        return new SearchOptions
        {
            ResourceType = "Patient",
            Expression = filters.Length == 0 ? semantic : Expression.And([semantic, .. filters]),
        };
    }

    private static SearchParameterExpression StatusEquals(string code) => new(
        StatusParam,
        new SearchParameterPredicateExpression(StatusParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: code, text: null)));

    private async Task<IReadOnlyList<(string ResourceId, double Distance)>> ExecuteAsync(CompiledSearch compiled)
    {
        var matches = new List<(long Sid, double Distance)>();
        await using (var connection = new SqlConnection(_database.ConnectionString))
        {
            await connection.OpenAsync();

#pragma warning disable CA2100 // The compiler's own output; every value is a bound @pN parameter.
            await using var command = new SqlCommand(compiled.Sql, connection);
#pragma warning restore CA2100
            foreach (var parameter in compiled.Parameters)
            {
                command.Parameters.AddWithValue(parameter.Name, parameter.Value);
            }

            await using var reader = await command.ExecuteReaderAsync();
            var sidOrdinal = reader.GetOrdinal("Sid1");
            var distanceOrdinal = reader.GetOrdinal("Distance");
            while (await reader.ReadAsync())
            {
                matches.Add((reader.GetInt64(sidOrdinal), reader.GetDouble(distanceOrdinal)));
            }
        }

        var rows = new List<(string, double)>(matches.Count);
        foreach (var (sid, distance) in matches)
        {
            var resourceId = await _database.ExecuteScalarAsync<string>(
                $"SELECT ResourceId FROM dbo.Resource WHERE ResourceSurrogateId = {sid}");
            rows.Add((resourceId, distance));
        }

        return rows;
    }

    private async Task<long> MergeAsync(string resourceId, string status, params VectorChunk[] chunks)
        => await MergeWrapperAsync(Wrapper(resourceId, status, [new VectorIndexEntry(SemanticParam.Url!, ModelKey, chunks)]));

    private async Task<long> MergeWrapperAsync(ResourceWrapper wrapper)
    {
        var (transactionId, _) = await _mergeRepository.BeginTransactionAsync(resourceCount: 1, CancellationToken.None);
        await _mergeRepository.MergeResourcesAsync(transactionId, singleTransaction: true, [wrapper], [0], CancellationToken.None);
        await _mergeRepository.CommitTransactionAsync(transactionId, cancellationToken: CancellationToken.None);
        return await _database.ExecuteScalarAsync<long>(
            $"SELECT ResourceSurrogateId FROM dbo.Resource WHERE ResourceId = '{wrapper.ResourceId}' AND IsHistory = 0");
    }

    private static ResourceWrapper Wrapper(string resourceId, string status, IReadOnlyList<VectorIndexEntry>? vectorIndices) =>
        new(
            "Patient", resourceId, "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse($$"""{"resourceType":"Patient","id":"{{resourceId}}"}"""),
            new ResourceRequest("PUT", $"Patient/{resourceId}"))
        {
            SearchIndices = [new SearchIndexEntry(StatusParam, new TokenSearchValue(system: null, code: status, text: null))],
            VectorIndices = vectorIndices,
        };

    private static VectorChunk ChunkAt(short ordinal, double theta) => new(ordinal, $"passage at {theta}", At(theta));

    private static float[] At(double theta)
    {
        var vector = new float[1536];
        vector[0] = (float)Math.Cos(theta);
        vector[1] = (float)Math.Sin(theta);
        return vector;
    }
}
