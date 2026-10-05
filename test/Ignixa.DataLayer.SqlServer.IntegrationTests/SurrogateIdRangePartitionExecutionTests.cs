using Ignixa.Abstractions;
using Ignixa.DataLayer.SqlServer.Compression;
using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.DataLayer.SqlServer.Search;
using Ignixa.Domain.Models;
using Ignixa.Search.Definition;
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IO;
using Shouldly;
using Xunit;
using SearchComparator = Ignixa.Specification.ValueSets.Normative.SearchComparator;
using SearchParamType = Ignixa.Specification.ValueSets.Normative.SearchParamType;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

/// <summary>
/// Executes the surrogate-id range path -- <see cref="SearchOptions.StartSurrogateId"/> /
/// <see cref="SearchOptions.EndSurrogateId"/> through the compiler and against a real database.
/// This is the read side of <c>$export</c>: <c>ExportOrchestration</c> fans a resource type out into
/// contiguous surrogate-id windows and each <c>ExportWorkerActivity</c> searches exactly one window,
/// so the partition contract the whole operation rests on is "non-overlapping and exhaustive".
/// <see cref="SqlServerCompiledSearchServiceTests"/> covers only range *generation*
/// (<c>GetExportRangesAsync</c>, which bypasses the compiler entirely); nothing covered range
/// *consumption* until this file.
/// </summary>
#pragma warning disable CA1001
public class SurrogateIdRangePartitionExecutionTests : IAsyncLifetime
#pragma warning restore CA1001
{
    private TestTenantDatabase _database = null!;
    private SqlServerSearchIndexReferenceDataCache _searchCache = null!;
    private SqlServerCompiledSearchService _service = null!;

    public async Task InitializeAsync()
    {
        _database = await TestTenantDatabase.CreateSqlServerFhirRepositoryAsync();

        _searchCache = new SqlServerSearchIndexReferenceDataCache(
            _database.SqlExecutionService, _database.TenantId, NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance);
        await _searchCache.PreloadResourceTypesAsync(CancellationToken.None);
        var resolver = new SqlServerSymbolResolver(_searchCache);

        var compartmentDefinitionManager = new CompartmentDefinitionManager(FhirVersion.R4);
        var schemaProvider = FhirVersion.R4.GetSchemaProvider();
        var searchParameterDefinitionManager = new SearchParameterDefinitionManager(
            schemaProvider, NullLogger<SearchParameterDefinitionManager>.Instance);

        _service = new SqlServerCompiledSearchService(
            _database.SqlExecutionService,
            _database.TenantId,
            resolver,
            compartmentDefinitionManager,
            searchParameterDefinitionManager,
            new GzipResourceCompressor(new RecyclableMemoryStreamManager()),
            NullLogger.Instance);
    }

    public async Task DisposeAsync()
    {
        _searchCache.Dispose();
        await _database.DisposeAsync();
    }

    private static readonly SearchParameterInfo TypeParameter = new(
        "_type", "_type", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Resource-type"));

    private static Expression TypeEquals(string resourceType) => new SearchParameterExpression(
        TypeParameter,
        new SearchParameterPredicateExpression(TypeParameter, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: resourceType, text: null)));

    private async Task CreatePatientAsync(string resourceId)
    {
        var resource = new ResourceWrapper(
            "Patient",
            resourceId,
            "1",
            DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse($$"""{"resourceType":"Patient","id":"{{resourceId}}"}"""),
            new ResourceRequest("PUT", $"Patient/{resourceId}"));

        await _database.Repository.CreateOrUpdateAsync(resource, CancellationToken.None);
    }

    private async Task<List<string>> SearchIdsAsync(long? startSurrogateId, long? endSurrogateId)
    {
        var options = new SearchOptions
        {
            ResourceType = "Patient",
            Expression = TypeEquals("Patient"),
            StartSurrogateId = startSurrogateId,
            EndSurrogateId = endSurrogateId,
        };

        var ids = new List<string>();
        await foreach (var result in _service.SearchStreamAsync(options, CancellationToken.None))
        {
            ids.Add(result.ResourceId);
        }

        return ids;
    }

    [Fact]
    public async Task GivenExportRangesOverAResourceType_WhenEachRangeIsSearched_ThenThePartitionsAreDisjointAndExhaustive()
    {
        // Arrange -- a fresh tenant database, so these are the only Patients that exist.
        var createdIds = new List<string>();
        for (var i = 0; i < 6; i++)
        {
            var id = $"surrogate-partition-{Guid.NewGuid():N}";
            await CreatePatientAsync(id);
            createdIds.Add(id);
        }

        var unbounded = await SearchIdsAsync(startSurrogateId: null, endSurrogateId: null);
        unbounded.OrderBy(x => x, StringComparer.Ordinal)
            .ShouldBe(createdIds.OrderBy(x => x, StringComparer.Ordinal));

        var ranges = await _service.GetExportRangesAsync("Patient", numberOfRanges: 3, CancellationToken.None);

        // A single range would make the disjointness assertion vacuous -- the partition contract is only
        // meaningful once the data is actually split.
        ranges.Count.ShouldBeGreaterThan(1);

        // Act -- one search per range, exactly as ExportWorkerActivity issues them.
        var perRange = new List<List<string>>();
        foreach (var range in ranges)
        {
            perRange.Add(await SearchIdsAsync(range.StartId, range.EndId));
        }

        // Assert -- exhaustive: every resource appears in some partition.
        perRange.SelectMany(x => x).OrderBy(x => x, StringComparer.Ordinal)
            .ShouldBe(createdIds.OrderBy(x => x, StringComparer.Ordinal));

        // Assert -- non-overlapping: no resource appears in two partitions. A range that failed to
        // filter at all would return the full set from every partition and trip this.
        perRange.SelectMany(x => x).Count().ShouldBe(createdIds.Count);

        // Assert -- each partition is a strict subset, i.e. the bound really narrowed the match set.
        perRange.ShouldAllBe(p => p.Count < createdIds.Count);
    }

    [Fact]
    public async Task GivenASurrogateIdRangeBelowAllData_WhenSearched_ThenNoResourcesAreReturned()
    {
        // Arrange
        await CreatePatientAsync($"surrogate-empty-{Guid.NewGuid():N}");
        var ranges = await _service.GetExportRangesAsync("Patient", numberOfRanges: 1, CancellationToken.None);
        ranges.Count.ShouldBe(1);

        // Act -- a window that ends strictly before the first real surrogate id.
        var results = await SearchIdsAsync(0L, ranges[0].StartId - 1);

        // Assert
        results.ShouldBeEmpty();
    }
}
