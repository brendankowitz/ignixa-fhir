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
using SearchComparator = Ignixa.Specification.ValueSets.Normative.SearchComparator;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

/// <summary>
/// Declared-target narrowing for an UNTYPED reference search value -- <c>Observation?subject={id}</c>
/// where the value carries no <c>ResourceType/</c> prefix. Without narrowing, a reference row pointing
/// at <c>Practitioner/{id}</c> is indistinguishable from one pointing at <c>Patient/{id}</c> whenever
/// two resources of different types share a natural id. Asserted at row level with a real colliding
/// decoy: emitted SQL cannot say which rows the narrowing removed, or whether removing them was right.
/// </summary>
// CA1001 (owns disposable fields but isn't itself IDisposable): mirrors SqlServerCompiledSearchServiceTests.cs's
// own suppression rationale -- xunit already drives this type's lifecycle through IAsyncLifetime, and
// DisposeAsync below disposes every disposable field.
#pragma warning disable CA1001
public class SqlServerCompiledSearchServiceUntypedReferenceTests : IAsyncLifetime
#pragma warning restore CA1001
{
    private static readonly SearchParameterDefinitionManager ParameterManager = new(
        FhirVersion.R4.GetSchemaProvider(), NullLogger<SearchParameterDefinitionManager>.Instance);

    private TestTenantDatabase _database = null!;
    private SqlServerSearchIndexReferenceDataCache _searchCache = null!;
    private SqlServerCompiledSearchService _service = null!;
    private SearchParameterInfo _subjectParameter = null!;

    public async Task InitializeAsync()
    {
        _database = await TestTenantDatabase.CreateSqlServerFhirRepositoryAsync();
        _subjectParameter = ParameterManager.GetSearchParameter("Observation", "subject");

        await _database.ExecuteNonQueryAsync(
            "INSERT INTO dbo.SearchParam (Uri, Status, LastUpdated, IsPartiallySupported) " +
            $"VALUES ('{_subjectParameter.Url}', 'active', SYSDATETIMEOFFSET(), 0)");

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
    public async Task GivenAnUntypedReferenceSearchWithANaturalIdCollisionAcrossResourceTypes_WhenSearchStreamAsyncCalled_ThenTheUndeclaredTargetIsExcluded()
    {
        // Arrange -- Observation.subject declares Patient|Group|Device|Location; Practitioner is not
        // among them, so a subject reference pointing at Practitioner/{X} can never be a legitimate
        // match for subject={X}.
        _subjectParameter.TargetResourceTypes.ShouldContain("Patient");
        _subjectParameter.TargetResourceTypes.ShouldNotContain("Practitioner");

        const string CollidingId = "untyped-collide";
        await CreateResourceAsync("Patient", CollidingId, null);
        await CreateResourceAsync("Practitioner", CollidingId, null);

        const string RealMatchId = "untyped-real-match";
        const string DecoyId = "untyped-decoy";
        await CreateResourceAsync("Observation", RealMatchId,
            [new SearchIndexEntry(_subjectParameter, new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: "Patient", resourceId: CollidingId))]);
        await CreateResourceAsync("Observation", DecoyId,
            [new SearchIndexEntry(_subjectParameter, new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: "Practitioner", resourceId: CollidingId))]);

        // Act
        var results = await CollectAsync(CollidingId);

        // Assert
        results.Select(r => r.ResourceId).ShouldBe([RealMatchId]);
    }

    [Fact]
    public async Task GivenAnUntypedReferenceSearchWithNoCollision_WhenSearchStreamAsyncCalled_ThenTheLegitimateMatchIsStillReturned()
    {
        // Arrange -- the narrowing must not cost an ordinary untyped match. Same shape as above minus
        // the colliding Practitioner.
        const string PatientId = "untyped-plain-patient";
        const string OtherPatientId = "untyped-plain-other-patient";
        await CreateResourceAsync("Patient", PatientId, null);
        await CreateResourceAsync("Patient", OtherPatientId, null);

        const string MatchId = "untyped-plain-match";
        const string OtherId = "untyped-plain-other";
        await CreateResourceAsync("Observation", MatchId,
            [new SearchIndexEntry(_subjectParameter, new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: "Patient", resourceId: PatientId))]);
        await CreateResourceAsync("Observation", OtherId,
            [new SearchIndexEntry(_subjectParameter, new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: "Patient", resourceId: OtherPatientId))]);

        // Act
        var results = await CollectAsync(PatientId);

        // Assert
        results.Select(r => r.ResourceId).ShouldBe([MatchId]);
    }

    private async Task<List<SearchEntryResult>> CollectAsync(string untypedReferenceId)
    {
        var untypedValue = new ReferenceSearchValue(
            ReferenceKind.InternalOrExternal, baseUri: null!, resourceType: null!, resourceId: untypedReferenceId);
        var options = new SearchOptions
        {
            ResourceType = "Observation",
            Expression = new SearchParameterExpression(
                _subjectParameter,
                new SearchParameterPredicateExpression(_subjectParameter, SearchComparator.Eq, modifier: null, untypedValue)),
        };

        var results = new List<SearchEntryResult>();
        await foreach (var result in _service.SearchStreamAsync(options, CancellationToken.None))
        {
            results.Add(result);
        }

        return results;
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
