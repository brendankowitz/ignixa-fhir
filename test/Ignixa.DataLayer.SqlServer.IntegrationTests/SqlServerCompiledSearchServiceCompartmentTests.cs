using Ignixa.Abstractions;
using Ignixa.DataLayer.SqlServer.Compression;
using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.DataLayer.SqlServer.Search;
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
using CompartmentType = Ignixa.Specification.ValueSets.Normative.CompartmentType;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

/// <summary>
/// A leakage property, asserted at row level: a compartment query must not return a resource that
/// belongs to a DIFFERENT resource type which merely happens to share the compartment root's natural
/// id. <c>CompartmentLoweringRuleTests</c> checks the emitted predicate's type but not that it filters
/// on <c>ReferenceResourceTypeId</c>, and no fixture anywhere seeds a colliding pair -- so nothing
/// else can catch a regression that drops that column from the predicate.
/// </summary>
// CA1001 (owns disposable fields but isn't itself IDisposable): mirrors SqlServerCompiledSearchServiceTests.cs's
// own suppression rationale -- xunit already drives this type's lifecycle through IAsyncLifetime, and
// DisposeAsync below disposes every disposable field.
#pragma warning disable CA1001
public class SqlServerCompiledSearchServiceCompartmentTests : IAsyncLifetime
#pragma warning restore CA1001
{
    private static readonly SearchParameterDefinitionManager ParameterManager = new(
        FhirVersion.R4.GetSchemaProvider(), NullLogger<SearchParameterDefinitionManager>.Instance);

    private static readonly CompartmentDefinitionManager CompartmentManager = new(FhirVersion.R4);

    private TestTenantDatabase _database = null!;
    private SqlServerSearchIndexReferenceDataCache _searchCache = null!;
    private SqlServerCompiledSearchService _service = null!;

    public async Task InitializeAsync()
    {
        _database = await TestTenantDatabase.CreateSqlServerFhirRepositoryAsync();

        // Every reference search parameter used by ANY member type of the Patient compartment, not
        // just Observation's: Resolve walks the whole compartment definition to build
        // SymbolTable.CompartmentMembership before Lower ever narrows it to the filtered resource
        // types, and an unresolved parameter fails the entire compile.
        await SeedCompartmentCatalogAsync(CompartmentType.Patient);

        _searchCache = new SqlServerSearchIndexReferenceDataCache(
            _database.SqlExecutionService, _database.TenantId, NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance);
        await _searchCache.PreloadResourceTypesAsync(CancellationToken.None);
        var resolver = new SqlServerSymbolResolver(_searchCache);

        _service = new SqlServerCompiledSearchService(
            _database.SqlExecutionService,
            _database.TenantId,
            resolver,
            new CompartmentDefinitionManager(FhirVersion.R4),
            new SearchParameterDefinitionManager(FhirVersion.R4.GetSchemaProvider(), NullLogger<SearchParameterDefinitionManager>.Instance),
            new GzipResourceCompressor(new RecyclableMemoryStreamManager()),
            NullLogger.Instance);
    }

    public async Task DisposeAsync()
    {
        _searchCache.Dispose();
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task GivenACompartmentSearchWhereAnotherResourceTypeSharesTheCompartmentRootsNaturalId_WhenSearchStreamAsyncCalled_ThenTheDecoyDoesNotLeakIntoTheResults()
    {
        // Arrange -- Patient/{X} and Practitioner/{X} share one natural id. One Observation's subject
        // points at Patient/{X} (a genuine member of Patient/{X}'s compartment); the decoy's subject
        // points at Practitioner/{X}, which is not.
        var subjectParam = ParameterManager.GetSearchParameter("Observation", "subject");

        const string CollidingId = "compartment-collide";
        await CreateResourceAsync("Patient", CollidingId, null);
        await CreateResourceAsync("Practitioner", CollidingId, null);

        const string RealMemberId = "compartment-real-member";
        const string DecoyMemberId = "compartment-decoy-member";
        await CreateResourceAsync("Observation", RealMemberId,
            [new SearchIndexEntry(subjectParam, new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: "Patient", resourceId: CollidingId))]);
        await CreateResourceAsync("Observation", DecoyMemberId,
            [new SearchIndexEntry(subjectParam, new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: "Practitioner", resourceId: CollidingId))]);

        var options = new SearchOptions
        {
            ResourceType = "Observation",
            Expression = new CompartmentSearchExpression("Patient", CollidingId, new HashSet<string> { "Observation" }),
        };

        // Act
        var results = new List<SearchEntryResult>();
        await foreach (var result in _service.SearchStreamAsync(options, CancellationToken.None))
        {
            results.Add(result);
        }

        // Assert -- the decoy's reference row is indistinguishable from the real one on
        // ReferenceResourceId alone; only ReferenceResourceTypeId separates them.
        results.Select(r => r.ResourceId).ShouldBe([RealMemberId]);
    }

    private async Task SeedCompartmentCatalogAsync(CompartmentType compartmentType)
    {
        var urls = new HashSet<string>(StringComparer.Ordinal);
        if (CompartmentManager.TryGetResourceTypes(compartmentType, out var resourceTypes))
        {
            foreach (var resourceType in resourceTypes)
            {
                if (!CompartmentManager.TryGetSearchParams(resourceType, compartmentType, out var codes))
                {
                    continue;
                }

                foreach (var code in codes)
                {
                    if (ParameterManager.TryGetSearchParameter(resourceType, code, out var searchParam) && searchParam.Url is { } url)
                    {
                        urls.Add(url.ToString());
                    }
                }
            }
        }

        foreach (var url in urls)
        {
            await _database.ExecuteNonQueryAsync(
                "INSERT INTO dbo.SearchParam (Uri, Status, LastUpdated, IsPartiallySupported) " +
                $"VALUES ('{url}', 'active', SYSDATETIMEOFFSET(), 0)");
        }
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
