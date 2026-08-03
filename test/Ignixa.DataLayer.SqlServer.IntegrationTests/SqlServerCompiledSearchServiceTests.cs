using System.Globalization;
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
        // Arrange -- a search parameter this tenant's catalog has no id for, so Resolve reports it
        // unresolved and the plan never reaches Lower. This replaced a partial-precision _lastUpdated
        // predicate, which used to be the confirmed Lower-time failure and is now a supported range: an
        // unresolvable parameter is the durable choice here because this test is about the SERVICE's
        // failure mapping (any compile failure surfaces as RequestNotValidException, not as a leaked
        // NotSupportedException or a silent empty result), not about which constructs the compiler
        // happens not to support yet. Closing another gap must not silently un-test that mapping again.
        var unknownParameter = new SearchParameterInfo(
            "not-a-real-parameter",
            "not-a-real-parameter",
            SearchParamType.String,
            new Uri("http://example.org/fhir/SearchParameter/not-a-real-parameter"));
        var predicate = new SearchParameterExpression(
            unknownParameter,
            new SearchParameterPredicateExpression(unknownParameter, SearchComparator.Eq, modifier: null, new StringSearchValue("anything")));
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
    public async Task GivenAPartialPrecisionLastUpdatedSearch_WhenSearchStreamAsyncCalled_ThenTheWholeYearMatches()
    {
        // Arrange -- a year-precision _lastUpdated. This used to be the compiler's documented
        // NotSupportedException (Start != End had no point-vs-range formula); it now lowers to a real
        // closed range over the surrogate-id bucket, which is the FHIR semantics: a year-precision
        // instant matches any resource written anywhere in that year. Asserted through the live search
        // path rather than at the lowering unit level, because the surrogate-id encoding of the range
        // bounds is exactly the part a unit test on the plan cannot check.
        var resourceId = $"search-svc-lastupdated-{Guid.NewGuid():N}";
        await CreatePatientAsync(resourceId);
        var currentYear = DateTimeOffset.UtcNow.Year.ToString(CultureInfo.InvariantCulture);
        var options = new SearchOptions
        {
            ResourceType = "Patient",
            Expression = new SearchParameterExpression(
                LastUpdatedParameter,
                new SearchParameterPredicateExpression(
                    LastUpdatedParameter, SearchComparator.Eq, modifier: null, DateTimeSearchValue.Parse(currentYear))),
        };

        // Act
        var results = new List<SearchEntryResult>();
        await foreach (var result in _service.SearchStreamAsync(options, CancellationToken.None))
        {
            results.Add(result);
        }

        // Assert
        results.ShouldContain(r => r.ResourceId == resourceId);
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

    [Fact]
    public async Task GivenResourcesAcrossASurrogateIdSpan_WhenGetExportRangesAsyncCalled_ThenReturnsNonOverlappingExhaustiveRanges()
    {
        // Arrange -- create 3 Patients (distinct surrogate ids by construction).
        await CreatePatientAsync($"export-range-{Guid.NewGuid():N}");
        await CreatePatientAsync($"export-range-{Guid.NewGuid():N}");
        await CreatePatientAsync($"export-range-{Guid.NewGuid():N}");

        // Act
        var ranges = await _service.GetExportRangesAsync("Patient", numberOfRanges: 2, CancellationToken.None);

        // Assert
        ranges.Count.ShouldBeGreaterThan(0);
        ranges.ShouldAllBe(r => r.StartId <= r.EndId);
        // Ranges are contiguous and exhaustive: each range's start is the previous range's end + 1.
        for (var i = 1; i < ranges.Count; i++)
        {
            ranges[i].StartId.ShouldBe(ranges[i - 1].EndId + 1);
        }
    }

    [Fact]
    public async Task GivenAResourceTypeWithNoResources_WhenGetExportRangesAsyncCalled_ThenReturnsEmpty()
    {
        var ranges = await _service.GetExportRangesAsync("Observation", numberOfRanges: 4, CancellationToken.None);
        ranges.ShouldBeEmpty();
    }
}
