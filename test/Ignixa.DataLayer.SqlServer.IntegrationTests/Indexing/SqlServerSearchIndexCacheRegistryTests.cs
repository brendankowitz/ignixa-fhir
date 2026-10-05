using Ignixa.DataLayer.SqlServer.Compression;
using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Domain.Constants;
using Ignixa.Domain.Models;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.ValueSets.Normative;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IO;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Indexing;

/// <summary>
/// The registry exists for one reason: identity. Row generators read <c>SearchParameterMappings</c> off the
/// cache instance the write path was handed, so a search-parameter sync that populates a different instance
/// leaves the write path dropping index rows while reporting success. These tests assert the identity
/// property directly, and then assert the consequence that depends on it.
/// </summary>
public class SqlServerSearchIndexCacheRegistryTests : IAsyncLifetime
{
    private const string IdentifierSearchParamUrl = "http://hl7.org/fhir/SearchParameter/Patient-identifier";

    private TerminologyTestFixture _fixture = null!;

    public async Task InitializeAsync() => _fixture = await TerminologyTestFixture.CreateAsync();

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    [Fact]
    public async Task GivenTheSameTenantTwice_WhenCachesAreObtained_ThenItIsTheSameInstance()
    {
        var first = await _fixture.CacheRegistry.GetOrCreateAsync(
            TestTenantDatabase.TestTenantId, CancellationToken.None);
        var second = await _fixture.CacheRegistry.GetOrCreateAsync(
            TestTenantDatabase.TestTenantId, CancellationToken.None);

        second.ShouldBeSameAs(first);
    }

    [Fact]
    public async Task GivenTwoTenants_WhenCachesAreObtained_ThenTheyAreIsolatedFromEachOther()
    {
        var tenant = await _fixture.CacheRegistry.GetOrCreateAsync(
            TestTenantDatabase.TestTenantId, CancellationToken.None);
        var systemPartition = await _fixture.CacheRegistry.GetOrCreateAsync(
            SystemConstants.SystemPartitionId, CancellationToken.None);

        systemPartition.ShouldNotBeSameAs(tenant);
    }

    [Fact]
    public async Task GivenASystemRecordedMissingInEveryTenant_WhenForgetIsBroadcast_ThenNoneKeepReportingItMissing()
    {
        // A negative entry is bounded by its own TTL, but a write that creates the row should retract it at
        // once rather than leaving other tenants answering "missing" until expiry. EF broadcasts through
        // MultiTenantSearchIndexCache; before the registry the SqlServer side had nowhere to broadcast from.
        const string systemUri = "http://example.org/fhir/registry-broadcast";

        var tenant = await _fixture.CacheRegistry.GetOrCreateAsync(
            TestTenantDatabase.TestTenantId, CancellationToken.None);
        var systemPartition = await _fixture.CacheRegistry.GetOrCreateAsync(
            SystemConstants.SystemPartitionId, CancellationToken.None);

        (await tenant.TryGetSystemIdAsync(systemUri, CancellationToken.None)).ShouldBeNull();
        (await systemPartition.TryGetSystemIdAsync(systemUri, CancellationToken.None)).ShouldBeNull();

        await _fixture.ExecuteNonQueryAsync(
            $"INSERT INTO dbo.System (Value) VALUES ('{systemUri}')", CancellationToken.None);

        // Still remembered as missing by both, because nothing has retracted the record yet.
        (await tenant.TryGetSystemIdAsync(systemUri, CancellationToken.None)).ShouldBeNull();

        _fixture.CacheRegistry.ForgetMissingSystem(systemUri);

        (await tenant.TryGetSystemIdAsync(systemUri, CancellationToken.None)).ShouldNotBeNull();
        (await systemPartition.TryGetSystemIdAsync(systemUri, CancellationToken.None)).ShouldNotBeNull();
    }

    [Fact]
    public async Task GivenAPoisonedCacheFromTheRegistry_WhenSyncedThroughASecondHandle_ThenTheWritePathStopsDroppingRows()
    {
        // THE POINT OF THE REGISTRY. The repository is built from one handle and the sync runs through
        // another. Both must be the same object, or the sync repairs a cache nothing reads and the index
        // rows stay dropped while the write reports success.
        var writePathCache = await _fixture.CacheRegistry.GetOrCreateAsync(
            TestTenantDatabase.TestTenantId, CancellationToken.None);

        var repository = BuildRepository(writePathCache);

        // Probing before the dbo.SearchParam row exists is what poisons the entry.
        (await writePathCache.GetSearchParamIdAsync(IdentifierSearchParamUrl, CancellationToken.None))
            .ShouldBeNull();

        var syncHandle = await _fixture.CacheRegistry.GetOrCreateAsync(
            TestTenantDatabase.TestTenantId, CancellationToken.None);

        var synced = await syncHandle.SyncSearchParametersToDatabaseAsync(
            [IdentifierSearchParamUrl], searchParameterDefinitionManager: null, CancellationToken.None);

        synced.ShouldBe(1);

        var transactionId = await WritePatientWithIdentifierAsync(repository, "registry-patient", "abc123");

        var rowCount = await _fixture.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.TokenSearchParam WHERE ResourceSurrogateId >= {transactionId}",
            CancellationToken.None);

        rowCount.ShouldBe(
            1,
            "the sync must reach the very cache instance the repository reads, or index rows stay dropped");
    }

    private SqlServerMergeRepository BuildRepository(SqlServerSearchIndexReferenceDataCache cache)
    {
        var compressor = new GzipResourceCompressor(new RecyclableMemoryStreamManager());

        var extensionUpdater = new SqlServerPostMergeExtensionUpdater(
            _fixture.SqlExecutionService,
            TestTenantDatabase.TestTenantId,
            NullLogger<SqlServerPostMergeExtensionUpdater>.Instance);

        return new SqlServerMergeRepository(
            _fixture.SqlExecutionService,
            TestTenantDatabase.TestTenantId,
            compressor,
            cache,
            extensionUpdater,
            NullLogger<SqlServerMergeRepository>.Instance);
    }

    private static async Task<long> WritePatientWithIdentifierAsync(
        SqlServerMergeRepository repository, string resourceId, string identifierValue)
    {
        var (transactionId, _) = await repository.BeginTransactionAsync(resourceCount: 1, CancellationToken.None);

        var resourceJson = ResourceJsonNode.Parse(
            $$"""{"resourceType":"Patient","id":"{{resourceId}}","identifier":[{"value":"{{identifierValue}}"}]}""");

        var searchParameter = new SearchParameterInfo(
            "identifier", "identifier", SearchParamType.Token, new Uri(IdentifierSearchParamUrl));

        var tokenValue = new TokenSearchValue(
            system: null, code: identifierValue, text: null, identifierTypeSystem: null, identifierTypeCode: null);

        var wrapper = new ResourceWrapper(
            "Patient", resourceId, "1", DateTimeOffset.UtcNow, resourceJson,
            new ResourceRequest("PUT", $"Patient/{resourceId}"))
        {
            SearchIndices = [new SearchIndexEntry(searchParameter, tokenValue)],
        };

        await repository.MergeResourcesAsync(
            transactionId, singleTransaction: true, [wrapper], [0], CancellationToken.None);
        await repository.CommitTransactionAsync(transactionId, cancellationToken: CancellationToken.None);

        return transactionId;
    }
}
