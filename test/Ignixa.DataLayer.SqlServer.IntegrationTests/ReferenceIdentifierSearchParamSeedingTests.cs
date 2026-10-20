// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.DataLayer.SqlServer.Compression;
using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Domain.Models;
using Ignixa.Search.Definition;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IO;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

/// <summary>
/// Regression guard for the derived <c>#identifier</c> parameter's path from
/// <see cref="SearchParameterDefinitionManager"/> registration through <c>dbo.SearchParam</c> seeding to an
/// actual <c>dbo.TokenSearchParam</c> row, against the real <see cref="SqlServerSearchIndexReferenceDataCache"/>
/// / <see cref="SqlServerMergeRepository"/> write path described in
/// <c>docs/features/search/investigations/reference-identifier-search.md</c>'s "Data layer requirements".
/// Mirrors <see cref="SqlServerMergeRepositoryTests"/>'s fixture pattern (TestTenantDatabase; DacFx deploy
/// once, restore per test).
/// </summary>
// CA1001: see SqlServerMergeRepositoryTests' identical suppression -- the cache's only disposable is a
// SemaphoreSlim, explicitly disposed in DisposeAsync below.
#pragma warning disable CA1001
public class ReferenceIdentifierSearchParamSeedingTests : IAsyncLifetime
#pragma warning restore CA1001
{
    private TestTenantDatabase _database = null!;
    private SqlServerSearchIndexReferenceDataCache _cache = null!;
    private SqlServerMergeRepository _repository = null!;
    private SearchParameterDefinitionManager _definitions = null!;

    public async Task InitializeAsync()
    {
        _database = await TestTenantDatabase.CreateEmptyAsync();
        _definitions = new SearchParameterDefinitionManager(
            new R4CoreSchemaProvider(), NullLogger<SearchParameterDefinitionManager>.Instance);
        _cache = new SqlServerSearchIndexReferenceDataCache(
            _database.SqlExecutionService, _database.TenantId, NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance);
        await _cache.PreloadResourceTypesAsync(CancellationToken.None);

        var compressor = new GzipResourceCompressor(new RecyclableMemoryStreamManager());
        var extensionUpdater = new SqlServerPostMergeExtensionUpdater(
            _database.SqlExecutionService, _database.TenantId, NullLogger<SqlServerPostMergeExtensionUpdater>.Instance);
        _repository = new SqlServerMergeRepository(
            _database.SqlExecutionService, _database.TenantId, compressor, _cache, extensionUpdater, NullLogger<SqlServerMergeRepository>.Instance);
    }

    public async Task DisposeAsync()
    {
        _cache.Dispose();
        await _database.DisposeAsync();
    }

    /// <summary>
    /// End-to-end proof that a reference parameter's derived <c>#identifier</c> sibling (here
    /// <c>Patient-organization#identifier</c>, derived from the shipped "organization" reference parameter,
    /// expression <c>Patient.managingOrganization</c>) reaches <c>dbo.SearchParam</c> via
    /// <see cref="SqlServerSearchIndexReferenceDataCache.SeedSearchParametersToDatabaseAsync"/>, and that a
    /// resource indexed under it produces a real <c>dbo.TokenSearchParam</c> row keyed on that id -- not
    /// skipped with a warning, per <see cref="RowGenerators.TokenSearchParameterRowGenerator"/>'s
    /// "SearchParamId not found in cache" guard.
    /// </summary>
    [Fact]
    public async Task GivenAReferenceParametersDerivedIdentifierSibling_WhenSeededAndIndexed_ThenATokenSearchParamRowExistsUnderItsRealId()
    {
        // Arrange
        SearchParameterInfo baseParameter = _definitions.GetSearchParameter("Patient", "organization");
        SearchParameterInfo derivedParameter = ReferenceIdentifierSearchParameterFactory.Create(baseParameter);

        var syncedCount = await _cache.SeedSearchParametersToDatabaseAsync(_definitions, CancellationToken.None);
        syncedCount.ShouldBeGreaterThan(0);

        var derivedSearchParamId = await _database.ExecuteScalarAsync<short>(
            $"SELECT SearchParamId FROM dbo.SearchParam WHERE Uri = '{derivedParameter.Url}'");
        derivedSearchParamId.ShouldNotBe(
            (short)0,
            "the derived '#identifier' parameter's canonical URL (with its fragment) must get its own "
            + "dbo.SearchParam row -- a storage layer that aliases it onto the base reference parameter's "
            + "row, or that skips it as an unknown URL, silently drops every derived index row.");

        var (transactionId, _) = await _repository.BeginTransactionAsync(resourceCount: 1, CancellationToken.None);
        var resourceJson = ResourceJsonNode.Parse(
            """{"resourceType":"Patient","id":"identifier-seeding-test","managingOrganization":{"identifier":{"system":"http://example.org/orgs","value":"org-42"}}}""");
        var tokenValue = new TokenSearchValue(
            system: "http://example.org/orgs", code: "org-42", text: null, identifierTypeSystem: null, identifierTypeCode: null);
        var wrapper = new ResourceWrapper(
            "Patient", "identifier-seeding-test", "1", DateTimeOffset.UtcNow, resourceJson,
            new ResourceRequest("PUT", "Patient/identifier-seeding-test"))
        {
            SearchIndices = [new SearchIndexEntry(derivedParameter, tokenValue)]
        };

        // Act
        await _repository.MergeResourcesAsync(transactionId, singleTransaction: true, [wrapper], [0], CancellationToken.None);
        await _repository.CommitTransactionAsync(transactionId, cancellationToken: CancellationToken.None);

        // Assert
        var rowCount = await _database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.TokenSearchParam WHERE SearchParamId = {derivedSearchParamId} AND Code = 'org-42'");
        rowCount.ShouldBe(
            1,
            "the derived parameter's real dbo.SearchParam id must be the one the row generator writes "
            + "under -- a mismatch (e.g. a cache resolving a different, unrelated id for the same URL) "
            + "indexes nothing a search for this derived parameter can ever find.");
    }
}
