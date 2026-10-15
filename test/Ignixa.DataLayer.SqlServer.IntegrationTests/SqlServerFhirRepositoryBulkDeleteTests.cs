using Ignixa.Abstractions;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Domain.Models;
using Ignixa.Serialization.SourceNodes;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

/// <summary>
/// The physical-deletion contract <c>$bulk-delete</c> depends on:
/// <see cref="SqlServerFhirRepository.HardDeleteAsync"/> (hard-delete mode) and
/// <see cref="SqlServerFhirRepository.PurgeHistoryAsync"/> (purge-history mode).
/// </summary>
public class SqlServerFhirRepositoryBulkDeleteTests : IAsyncLifetime
{
    private TestTenantDatabase _database = null!;
    private SqlServerFhirRepository _repository = null!;

    public async Task InitializeAsync()
    {
        _database = await TestTenantDatabase.CreateSqlServerFhirRepositoryAsync();
        _repository = _database.Repository;
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task GivenAResourceWithThreeVersions_WhenHardDeleteAsync_ThenEveryVersionIsRemovedAndUnreadable()
    {
        // Arrange
        const string ResourceId = "bulk-hard-delete-1";
        var key = new ResourceKey("Patient", ResourceId);
        await CreateVersionAsync(ResourceId);
        await CreateVersionAsync(ResourceId);
        await CreateVersionAsync(ResourceId);

        // Act
        var result = await _repository.HardDeleteAsync(key, CancellationToken.None);

        // Assert
        result.ShouldBeTrue();
        (await _repository.GetAsync(key, CancellationToken.None)).ShouldBeNull();
        var history = await _repository.GetResourceHistoryAsync(
            key, new HistoryQueryParameters { Count = 10 }, CancellationToken.None).ToListAsync();
        history.ShouldBeEmpty();
        (await _database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.Resource WHERE ResourceId = '{ResourceId}'")).ShouldBe(0);
    }

    [Fact]
    public async Task GivenTwoResources_WhenHardDeleteAsyncRunsOnOne_ThenTheOtherResourceIsUntouched()
    {
        // Arrange
        const string DeletedId = "bulk-hard-delete-victim";
        const string SurvivorId = "bulk-hard-delete-survivor";
        await CreateVersionAsync(DeletedId);
        await CreateVersionAsync(SurvivorId);

        // Act
        var result = await _repository.HardDeleteAsync(new ResourceKey("Patient", DeletedId), CancellationToken.None);

        // Assert
        result.ShouldBeTrue();
        (await _repository.GetAsync(new ResourceKey("Patient", DeletedId), CancellationToken.None)).ShouldBeNull();
        var survivor = await _repository.GetAsync(new ResourceKey("Patient", SurvivorId), CancellationToken.None);
        survivor.ShouldNotBeNull();
        survivor!.VersionId.ShouldBe("1");
    }

    [Fact]
    public async Task GivenNoVersionExistsForAKnownResourceType_WhenHardDeleteAsync_ThenReturnsFalse()
    {
        // Act
        var result = await _repository.HardDeleteAsync(
            new ResourceKey("Patient", "bulk-hard-delete-missing"), CancellationToken.None);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task GivenAResourceTypeNeverWritten_WhenHardDeleteAsync_ThenReturnsFalseWithoutCreatingAResourceTypeRow()
    {
        // Act
        var result = await _repository.HardDeleteAsync(
            new ResourceKey("ZzBulkDeleteNeverWrittenType", "whatever"), CancellationToken.None);

        // Assert
        result.ShouldBeFalse();
        (await _database.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.ResourceType WHERE Name = 'ZzBulkDeleteNeverWrittenType'")).ShouldBe(0);
    }

    [Fact]
    public async Task GivenThreeVersions_WhenPurgeHistoryAsync_ThenTheCurrentVersionIsKeptAndTwoHistoricalVersionsAreRemoved()
    {
        // Arrange
        const string ResourceId = "bulk-purge-1";
        var key = new ResourceKey("Patient", ResourceId);
        await CreateVersionAsync(ResourceId);
        await CreateVersionAsync(ResourceId);
        await CreateVersionAsync(ResourceId);

        // Act
        var purgedCount = await _repository.PurgeHistoryAsync(key, CancellationToken.None);

        // Assert
        purgedCount.ShouldBe(2);
        var current = await _repository.GetAsync(key, CancellationToken.None);
        current.ShouldNotBeNull();
        current!.VersionId.ShouldBe("3");
        var history = await _repository.GetResourceHistoryAsync(
            key, new HistoryQueryParameters { Count = 10 }, CancellationToken.None).ToListAsync();
        history.Count.ShouldBe(1);
        history.Single().VersionId.ShouldBe("3");
    }

    [Fact]
    public async Task GivenNoHistory_WhenPurgeHistoryAsync_ThenReturnsZeroAndKeepsTheCurrentVersion()
    {
        // Arrange
        const string ResourceId = "bulk-purge-no-history";
        var key = new ResourceKey("Patient", ResourceId);
        await CreateVersionAsync(ResourceId);

        // Act
        var purgedCount = await _repository.PurgeHistoryAsync(key, CancellationToken.None);

        // Assert
        purgedCount.ShouldBe(0);
        (await _repository.GetAsync(key, CancellationToken.None)).ShouldNotBeNull();
    }

    [Fact]
    public async Task GivenNoVersionExists_WhenPurgeHistoryAsync_ThenReturnsZero()
    {
        // Act
        var purgedCount = await _repository.PurgeHistoryAsync(
            new ResourceKey("Patient", "bulk-purge-missing"), CancellationToken.None);

        // Assert
        purgedCount.ShouldBe(0);
    }

    [Fact]
    public async Task GivenACurrentSoftDeletedTombstoneWithHistory_WhenPurgeHistoryAsync_ThenTheTombstoneIsKeptAndItsHistoryIsRemoved()
    {
        // Arrange
        const string ResourceId = "bulk-purge-tombstone";
        var key = new ResourceKey("Patient", ResourceId);
        await CreateVersionAsync(ResourceId);
        await CreateVersionAsync(ResourceId);
        await _repository.DeleteAsync(
            key, new ResourceRequest("DELETE", $"Patient/{ResourceId}"), definitionsEventId: 0, cancellationToken: CancellationToken.None);

        // Act
        var purgedCount = await _repository.PurgeHistoryAsync(key, CancellationToken.None);

        // Assert
        purgedCount.ShouldBe(2);
        var current = await _repository.GetAsync(key, CancellationToken.None);
        current.ShouldNotBeNull();
        current!.IsDeleted.ShouldBeTrue();
        current.VersionId.ShouldBe("3");
        var history = await _repository.GetResourceHistoryAsync(
            key, new HistoryQueryParameters { Count = 10 }, CancellationToken.None).ToListAsync();
        history.Count.ShouldBe(1);
    }

    [Fact]
    public async Task GivenOrphanedIndexRowsOnHistoricalVersions_WhenPurgeHistoryAsync_ThenTheyAreRemovedAndTheCurrentVersionKeepsIts()
    {
        // Arrange
        //
        // The normal write path already wipes a version's search-index rows the instant it is
        // superseded (dbo.MergeResources.sql's @PreviousSurrogateIds deletes) -- a historical version
        // was never searchable to begin with, so PurgeHistoryAsync's own index-table DELETE is
        // normally a no-op for one. What it must still cover is a row that reaches a historical
        // surrogate id some other way -- the same kind of orphan HardDeleteResourceCoreAsync's own
        // comments describe for a race between a write and a delete. dbo.ResourceWriteClaim is the
        // one table the write path cannot populate (its row generator is a documented stub -- see
        // SearchIndexTableSeeder's remarks), so inserting one directly against an ALREADY-historical
        // surrogate id reproduces that orphan without relying on a race.
        await SearchIndexTableSeeder.SeedSearchParameterCatalogAsync(_database, CancellationToken.None);
        const string ReferenceTargetId = "bulk-purge-index-target";
        await CreateVersionAsync(ReferenceTargetId);

        const string ResourceId = "bulk-purge-index-1";
        var key = new ResourceKey("Patient", ResourceId);
        await CreateVersionAsync(ResourceId);
        var v1Surrogate = await SurrogateIdForVersionAsync(ResourceId, 1);

        await CreateVersionAsync(ResourceId);
        var v2Surrogate = await SurrogateIdForVersionAsync(ResourceId, 2);
        // v1 is historical now -- a later write's supersede cleanup cannot touch this row.
        await SearchIndexTableSeeder.InsertResourceWriteClaimAsync(_database, v1Surrogate, CancellationToken.None);

        var v3Surrogate = await CreateIndexedVersionAsync(ResourceId, ReferenceTargetId);
        // v2 is historical now, for the same reason.
        await SearchIndexTableSeeder.InsertResourceWriteClaimAsync(_database, v2Surrogate, CancellationToken.None);

        (await _database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.ResourceWriteClaim WHERE ResourceSurrogateId = {v1Surrogate}")).ShouldBe(1);
        (await _database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.ResourceWriteClaim WHERE ResourceSurrogateId = {v2Surrogate}")).ShouldBe(1);
        await SearchIndexTableSeeder.AssertEverySearchIndexTableHasRowsAsync(_database, v3Surrogate, CancellationToken.None);

        // Act
        var purgedCount = await _repository.PurgeHistoryAsync(key, CancellationToken.None);

        // Assert
        purgedCount.ShouldBe(2);
        (await _database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.ResourceWriteClaim WHERE ResourceSurrogateId = {v1Surrogate}")).ShouldBe(0);
        (await _database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.ResourceWriteClaim WHERE ResourceSurrogateId = {v2Surrogate}")).ShouldBe(0);
        await SearchIndexTableSeeder.AssertEverySearchIndexTableHasRowsAsync(_database, v3Surrogate, CancellationToken.None);
    }

    private Task<long> SurrogateIdForVersionAsync(string resourceId, int version) =>
        _database.ExecuteScalarAsync<long>(
            $"SELECT ResourceSurrogateId FROM dbo.Resource WHERE ResourceId = '{resourceId}' AND Version = {version}");

    private async Task<long> CreateIndexedVersionAsync(string resourceId, string referenceTargetId)
    {
        var resource = new ResourceWrapper("Patient", resourceId, "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse($$"""{"resourceType":"Patient","id":"{{resourceId}}"}"""),
            new ResourceRequest("PUT", $"Patient/{resourceId}"))
        {
            SearchIndices = SearchIndexTableSeeder.BuildSearchIndicesCoveringEverySearchIndexTable(referenceTargetId)
        };
        await _repository.CreateOrUpdateAsync(resource, CancellationToken.None);
        var surrogateId = await _database.ExecuteScalarAsync<long>(
            $"SELECT ResourceSurrogateId FROM dbo.Resource WHERE ResourceId = '{resourceId}' AND IsHistory = 0");
        await SearchIndexTableSeeder.InsertResourceWriteClaimAsync(_database, surrogateId, CancellationToken.None);
        return surrogateId;
    }

    private async Task CreateVersionAsync(string resourceId)
    {
        var resource = new ResourceWrapper("Patient", resourceId, "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse($$"""{"resourceType":"Patient","id":"{{resourceId}}"}"""),
            new ResourceRequest("PUT", $"Patient/{resourceId}"));
        await _repository.CreateOrUpdateAsync(resource, CancellationToken.None);
    }
}
