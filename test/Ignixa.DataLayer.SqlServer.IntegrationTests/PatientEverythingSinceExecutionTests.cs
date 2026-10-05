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
/// Executes <c>Patient/$everything</c> -- and specifically its <c>_since</c> filter -- against a real
/// database. Two things in the unified compiler are reached for the first time here:
///
/// 1. <c>EmitVisibleSinceFilter</c> now appends <c>AND r.IsHistory = 0 AND r.IsDeleted = 0</c> to the
///    <c>dbo.Resource</c>/<c>dbo.Transactions</c> join, where the pre-unification emitter applied no row
///    filter at all. The argument that this is row-identical is a claim about an index, and only an
///    executed query can test it.
/// 2. <c>_since</c> itself had unit coverage only. The captured legacy corpus cannot reach it (its one
///    captured URL is <c>_since=3000</c>, which is not a parseable instant), so no <c>_since</c> query had
///    ever been executed against a database on either branch.
///
/// The cutoff is read back from <c>dbo.Transactions.VisibleDate</c> rather than taken from the test host's
/// clock: <c>_since</c> filters on the server's own transaction timestamps, and comparing those against a
/// client clock makes the assertion a race against clock skew rather than a test of the filter.
/// </summary>
#pragma warning disable CA1001
public class PatientEverythingSinceExecutionTests : IAsyncLifetime
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

        _searchCache = new SqlServerSearchIndexReferenceDataCache(
            _database.SqlExecutionService, _database.TenantId, NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance);
        await _searchCache.PreloadResourceTypesAsync(CancellationToken.None);

        await SeedPatientCompartmentCatalogAsync();

        _service = new SqlServerCompiledSearchService(
            _database.SqlExecutionService,
            _database.TenantId,
            new SqlServerSymbolResolver(_searchCache),
            CompartmentManager,
            ParameterManager,
            new GzipResourceCompressor(new RecyclableMemoryStreamManager()),
            NullLogger.Instance);
    }

    public async Task DisposeAsync()
    {
        _searchCache.Dispose();
        await _database.DisposeAsync();
    }

    // Resolve.RunAsync walks the WHOLE Patient compartment definition before Lower narrows it, so every
    // member type's membership parameter has to resolve or the compile fails outright. Same rationale as
    // CompiledSearchChainIncludeCompartmentDifferentialTests.SeedCompartmentCatalogAsync.
    private async Task SeedPatientCompartmentCatalogAsync()
    {
        var urls = new HashSet<Uri>();
        if (CompartmentManager.TryGetResourceTypes(CompartmentType.Patient, out var resourceTypes))
        {
            foreach (var resourceType in resourceTypes)
            {
                if (!CompartmentManager.TryGetSearchParams(resourceType, CompartmentType.Patient, out var codes))
                {
                    continue;
                }

                foreach (var code in codes)
                {
                    if (ParameterManager.TryGetSearchParameter(resourceType, code, out var searchParam) && searchParam.Url is { } url)
                    {
                        urls.Add(url);
                    }
                }
            }
        }

        foreach (var url in urls)
        {
            await _database.ExecuteNonQueryAsync(
                "INSERT INTO dbo.SearchParam (Uri, Status, LastUpdated, IsPartiallySupported) " +
                $"VALUES ('{url.ToString().Replace("'", "''", StringComparison.Ordinal)}', 'active', SYSDATETIMEOFFSET(), 0)",
                CancellationToken.None);
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

    private async Task<List<string>> EverythingAsync(string patientId, DateTimeOffset? since)
    {
        // The anchor type mirrors what PatientEverythingHandler now builds. It used to pass null here,
        // which Lower rejects outright ("$everything is not supported in system-level search"), making the
        // operation uncompilable as wired; that is fixed. LowerPatientEverything still never consults the
        // anchor -- the null-guard is its only reader -- so this stays the compartment root type, not the
        // set of types the traversal returns.
        var options = new SearchOptions
        {
            ResourceType = "Patient",
            Expression = new PatientEverythingExpression(
                patientId: patientId,
                startDate: null,
                endDate: null,
                sinceDate: since,
                filteredResourceTypes: null,
                includeReferencedResources: false),
            MaxItemCount = 50,
        };

        var ids = new List<string>();
        await foreach (var result in _service.SearchStreamAsync(options, CancellationToken.None))
        {
            ids.Add(result.ResourceId);
        }

        return ids;
    }

    [Fact]
    public async Task GivenAPatientCompartment_WhenEverythingIsSearched_ThenReturnsThePatientAndItsCompartmentMembers()
    {
        // Arrange
        var subjectParam = ParameterManager.GetSearchParameter("Observation", "subject");
        var patientId = $"everything-pat-{Guid.NewGuid():N}";
        await CreateResourceAsync("Patient", patientId, null);

        var memberId = $"everything-member-{Guid.NewGuid():N}";
        await CreateResourceAsync("Observation", memberId,
            [new SearchIndexEntry(subjectParam, new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: "Patient", resourceId: patientId))]);

        var strangerPatientId = $"everything-stranger-pat-{Guid.NewGuid():N}";
        await CreateResourceAsync("Patient", strangerPatientId, null);
        var strangerId = $"everything-stranger-{Guid.NewGuid():N}";
        await CreateResourceAsync("Observation", strangerId,
            [new SearchIndexEntry(subjectParam, new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: "Patient", resourceId: strangerPatientId))]);

        // Act
        var results = await EverythingAsync(patientId, since: null);

        // Assert
        results.OrderBy(x => x, StringComparer.Ordinal)
            .ShouldBe(new[] { patientId, memberId }.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public async Task GivenAPatientCompartmentWithASinceCutoff_WhenEverythingIsSearched_ThenOnlyMembersVisibleSinceThatTransactionAreReturned()
    {
        // Arrange -- an earlier and a later compartment member, written in separate transactions.
        var subjectParam = ParameterManager.GetSearchParameter("Observation", "subject");
        var patientId = $"since-pat-{Guid.NewGuid():N}";
        await CreateResourceAsync("Patient", patientId, null);

        var earlyId = $"since-early-{Guid.NewGuid():N}";
        await CreateResourceAsync("Observation", earlyId,
            [new SearchIndexEntry(subjectParam, new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: "Patient", resourceId: patientId))]);

        var lateId = $"since-late-{Guid.NewGuid():N}";
        await CreateResourceAsync("Observation", lateId,
            [new SearchIndexEntry(subjectParam, new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: "Patient", resourceId: patientId))]);

        // SqlServerFhirRepository.CreateOrUpdateAsync opens a dbo.Transactions row per write (via
        // MergeResourcesBeginTransaction) but never commits it, so every ledger row carries a NULL
        // VisibleDate. _since compares against exactly that column, so on this write path the filter
        // matches nothing whatever the predicate says, and the test would pass for the wrong reason.
        // Committing the ledger here -- ordered VisibleDate per real transaction id -- makes the
        // assertion about the emitted filter rather than about the merge pipeline.
        await _database.ExecuteNonQueryAsync(
            """
            UPDATE t
            SET t.VisibleDate = o.NewVisibleDate, t.IsCompleted = 1, t.IsSuccess = 1, t.IsVisible = 1
            FROM dbo.Transactions t
            JOIN (
                SELECT SurrogateIdRangeFirstValue,
                       DATEADD(minute, ROW_NUMBER() OVER (ORDER BY SurrogateIdRangeFirstValue), '2000-01-01T00:00:00') AS NewVisibleDate
                FROM dbo.Transactions
            ) o ON o.SurrogateIdRangeFirstValue = t.SurrogateIdRangeFirstValue
            """,
            CancellationToken.None);

        // The cutoff is the LATE observation's own transaction timestamp. `VisibleDate >= @since` is
        // inclusive, so exactly the late member is on or after it.
        var cutoff = await _database.ExecuteScalarAsync<DateTime>(
            $"SELECT t.VisibleDate FROM dbo.Resource r JOIN dbo.Transactions t ON r.TransactionId = t.SurrogateIdRangeFirstValue WHERE r.ResourceId = '{lateId}'",
            CancellationToken.None);

        // Sanity: without the cutoff both members are in the compartment.
        var unfiltered = await EverythingAsync(patientId, since: null);
        unfiltered.ShouldContain(earlyId);
        unfiltered.ShouldContain(lateId);

        // Act
        var results = await EverythingAsync(patientId, since: new DateTimeOffset(cutoff, TimeSpan.Zero));

        // Assert -- _since scopes the compartment branch only; the patient itself is always returned.
        results.ShouldContain(patientId);
        results.ShouldContain(lateId);
        results.ShouldNotContain(earlyId);
    }
}
