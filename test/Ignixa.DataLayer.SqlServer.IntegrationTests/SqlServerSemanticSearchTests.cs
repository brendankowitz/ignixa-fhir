using Ignixa.Abstractions;
using Ignixa.Application.Features.SemanticSearch;
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
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IO;
using Shouldly;
using Xunit;
using SearchComparator = Ignixa.Specification.ValueSets.Normative.SearchComparator;
using SearchParamType = Ignixa.Specification.ValueSets.Normative.SearchParamType;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

/// <summary>
/// Task 8: <see cref="SemanticQueryPreparer"/> (Application) wired against
/// <see cref="SqlServerCompiledSearchService"/> (DataLayer) -- a search's query text is embedded once, the
/// gate/ranking from Task 7 runs with that embedding, and the resulting <see cref="SearchEntryResult.Score"/>
/// is read back off real rows. Deliberately placed here, not in Ignixa.Application.Tests, because this is
/// the one place both the Application preparer and a real SQL Server <c>vector(1536)</c> column are
/// reachable together -- see Review Focus #2 in the slice-1 plan, which specifically requires exercising
/// both pieces across a real paging seam.
/// </summary>
#pragma warning disable CA1001 // _cache's only disposable is a SemaphoreSlim; disposed in DisposeAsync.
public class SqlServerSemanticSearchTests : IAsyncLifetime
#pragma warning restore CA1001
{
    private const string ModelName = "test-embedding-model";
    private const string ModelVersion = "1";
    private const string ModelKey = ModelName + "|" + ModelVersion;

    /// <summary>
    /// A real tokenizer model name for <see cref="SemanticTextChunker"/>'s constructor, distinct from
    /// <see cref="ModelName"/> above: that constant identifies the (fake, test-only) embedding model
    /// these tests store vectors under, but the chunker's constructor requires a name
    /// <see cref="Microsoft.ML.Tokenizers.TiktokenTokenizer.CreateForModel"/> actually recognizes to pick
    /// a BPE vocabulary -- it has no bearing on the stored <see cref="ModelKey"/>'s identity.
    /// </summary>
    private const string TokenizerModelName = "text-embedding-3-small";

    private static readonly SearchParameterInfo StatusParam = new(
        "test-status", "test-status", SearchParamType.Token, new Uri("http://example.org/fhir/SearchParameter/test-status"));

    private static readonly SearchParameterInfo SubjectParam =
        new SearchParameterDefinitionManager(FhirVersion.R4.GetSchemaProvider(), NullLogger<SearchParameterDefinitionManager>.Instance)
            .GetSearchParameter("Observation", "subject");

    private TestTenantDatabase _database = null!;
    private SqlServerSearchIndexReferenceDataCache _cache = null!;
    private SqlServerMergeRepository _mergeRepository = null!;
    private SqlServerSymbolResolver _resolver = null!;
    private SqlServerCompiledSearchService _service = null!;

    public async Task InitializeAsync()
    {
        _database = await TestTenantDatabase.CreateEmptyAsync();
        foreach (var url in new[] { SemanticParam(0m).Url!, StatusParam.Url!, SubjectParam.Url! })
        {
            await _database.ExecuteNonQueryAsync(
                $"INSERT INTO dbo.SearchParam (Uri, Status, LastUpdated, IsPartiallySupported) VALUES ('{url}', 'active', SYSDATETIMEOFFSET(), 0)");
        }

        // CreateEmptyAsync seeds only "Patient" (see its own remarks: the write path's on-demand
        // GetOrCreateResourceTypeIdAsync -- the production route every resource type but "Patient"
        // normally takes -- is reached through SqlServerFhirRepository.CreateOrUpdateAsync, not through
        // SqlServerMergeRepository.MergeResourcesAsync called directly, which is what MergeWrapperAsync
        // below does. "Observation" and "Patient" (the include target) both need a row up front here.
        await _database.ExecuteNonQueryAsync("INSERT INTO dbo.ResourceType (Name) VALUES ('Observation')");

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
    public async Task GivenThreeObservations_WhenSemanticSearched_ThenOrderedByDistanceWithScores()
    {
        // Arrange -- distances (1 - cos θ) are strictly increasing with θ for θ in [0, π]: near < mid < far.
        await MergeAsync("near", "final", ChunkAt(0, 0.1));
        await MergeAsync("mid", "final", ChunkAt(0, 0.5));
        await MergeAsync("far", "final", ChunkAt(0, 1.2));

        using var generator = new FixedEmbeddingGenerator(At(0));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var preparer = new SemanticQueryPreparer(generator, Chunker(), cache, QueryOptions());
        var prepared = await preparer.PrepareAsync(SemanticSearch(minimumScore: 0m), CancellationToken.None);

        var results = await CollectAsync(prepared);

        results.Select(r => r.ResourceId).ShouldBe(["near", "mid", "far"]);

        var distanceByResourceId = await QueryDistancesAsync(prepared);
        foreach (var result in results)
        {
            result.Score.ShouldNotBeNull();
            result.Score!.Value.ShouldBe(1 - (distanceByResourceId[result.ResourceId] / 2), tolerance: 1e-9);
        }
    }

    [Fact]
    public async Task GivenMinimumScore_WhenSearched_ThenBelowThresholdExcluded()
    {
        // Arrange -- "close" (distance ≈ 1 - cos 0.1 ≈ 0.005) clears a 0.6 gate (maxDistance 0.8); "far"
        // (distance ≈ 1 - cos 1.5 ≈ 0.929) does not.
        await MergeAsync("close", "final", ChunkAt(0, 0.1));
        await MergeAsync("far", "final", ChunkAt(0, 1.5));

        using var generator = new FixedEmbeddingGenerator(At(0));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var preparer = new SemanticQueryPreparer(generator, Chunker(), cache, QueryOptions());
        var prepared = await preparer.PrepareAsync(SemanticSearch(minimumScore: 0.6m), CancellationToken.None);

        var results = await CollectAsync(prepared);

        results.Select(r => r.ResourceId).ShouldBe(["close"]);
    }

    [Fact]
    public async Task GivenSemanticAndStatusFilter_WhenSearched_ThenNonMatchingStatusExcluded()
    {
        await MergeAsync("final-match", "final", ChunkAt(0, 0.1));
        await MergeAsync("amended-match", "amended", ChunkAt(0, 0.05));

        using var generator = new FixedEmbeddingGenerator(At(0));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var preparer = new SemanticQueryPreparer(generator, Chunker(), cache, QueryOptions());
        var options = SemanticSearch(minimumScore: 0m, StatusEquals("final"));
        var prepared = await preparer.PrepareAsync(options, CancellationToken.None);

        var results = await CollectAsync(prepared);

        results.Select(r => r.ResourceId).ShouldBe(["final-match"]);
        results[0].Score.ShouldNotBeNull();
    }

    [Fact]
    public async Task GivenSemanticWithInclude_WhenSearched_ThenIncludedEntriesHaveNoScore()
    {
        await MergeAsync("patient-1", skipVectors: true, resourceType: "Patient");
        await MergeObservationAsync("obs-1", "final", subjectId: "patient-1", ChunkAt(0, 0.1));

        var include = new IncludeExpression(
            ["Observation"], SubjectParam, "Observation", "Patient", null, wildCard: false, reversed: false, iterate: false);

        using var generator = new FixedEmbeddingGenerator(At(0));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var preparer = new SemanticQueryPreparer(generator, Chunker(), cache, QueryOptions());
        var options = SemanticSearch(minimumScore: 0m);
        options.Include = [include];
        var prepared = await preparer.PrepareAsync(options, CancellationToken.None);

        var results = await CollectAsync(prepared);

        var match = results.Single(r => r.ResourceId == "obs-1");
        match.SearchMode.ShouldBe(SearchEntryMode.Match);
        match.Score.ShouldNotBeNull();

        var included = results.Single(r => r.ResourceId == "patient-1");
        included.SearchMode.ShouldBe(SearchEntryMode.Include);
        included.Score.ShouldBeNull();
    }

    /// <summary>
    /// Review Focus #2: a non-deterministic provider must not skip or repeat rows across a page seam.
    /// <see cref="DriftingEmbeddingGenerator"/> returns a slightly different vector on every call it
    /// actually receives, so if <see cref="SemanticQueryPreparer"/>'s <see cref="IMemoryCache"/> did not
    /// de-duplicate the three <see cref="SemanticQueryPreparer.PrepareAsync"/> calls below (one per page,
    /// one for the ground-truth full set) onto one embedding, the three result sets would be ranked by
    /// three different vectors and the page union would not equal the full set.
    /// </summary>
    [Fact]
    public async Task GivenNonDeterministicGenerator_WhenPagingTwoPages_ThenUnionHasNoDuplicatesOrGaps()
    {
        await MergeAsync("obs-0", "final", ChunkAt(0, 0.10));
        await MergeAsync("obs-1", "final", ChunkAt(0, 0.20));
        await MergeAsync("obs-2", "final", ChunkAt(0, 0.30));
        await MergeAsync("obs-3", "final", ChunkAt(0, 0.40));

        using var generator = new DriftingEmbeddingGenerator();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var preparer = new SemanticQueryPreparer(generator, Chunker(), cache, QueryOptions());

        var page1Options = SemanticSearch(minimumScore: 0m);
        page1Options.MaxItemCount = 2;
        var page1Prepared = await preparer.PrepareAsync(page1Options, CancellationToken.None);
        var page1 = await CollectAsync(page1Prepared);

        var page2Options = SemanticSearch(minimumScore: 0m);
        page2Options.MaxItemCount = 2;
        page2Options.ContinuationToken = Ignixa.Search.Models.ContinuationToken.Encode(offset: 2, count: 2);
        var page2Prepared = await preparer.PrepareAsync(page2Options, CancellationToken.None);
        var page2 = await CollectAsync(page2Prepared);

        var fullOptions = SemanticSearch(minimumScore: 0m);
        fullOptions.MaxItemCount = 10;
        var fullPrepared = await preparer.PrepareAsync(fullOptions, CancellationToken.None);
        var full = await CollectAsync(fullPrepared);

        // The cache intercepted every call after the first: the drifting generator's non-determinism
        // never actually reached the SQL layer.
        generator.CallCount.ShouldBe(1);

        var pagedIds = page1.Select(r => r.ResourceId).Concat(page2.Select(r => r.ResourceId)).ToList();
        var fullIds = full.Select(r => r.ResourceId).ToList();

        pagedIds.Count.ShouldBe(fullIds.Count, "no duplicates and no gaps");
        pagedIds.Distinct().Count().ShouldBe(pagedIds.Count, "no duplicates across the page seam");
        pagedIds.ToHashSet().ShouldBeSubsetOf(fullIds.ToHashSet());
        fullIds.ToHashSet().ShouldBeSubsetOf(pagedIds.ToHashSet());
        pagedIds.ShouldBe(fullIds, "the page union preserves the single ranked order");
    }

    private static SearchOptions SemanticSearch(decimal minimumScore, params Expression[] filters)
    {
        var semantic = new VectorSearchExpression(SemanticParam(minimumScore), "chest pain, nausea");
        return new SearchOptions
        {
            ResourceType = "Observation",
            Expression = filters.Length == 0 ? semantic : Expression.And([semantic, .. filters]),
        };
    }

    private static SearchParameterExpression StatusEquals(string code) => new(
        StatusParam,
        new SearchParameterPredicateExpression(StatusParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: code, text: null)));

    private static SearchParameterInfo SemanticParam(decimal minimumScore) => new(
        "test-semantic",
        "test-semantic",
        SearchParamType.Special,
        new Uri("http://example.org/fhir/SearchParameter/test-semantic"),
        vectorConfig: new VectorSearchConfig(VectorTextExtractionPolicy.Concatenate, 8000, minimumScore, null, null));

    private static VectorSearchOptions QueryOptions() => new()
    {
        Enabled = true,
        Embedding = new VectorSearchEmbeddingOptions { ModelName = ModelName, ModelVersion = ModelVersion },
        Query = new VectorSearchQueryOptions { EmbeddingCacheMinutes = 10 },
    };

    private static SemanticTextChunker Chunker() => new(TokenizerModelName);

    private async Task<List<SearchEntryResult>> CollectAsync(SearchOptions options)
    {
        var results = new List<SearchEntryResult>();
        await foreach (var result in _service.SearchStreamAsync(options, CancellationToken.None))
        {
            results.Add(result);
        }

        return results;
    }

    /// <summary>
    /// Ground truth for Score assertions: compiles <paramref name="prepared"/> independently of
    /// <see cref="SqlServerCompiledSearchService"/> and reads the "Distance" column SQL itself computed,
    /// keyed by ResourceId, so a Score assertion checks the service's math against the database's own
    /// <c>VECTOR_DISTANCE</c> result rather than against a value this test also had to compute by hand.
    /// </summary>
    private async Task<Dictionary<string, double>> QueryDistancesAsync(SearchOptions options)
    {
        var compiled = (await new SearchSqlCompiler(_resolver).CreatePlanFromOptionsAsync(
            options,
            options.ResourceType,
            new SearchPlanOptions { Shape = new ResultShape.Matches(new SearchPaging.Offset(new OffsetSpec(0, 100))) })).Compile();

        var bySurrogateId = new Dictionary<long, double>();
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
                bySurrogateId[reader.GetInt64(sidOrdinal)] = reader.GetDouble(distanceOrdinal);
            }
        }

        var byResourceId = new Dictionary<string, double>();
        foreach (var (surrogateId, distance) in bySurrogateId)
        {
            var resourceId = await _database.ExecuteScalarAsync<string>(
                $"SELECT ResourceId FROM dbo.Resource WHERE ResourceSurrogateId = {surrogateId}");
            byResourceId[resourceId] = distance;
        }

        return byResourceId;
    }

    private async Task MergeAsync(string resourceId, string status, params VectorChunk[] chunks)
        => await MergeWrapperAsync(Wrapper("Observation", resourceId, status, [new VectorIndexEntry(SemanticParam(0m).Url!, ModelKey, chunks)]));

    private async Task MergeAsync(string resourceId, bool skipVectors, string resourceType)
        => await MergeWrapperAsync(Wrapper(resourceType, resourceId, status: null, vectorIndices: skipVectors ? null : []));

    private async Task MergeObservationAsync(string resourceId, string status, string subjectId, params VectorChunk[] chunks)
    {
        var wrapper = new ResourceWrapper(
            "Observation", resourceId, "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse($$"""{"resourceType":"Observation","id":"{{resourceId}}"}"""),
            new ResourceRequest("PUT", $"Observation/{resourceId}"))
        {
            SearchIndices =
            [
                new SearchIndexEntry(StatusParam, new TokenSearchValue(system: null, code: status, text: null)),
                new SearchIndexEntry(SubjectParam, new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: "Patient", resourceId: subjectId)),
            ],
            VectorIndices = [new VectorIndexEntry(SemanticParam(0m).Url!, ModelKey, chunks)],
        };
        await MergeWrapperAsync(wrapper);
    }

    private async Task MergeWrapperAsync(ResourceWrapper wrapper)
    {
        var (transactionId, _) = await _mergeRepository.BeginTransactionAsync(resourceCount: 1, CancellationToken.None);
        await _mergeRepository.MergeResourcesAsync(transactionId, singleTransaction: true, [wrapper], [0], CancellationToken.None);
        await _mergeRepository.CommitTransactionAsync(transactionId, cancellationToken: CancellationToken.None);
    }

    private static ResourceWrapper Wrapper(string resourceType, string resourceId, string? status, IReadOnlyList<VectorIndexEntry>? vectorIndices) =>
        new(
            resourceType, resourceId, "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse($$"""{"resourceType":"{{resourceType}}","id":"{{resourceId}}"}"""),
            new ResourceRequest("PUT", $"{resourceType}/{resourceId}"))
        {
            SearchIndices = status is null ? null : [new SearchIndexEntry(StatusParam, new TokenSearchValue(system: null, code: status, text: null))],
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

    /// <summary>Always returns the same embedding, for tests that need a known, stable query vector.</summary>
    private sealed class FixedEmbeddingGenerator(ReadOnlyMemory<float> vector) : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var results = new GeneratedEmbeddings<Embedding<float>>();
            foreach (var _ in values)
            {
                results.Add(new Embedding<float>(vector));
            }

            return Task.FromResult(results);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// Simulates a non-deterministic embedding provider: every call it actually receives returns a
    /// vector at a slightly different angle than the last. <see cref="SemanticQueryPreparer"/>'s cache is
    /// what should keep this generator from ever being called more than once for the same query text.
    /// </summary>
    private sealed class DriftingEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        private int _callCount;

        public int CallCount => _callCount;

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var callIndex = Interlocked.Increment(ref _callCount);
            var vector = At(0.001 * callIndex);

            var results = new GeneratedEmbeddings<Embedding<float>>();
            foreach (var _ in values)
            {
                results.Add(new Embedding<float>(vector));
            }

            return Task.FromResult(results);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
