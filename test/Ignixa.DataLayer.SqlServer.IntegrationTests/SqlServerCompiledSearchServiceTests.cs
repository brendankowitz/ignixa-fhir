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
using Ignixa.Search.Indexing;
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
    public async Task GivenACorruptProbeRow_WhenSearchStreamAsyncCalled_ThenYieldsAPagingProbeSentinelAndAVisibleOutcomeEntry()
    {
        // Arrange -- two Patients, created in order so the first has the lower ResourceSurrogateId
        // and sorts first under the default (unsorted) ascending-by-surrogate-id ordering.
        // MaxItemCount=1 with ProbeExtraRow=true asks the compiler for exactly 2 rows: the real page
        // (the first patient) plus a lookahead (the second) -- mirroring the exact defect code review
        // found: the (pageSize+1)th row, fetched purely to detect a further page, has a corrupt
        // RawResource and cannot be decompressed. Without the fix, TryBuildSearchEntryResult's skip
        // would drop that row from the stream entirely and the caller would never learn a further page
        // exists.
        var tag = Guid.NewGuid().ToString("N");
        var firstId = $"probe-corrupt-a-{tag}";
        var secondId = $"probe-corrupt-b-{tag}";
        await CreatePatientAsync(firstId);
        await CreatePatientAsync(secondId);

        await _database.ExecuteNonQueryAsync(
            $"UPDATE dbo.Resource SET RawResource = 0xDEADBEEF WHERE ResourceId = '{secondId}'");

        var options = new SearchOptions
        {
            ResourceType = "Patient",
            Expression = TypeEquals("Patient"),
            MaxItemCount = 1,
            ProbeExtraRow = true,
        };

        // Act
        var results = new List<SearchEntryResult>();
        await foreach (var result in _service.SearchStreamAsync(options, CancellationToken.None))
        {
            results.Add(result);
        }

        // Assert -- the real page (one Match entry for the first patient), a content-free sentinel
        // proving a further page exists despite the probe row's failure, and a client-visible
        // OperationOutcome standing in for the second patient's unreadable content.
        results.Count.ShouldBe(3);
        results.ShouldContain(r => r.SearchMode == SearchEntryMode.Match && r.ResourceId == firstId);
        results.ShouldContain(r => r.IsPagingProbe);
        results.ShouldContain(r => r.SearchMode == SearchEntryMode.Outcome && r.ResourceId == secondId);
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

    /// <summary>
    /// <c>_include</c> combined with a system-level (multi-type) search is a self-documented compiler
    /// scope boundary -- <c>Lower.Run</c> throws <c>NotSupportedException</c> when the target resource
    /// type is null and includes are present, because <c>BuildIncludeStages</c> needs a concrete match
    /// type to compute <c>SeedFromMatch</c>. This asserts the service maps that to a 400
    /// <see cref="RequestNotValidException"/> rather than leaking the internal exception or silently
    /// returning an under-filtered result set.
    /// <para>
    /// The control half matters as much as the refusal: an unresolvable search parameter ALSO surfaces
    /// as <see cref="RequestNotValidException"/>, so without proving the identical query succeeds with
    /// a concrete resource type, this test would pass just as happily against a catalog seeding
    /// mistake.
    /// </para>
    /// </summary>
    [Fact]
    public async Task GivenAnIncludeOnASystemLevelSearch_WhenSearchStreamAsyncCalled_ThenThrowsRequestNotValidExceptionWhileTheSameIncludeSucceedsOnATypedSearch()
    {
        // Arrange
        var parameterManager = new SearchParameterDefinitionManager(
            FhirVersion.R4.GetSchemaProvider(), NullLogger<SearchParameterDefinitionManager>.Instance);
        var familyParameter = parameterManager.GetSearchParameter("Patient", "family");
        var organizationParameter = parameterManager.GetSearchParameter("Patient", "organization");
        foreach (var url in new[] { familyParameter.Url!, organizationParameter.Url! })
        {
            await _database.ExecuteNonQueryAsync(
                "INSERT INTO dbo.SearchParam (Uri, Status, LastUpdated, IsPartiallySupported) " +
                $"VALUES ('{url}', 'active', SYSDATETIMEOFFSET(), 0)");
        }

        var organizationId = $"include-org-{Guid.NewGuid():N}";
        await CreateResourceAsync("Organization", organizationId, null);

        var patientId = $"include-patient-{Guid.NewGuid():N}";
        await CreateResourceAsync("Patient", patientId,
        [
            new SearchIndexEntry(familyParameter, new StringSearchValue("Includable")),
            new SearchIndexEntry(organizationParameter, new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: "Organization", resourceId: organizationId)),
        ]);

        var include = new IncludeExpression(
            ["Patient"], organizationParameter, "Patient", "Organization", null, wildCard: false, reversed: false, iterate: false);
        var predicate = new SearchParameterExpression(
            familyParameter,
            new SearchParameterPredicateExpression(familyParameter, SearchComparator.Eq, modifier: null, new StringSearchValue("Includable")));

        var systemLevelOptions = new SearchOptions { ResourceType = null, Expression = predicate, Include = [include] };
        var typedOptions = new SearchOptions { ResourceType = "Patient", Expression = predicate, Include = [include] };

        // Act & Assert -- the system-level shape is refused.
        await Should.ThrowAsync<RequestNotValidException>(async () =>
        {
            await foreach (var _ in _service.SearchStreamAsync(systemLevelOptions, CancellationToken.None))
            {
            }
        });

        // Act & Assert -- the same include against a concrete resource type compiles and runs, so the
        // refusal above is about the missing match type, not about anything unresolvable.
        var typedResults = new List<SearchEntryResult>();
        await foreach (var result in _service.SearchStreamAsync(typedOptions, CancellationToken.None))
        {
            typedResults.Add(result);
        }

        typedResults.Single(r => r.ResourceId == patientId).SearchMode.ShouldBe(SearchEntryMode.Match);
        typedResults.Single(r => r.ResourceId == organizationId).SearchMode.ShouldBe(SearchEntryMode.Include);
    }

    private async Task CreateResourceAsync(string resourceType, string resourceId, IReadOnlyList<object>? searchIndices)
    {
        var resource = new ResourceWrapper(
            resourceType,
            resourceId,
            "1",
            DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse($$"""{"resourceType":"{{resourceType}}","id":"{{resourceId}}"}"""),
            new ResourceRequest("PUT", $"{resourceType}/{resourceId}"))
        {
            SearchIndices = searchIndices,
        };

        await _database.Repository.CreateOrUpdateAsync(resource, CancellationToken.None);
    }
}
