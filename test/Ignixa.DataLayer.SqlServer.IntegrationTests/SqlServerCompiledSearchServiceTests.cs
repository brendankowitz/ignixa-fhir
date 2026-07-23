using Ignixa.Abstractions;
using Ignixa.DataLayer.SqlServer.Compression;
using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.DataLayer.SqlServer.Search;
using Ignixa.Domain.Exceptions;
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

// CA1001 (owns disposable fields but isn't itself IDisposable): mirrors SqlServerSymbolResolverTests.cs's
// own suppression rationale -- xunit already drives this type's lifecycle through IAsyncLifetime, and
// DisposeAsync below disposes every disposable field.
#pragma warning disable CA1001
public class SqlServerCompiledSearchServiceTests : IAsyncLifetime
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

        // Constructed exactly as production DI does -- see SqlEntityFrameworkRepositoryFactory.cs's
        // GetOrCreateDefinitionManagers / CompartmentSearchStep0Benchmark.cs's identical wiring: real,
        // pre-generated definition managers, no I/O of their own.
        var compartmentDefinitionManager = new CompartmentDefinitionManager(FhirVersion.R4);
        var schemaProvider = FhirVersion.R4.GetSchemaProvider();
        var searchParameterDefinitionManager = new SearchParameterDefinitionManager(
            schemaProvider, NullLogger<SearchParameterDefinitionManager>.Instance);

        var compressor = new GzipResourceCompressor(new RecyclableMemoryStreamManager());

        _service = new SqlServerCompiledSearchService(
            _database.SqlExecutionService,
            _database.TenantId,
            resolver,
            compartmentDefinitionManager,
            searchParameterDefinitionManager,
            compressor,
            NullLogger.Instance);
    }

    public async Task DisposeAsync()
    {
        _searchCache.Dispose();
        await _database.DisposeAsync();
    }

    private static readonly SearchParameterInfo IdParameter = new(
        "_id", "_id", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Resource-id"));

    private static readonly SearchParameterInfo TypeParameter = new(
        "_type", "_type", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Resource-type"));

    private static readonly SearchParameterInfo LastUpdatedParameter = new(
        "_lastUpdated", "_lastUpdated", SearchParamType.Date, new Uri("http://hl7.org/fhir/SearchParameter/Resource-lastUpdated"));

    private static Expression IdEquals(string resourceId) => new SearchParameterExpression(
        IdParameter,
        new SearchParameterPredicateExpression(IdParameter, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: resourceId, text: null)));

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

    [Fact]
    public async Task GivenAResourceMatchingASimplePredicate_WhenSearchStreamAsyncCalled_ThenReturnsItAsAMatch()
    {
        // Arrange
        var resourceId = $"search-svc-{Guid.NewGuid():N}";
        await CreatePatientAsync(resourceId);
        var options = new SearchOptions { ResourceType = "Patient", Expression = IdEquals(resourceId) };

        // Act
        var results = new List<SearchEntryResult>();
        await foreach (var result in _service.SearchStreamAsync(options, CancellationToken.None))
        {
            results.Add(result);
        }

        // Assert
        results.Count.ShouldBe(1);
        results[0].SearchMode.ShouldBe(SearchEntryMode.Match);
        results[0].ResourceId.ShouldBe(resourceId);
        results[0].ResourceType.ShouldBe("Patient");
    }

    [Fact]
    public async Task GivenAQueryThatFailsToCompile_WhenSearchStreamAsyncCalled_ThenThrowsRequestNotValidException()
    {
        // Arrange -- a _lastUpdated partial-precision predicate (Start != End), per the design doc's
        // confirmed NotSupportedException-at-Lower-time failure mode (ResourceColumnLoweringRule.cs).
        var partialPrecisionValue = new DateTimeSearchValue(PartialDateTime.Parse("2023"));
        var predicate = new SearchParameterExpression(
            LastUpdatedParameter,
            new SearchParameterPredicateExpression(LastUpdatedParameter, SearchComparator.Eq, modifier: null, partialPrecisionValue));
        var options = new SearchOptions { ResourceType = "Patient", Expression = predicate };

        // Act & Assert
        await Should.ThrowAsync<RequestNotValidException>(async () =>
        {
            await foreach (var _ in _service.SearchStreamAsync(options, CancellationToken.None))
            {
            }
        });
    }

    [Fact]
    public async Task GivenTwoMatchingResources_WhenCountAsyncCalled_ThenReturnsTwo()
    {
        // Arrange
        var resourceId1 = $"search-svc-count-{Guid.NewGuid():N}";
        var resourceId2 = $"search-svc-count-{Guid.NewGuid():N}";
        await CreatePatientAsync(resourceId1);
        await CreatePatientAsync(resourceId2);
        var options = new SearchOptions { ResourceType = "Patient", Expression = TypeEquals("Patient") };

        // Act
        var count = await _service.CountAsync(options, CancellationToken.None);

        // Assert
        count.ShouldBe(2);
    }
}
