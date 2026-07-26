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
/// Executes the two <c>Patient/$everything</c> paths that no test on either branch had ever run against a
/// database.
/// <para>
/// The first is the referenced-type expansion. <c>PatientEverythingSinceExecutionTests</c> passes
/// <c>includeReferencedResources: false</c> in both of its facts, so the expansion CTE had no executed
/// coverage at all -- including the seed fix that unions the patient's own row into the expansion source.
/// Before that fix the expansion was seeded from the compartment alone, and since no
/// <c>ReferenceSearchParam</c> row points from a patient at itself, a Practitioner or Organization reachable
/// only from the patient row was silently dropped. This test isolates exactly that: the Practitioner and
/// Organization here are referenced by the patient and by nothing else, so the pre-fix traversal returns
/// neither.
/// </para>
/// <para>
/// The second is <c>_since</c> over the ordinary write path, which is expected to return nothing and is
/// documented as such on the test itself.
/// </para>
/// </summary>
#pragma warning disable CA1001
public class PatientEverythingExpansionExecutionTests : IAsyncLifetime
#pragma warning restore CA1001
{
    private static readonly SearchParameterDefinitionManager ParameterManager = new(
        FhirVersion.R4.GetSchemaProvider(), NullLogger<SearchParameterDefinitionManager>.Instance);

    private static readonly CompartmentDefinitionManager CompartmentManager = new(FhirVersion.R4);

    // The expansion's own target list (StructuralContext.PatientEverythingReferencedResourceTypes). These
    // parameters are not compartment membership parameters, so the compartment catalog seeding below does
    // not cover them, and an unregistered parameter fails the compile rather than returning fewer rows.
    private static readonly (string ResourceType, string Code)[] ExpansionParameters =
    [
        ("Patient", "general-practitioner"),
        ("Patient", "organization"),
        ("Encounter", "location"),
    ];

    private TestTenantDatabase _database = null!;
    private SqlServerSearchIndexReferenceDataCache _searchCache = null!;
    private SqlServerCompiledSearchService _service = null!;

    public async Task InitializeAsync()
    {
        _database = await TestTenantDatabase.CreateSqlServerFhirRepositoryAsync();

        _searchCache = new SqlServerSearchIndexReferenceDataCache(
            _database.SqlExecutionService, _database.TenantId, NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance);
        await _searchCache.PreloadResourceTypesAsync(CancellationToken.None);

        await SeedSearchParameterCatalogAsync();

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

    // Resolve.RunAsync walks the whole Patient compartment definition before Lower narrows it, so every
    // member type's membership parameter has to resolve or the compile fails outright. The expansion
    // parameters are added on top for the same reason.
    private async Task SeedSearchParameterCatalogAsync()
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

        foreach (var (resourceType, code) in ExpansionParameters)
        {
            if (ParameterManager.TryGetSearchParameter(resourceType, code, out var searchParam) && searchParam.Url is { } url)
            {
                urls.Add(url);
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

    private static SearchIndexEntry Reference(SearchParameterInfo parameter, string targetType, string targetId)
        => new(parameter, new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: targetType, resourceId: targetId));

    private async Task<List<string>> EverythingAsync(string patientId, DateTimeOffset? since, bool includeReferencedResources)
    {
        var options = new SearchOptions
        {
            ResourceType = "Patient",
            Expression = new PatientEverythingExpression(
                patientId: patientId,
                startDate: null,
                endDate: null,
                sinceDate: since,
                filteredResourceTypes: null,
                includeReferencedResources: includeReferencedResources),
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
    public async Task GivenReferencedResourcesReachableOnlyFromThePatientRow_WhenEverythingIncludesThem_ThenTheyAreReturned()
    {
        // Arrange
        var generalPractitionerParam = ParameterManager.GetSearchParameter("Patient", "general-practitioner");
        var patientOrganizationParam = ParameterManager.GetSearchParameter("Patient", "organization");
        var observationSubjectParam = ParameterManager.GetSearchParameter("Observation", "subject");
        // The R4 Patient compartment links Encounter through "patient", not "subject" (see
        // R4CompartmentDefinitions: ("Encounter", ["patient"])). Indexing the reference under any other
        // parameter leaves the Encounter outside the compartment entirely.
        var encounterPatientParam = ParameterManager.GetSearchParameter("Encounter", "patient");
        var encounterLocationParam = ParameterManager.GetSearchParameter("Encounter", "location");

        var practitionerId = $"expansion-prac-{Guid.NewGuid():N}";
        var organizationId = $"expansion-org-{Guid.NewGuid():N}";
        var locationId = $"expansion-loc-{Guid.NewGuid():N}";
        await CreateResourceAsync("Practitioner", practitionerId, null);
        await CreateResourceAsync("Organization", organizationId, null);
        await CreateResourceAsync("Location", locationId, null);

        // The Practitioner and Organization are referenced by the patient row and by nothing else. If any
        // compartment member also referenced them, the compartment branch alone would surface them and the
        // seed-union fix would be untestable -- this is the isolation the whole test depends on.
        var patientId = $"expansion-pat-{Guid.NewGuid():N}";
        await CreateResourceAsync("Patient", patientId,
        [
            Reference(generalPractitionerParam, "Practitioner", practitionerId),
            Reference(patientOrganizationParam, "Organization", organizationId),
        ]);

        var observationId = $"expansion-obs-{Guid.NewGuid():N}";
        await CreateResourceAsync("Observation", observationId,
            [Reference(observationSubjectParam, "Patient", patientId)]);

        // The Location is reachable only through a compartment member, proving the expansion reaches
        // through the compartment branch too and not only through the patient-itself branch.
        var encounterId = $"expansion-enc-{Guid.NewGuid():N}";
        await CreateResourceAsync("Encounter", encounterId,
        [
            Reference(encounterPatientParam, "Patient", patientId),
            Reference(encounterLocationParam, "Location", locationId),
        ]);

        // A stranger patient with its own compartment member and its own referenced practitioner, to prove
        // the traversal does not leak across patients.
        var strangerPractitionerId = $"expansion-stranger-prac-{Guid.NewGuid():N}";
        await CreateResourceAsync("Practitioner", strangerPractitionerId, null);
        var strangerPatientId = $"expansion-stranger-pat-{Guid.NewGuid():N}";
        await CreateResourceAsync("Patient", strangerPatientId,
            [Reference(generalPractitionerParam, "Practitioner", strangerPractitionerId)]);
        var strangerObservationId = $"expansion-stranger-obs-{Guid.NewGuid():N}";
        await CreateResourceAsync("Observation", strangerObservationId,
            [Reference(observationSubjectParam, "Patient", strangerPatientId)]);

        // Act
        var results = await EverythingAsync(patientId, since: null, includeReferencedResources: true);

        // Assert -- exactly the patient, its compartment members, and the resources referenced from either.
        results.OrderBy(x => x, StringComparer.Ordinal).ShouldBe(
            new[] { patientId, observationId, encounterId, practitionerId, organizationId, locationId }
                .OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public async Task GivenTheExpansionIsNotRequested_WhenEverythingIsSearched_ThenReferencedResourcesAreExcluded()
    {
        // The companion to the test above: the same graph with includeReferencedResources: false must return
        // the compartment only. Without this, a traversal that returned the referenced resources
        // unconditionally would still satisfy the assertion above.
        var generalPractitionerParam = ParameterManager.GetSearchParameter("Patient", "general-practitioner");
        var observationSubjectParam = ParameterManager.GetSearchParameter("Observation", "subject");

        var practitionerId = $"noexpansion-prac-{Guid.NewGuid():N}";
        await CreateResourceAsync("Practitioner", practitionerId, null);

        var patientId = $"noexpansion-pat-{Guid.NewGuid():N}";
        await CreateResourceAsync("Patient", patientId,
            [Reference(generalPractitionerParam, "Practitioner", practitionerId)]);

        var observationId = $"noexpansion-obs-{Guid.NewGuid():N}";
        await CreateResourceAsync("Observation", observationId,
            [Reference(observationSubjectParam, "Patient", patientId)]);

        // Act
        var results = await EverythingAsync(patientId, since: null, includeReferencedResources: false);

        // Assert
        results.OrderBy(x => x, StringComparer.Ordinal)
            .ShouldBe(new[] { patientId, observationId }.OrderBy(x => x, StringComparer.Ordinal));
        results.ShouldNotContain(practitionerId);
    }

    [Fact]
    public async Task GivenASinceQueryAgainstTheProductionWritePath_WhenEverythingIsSearched_ThenNoMemberIsReturnedBecauseVisibleDateIsNeverCommitted()
    {
        // A PASS HERE DOCUMENTS A DEFECT, IT DOES NOT VALIDATE CORRECT BEHAVIOUR.
        //
        // SqlServerFhirRepository.CreateOrUpdateAsync opens a dbo.Transactions row per write via
        // MergeResourcesBeginTransaction and never commits it. Only the MergeResources stored-procedure
        // path (with @TransactionId supplied, so it calls MergeResourcesCommitTransaction internally) sets
        // VisibleDate. On this write path VisibleDate stays NULL forever, and _since filters on exactly
        // that column, so the filter matches nothing regardless of the cutoff.
        //
        // PatientEverythingSinceExecutionTests covers the emitted filter itself, and works around this by
        // issuing a manual UPDATE of dbo.Transactions first. This test deliberately does not, so the
        // production write path's behaviour is pinned rather than bypassed. It turns green -- and should
        // then be rewritten to assert the member IS returned -- the day CreateOrUpdateAsync's transaction
        // lifecycle is fixed. See docs/superpowers/specs/2026-07-25-patient-everything-branch-a-handoff.md.
        var observationSubjectParam = ParameterManager.GetSearchParameter("Observation", "subject");

        var cutoff = await _database.ExecuteScalarAsync<DateTime>(
            "SELECT SYSUTCDATETIME()", CancellationToken.None);

        var patientId = $"since-writepath-pat-{Guid.NewGuid():N}";
        await CreateResourceAsync("Patient", patientId, null);

        var memberId = $"since-writepath-obs-{Guid.NewGuid():N}";
        await CreateResourceAsync("Observation", memberId,
            [Reference(observationSubjectParam, "Patient", patientId)]);

        // Sanity: with no cutoff the member is in the compartment, so its absence below is the _since
        // filter's doing and not a seeding failure.
        var unfiltered = await EverythingAsync(patientId, since: null, includeReferencedResources: false);
        unfiltered.ShouldContain(memberId);

        // Every write above happened after this cutoff, so a working _since would admit the member.
        var results = await EverythingAsync(
            patientId, since: new DateTimeOffset(cutoff, TimeSpan.Zero).AddMinutes(-1), includeReferencedResources: false);

        // The patient-itself branch is never filtered by _since, so it still returns.
        results.ShouldContain(patientId);
        results.ShouldNotContain(memberId);

        // Pin the cause, so a future reader does not mistake this for the filter being over-restrictive.
        var nullVisibleDates = await _database.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.Transactions WHERE VisibleDate IS NULL", CancellationToken.None);
        nullVisibleDates.ShouldBeGreaterThan(0);
    }
}
