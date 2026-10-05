using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Domain.Models;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

/// <summary>
/// End-to-end validation, at a production-representative scale, that concurrent first-writes
/// against a cold SqlServerSearchIndexReferenceDataCache do not drop search-parameter rows (the
/// originally-observed production regression -- see
/// docs/superpowers/specs/2026-07-20-sqlserver-search-param-cache-race-fix-design.md). This test
/// exercises SqlServerMergeRepository.MergeResourcesAsync directly (not just the cache in
/// isolation, unlike SqlServerSearchIndexReferenceDataCacheTests' concurrency tests), through the
/// same TestTenantDatabase fixture used by every other SqlServerMergeRepository test -- which
/// deliberately does not eagerly preload search parameters, so the lazy Ensure*PreloadedAsync path
/// is what's actually under test here. It validates correctness and completeness under real
/// concurrent load; it does NOT deterministically reproduce the narrow mid-population-loop race
/// window itself -- that in-memory insert loop completes on a microsecond scale, so this test
/// cannot guarantee landing a reader inside it under realistic timing. See
/// SqlServerSearchIndexReferenceDataCacheTests.GivenALoadPausedMidPopulation_... for the
/// deterministic reproduction of that specific race, using an injectable test hook.
/// </summary>
public sealed class SqlServerMergeRepositoryConcurrentColdCacheTests : IAsyncLifetime
{
    // 200 rows and 40 concurrent writers exercise the cold-cache load path at a
    // production-representative scale to validate correctness and completeness under real
    // concurrent load. This does NOT guarantee reproducing the narrow mid-population-loop race
    // window -- that in-memory insert loop completes on a microsecond scale, so a still-broken
    // guard is not reliably caught by scale/timing alone.
    private const int SearchParamCount = 200;
    private const int ConcurrentWriteCount = 40;

    private TestTenantDatabase _database = null!;

    public async Task InitializeAsync()
    {
        _database = await TestTenantDatabase.CreateSqlServerFhirRepositoryAsync();

        var values = string.Join(",", Enumerable.Range(0, SearchParamCount)
            .Select(i => $"('{ConcurrencyTestSearchParamUrl(i)}', 'active', SYSDATETIMEOFFSET(), 0)"));
        await _database.ExecuteNonQueryAsync(
            $"INSERT INTO dbo.SearchParam (Uri, Status, LastUpdated, IsPartiallySupported) VALUES {values}");
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    private static string ConcurrencyTestSearchParamUrl(int index) =>
        $"http://example.org/concurrency-test-param-{index}";

    [Fact]
    public async Task GivenAColdCache_WhenManyResourcesAreMergedConcurrently_ThenEveryTokenSearchParamRowLands()
    {
        var writes = Enumerable.Range(0, ConcurrentWriteCount).Select(async i =>
        {
            var paramIndex = i % SearchParamCount;
            var searchParameter = new SearchParameterInfo(
                $"concurrency-test-param-{paramIndex}",
                $"concurrency-test-param-{paramIndex}",
                SearchParamType.Token,
                new Uri(ConcurrencyTestSearchParamUrl(paramIndex)));
            var tokenValue = new TokenSearchValue(
                system: null, code: $"concurrency-code-{i}", text: null,
                identifierTypeSystem: null, identifierTypeCode: null);

            var resourceId = $"concurrency-test-patient-{i}";
            var resourceJson = ResourceJsonNode.Parse(
                $$"""{"resourceType":"Patient","id":"{{resourceId}}"}""");
            var wrapper = new ResourceWrapper(
                "Patient", resourceId, "1", DateTimeOffset.UtcNow, resourceJson,
                new ResourceRequest("PUT", $"Patient/{resourceId}"))
            {
                SearchIndices = [new SearchIndexEntry(searchParameter, tokenValue)]
            };

            var (transactionId, _) = await _database.MergeRepository.BeginTransactionAsync(
                resourceCount: 1, CancellationToken.None);
            await _database.MergeRepository.MergeResourcesAsync(
                transactionId, singleTransaction: true, [wrapper], [0], CancellationToken.None);
            await _database.MergeRepository.CommitTransactionAsync(
                transactionId, cancellationToken: CancellationToken.None);
        });

        await Task.WhenAll(writes);

        var tokenRowCount = await _database.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.TokenSearchParam WHERE Code LIKE 'concurrency-code-%'");
        tokenRowCount.ShouldBe(ConcurrentWriteCount,
            "every concurrently-merged resource's token search parameter must land -- a lower " +
            "count means the cache raced and silently dropped rows for a search parameter that " +
            "hadn't finished loading yet");
    }
}
