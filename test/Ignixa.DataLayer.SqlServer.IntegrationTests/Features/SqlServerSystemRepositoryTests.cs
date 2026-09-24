using Ignixa.DataLayer.SqlServer.Features.Terminology;
using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Features;

/// <summary>
/// Behavioural contract for <c>ISystemRepository</c>. The three assertions the cache's own
/// <c>GetOrCreateSystemIdAsync</c> would not satisfy on its own — trimming, whitespace rejection, and
/// resolving a lost unique-constraint race — are the reason this type exists rather than the cache being
/// registered directly, so each is pinned here.
/// </summary>
#pragma warning disable CA1001
public class SqlServerSystemRepositoryTests : IAsyncLifetime
#pragma warning restore CA1001
{
    private TestTenantDatabase _database = null!;
    private SqlServerSearchIndexReferenceDataCache _cache = null!;

    public async Task InitializeAsync()
    {
        _database = await TestTenantDatabase.CreateSqlServerFhirRepositoryAsync();
        _cache = new SqlServerSearchIndexReferenceDataCache(
            _database.SqlExecutionService,
            _database.TenantId,
            NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance);
    }

    public async Task DisposeAsync()
    {
        _cache.Dispose();
        await _database.DisposeAsync();
    }

    private SqlServerSystemRepository CreateRepository()
        => new(_cache, NullLogger<SqlServerSystemRepository>.Instance);

    private static string NewSystemUri() => $"http://example.org/fhir/{Guid.NewGuid():N}";

    [Fact]
    public async Task GivenTheSameUriTwice_WhenGetOrCreated_ThenTheSameIdIsReturned()
    {
        var repository = CreateRepository();
        var uri = NewSystemUri();

        var first = await repository.GetOrCreateAsync(uri, CancellationToken.None);
        var second = await repository.GetOrCreateAsync(uri, CancellationToken.None);

        first.ShouldBeGreaterThan(0);
        second.ShouldBe(first);
    }

    [Fact]
    public async Task GivenAnUnknownUri_WhenTheIdIsRequested_ThenNullIsReturned()
    {
        var repository = CreateRepository();

        var id = await repository.GetSystemIdAsync(NewSystemUri(), CancellationToken.None);

        id.ShouldBeNull();
    }

    [Fact]
    public async Task GivenACreatedSystem_WhenTheIdIsRequested_ThenItIsFound()
    {
        var repository = CreateRepository();
        var uri = NewSystemUri();

        var created = await repository.GetOrCreateAsync(uri, CancellationToken.None);
        var found = await repository.GetSystemIdAsync(uri, CancellationToken.None);

        found.ShouldBe(created);
    }

    [Fact]
    public async Task GivenAUriWithSurroundingWhitespace_WhenGetOrCreated_ThenItIsTrimmedBeforeStorage()
    {
        // The cache's own get-or-create does not trim. Without this normalization " http://x " and
        // "http://x" would become two distinct rows with two distinct ids.
        var repository = CreateRepository();
        var uri = NewSystemUri();

        var padded = await repository.GetOrCreateAsync($"  {uri}  ", CancellationToken.None);
        var bare = await repository.GetOrCreateAsync(uri, CancellationToken.None);

        padded.ShouldBe(bare);

        var storedCount = await _database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.System WHERE Value = '{uri}'", CancellationToken.None);
        storedCount.ShouldBe(1);
    }

    [Fact]
    public async Task GivenAWhitespaceOnlyUri_WhenGetOrCreated_ThenItIsRejected()
    {
        // The cache rejects null and empty but would accept "   " and store it. The repository's contract
        // is whitespace-rejecting, so this must throw rather than create a blank system row.
        var repository = CreateRepository();

        await Should.ThrowAsync<ArgumentException>(
            () => repository.GetOrCreateAsync("   ", CancellationToken.None));
        await Should.ThrowAsync<ArgumentException>(
            () => repository.GetSystemIdAsync("   ", CancellationToken.None));
    }

    [Fact]
    public async Task GivenManyConcurrentCallsForOneNewUri_WhenGetOrCreated_ThenExactlyOneRowExists()
    {
        // In-process this is serialized by the cache's semaphore rather than by the unique-constraint
        // retry, so it exercises that path and not the cross-process race. The retry itself cannot be
        // provoked from a single process; it is covered by inspection and the catch in GetOrCreateAsync.
        var repository = CreateRepository();
        var uri = NewSystemUri();

        var ids = await Task.WhenAll(
            Enumerable.Range(0, 10).Select(_ => repository.GetOrCreateAsync(uri, CancellationToken.None)));

        ids.Distinct().Count().ShouldBe(1);

        var storedCount = await _database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.System WHERE Value = '{uri}'", CancellationToken.None);
        storedCount.ShouldBe(1);
    }
}
