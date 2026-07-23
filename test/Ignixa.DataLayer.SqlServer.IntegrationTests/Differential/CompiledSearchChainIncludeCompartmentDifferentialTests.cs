using Ignixa.Abstractions;
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
using Shouldly;
using Xunit;
using CompartmentType = Ignixa.Specification.ValueSets.Normative.CompartmentType;
using SearchComparator = Ignixa.Specification.ValueSets.Normative.SearchComparator;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Differential;

/// <summary>
/// Proves the compiler-driven <see cref="Ignixa.DataLayer.SqlServer.Search.SqlServerCompiledSearchService"/>
/// agrees with the legacy EF-based <see cref="Ignixa.DataLayer.SqlEntityFramework.Search.SqlEntityFrameworkSearchService"/>
/// on chain, include/revinclude (+ single-hop <c>:iterate</c>), and compartment search -- the second of 3
/// differential-search harness tasks (Task 11 covered leaf/composite types, count, <c>:missing</c>; sort/paging
/// is Task 13). Sibling file to <see cref="CompiledSearchDifferentialTests"/>, split out per this initiative's
/// established file-size discipline (Task 11's file was already ~770 lines with 16 tests; folding this task's
/// 8 more tests into it would make a single file unwieldy) -- deliberately self-contained (its own
/// <c>ParameterManager</c>/<c>CreateResourceAsync</c>/<c>CollectAsync</c>/<c>AssertSameResults</c> helpers, not
/// shared with the sibling file), matching every other *DifferentialTests.cs sibling in this folder
/// (<c>BatchWriteDifferentialTests</c>, <c>HistoryDifferentialTests</c>, etc.), none of which share helpers
/// with each other either.
/// </summary>
public class CompiledSearchChainIncludeCompartmentDifferentialTests
{
    // Pure, I/O-free lookup structure over the pre-generated R4 catalog -- see CompiledSearchDifferentialTests'
    // identical field for the full rationale.
    private static readonly SearchParameterDefinitionManager ParameterManager = new(
        FhirVersion.R4.GetSchemaProvider(), NullLogger<SearchParameterDefinitionManager>.Instance);

    // Real R4 compartment definitions -- same instance shape DifferentialTestHarness.CreateAsync already
    // wires into both search services, used here only to enumerate what to seed (never for I/O).
    private static readonly CompartmentDefinitionManager CompartmentManager = new(FhirVersion.R4);

    /// <summary>
    /// Seeds every reference search parameter used by ANY member resource type of <paramref name="compartmentType"/>
    /// -- not just the ones relevant to the resource type(s) a particular query filters to. Required because
    /// Resolve.RunAsync's ResolveCompartmentMembership (Ignixa.Search.Sql) unconditionally walks the WHOLE
    /// compartment definition to build SymbolTable.CompartmentMembership, before Lower ever consults
    /// CompartmentSearchExpression.FilteredResourceTypes to narrow it down -- Resolve does all of the
    /// compiler's I/O up front and has no notion yet of "which groups Lower will actually keep." A catalog
    /// seeded with only the filtered type's own membership parameter(s) leaves every OTHER member type's
    /// parameter unresolved, which fails the whole compile (ResolvedSymbols.Unresolved), even though Lower
    /// would have discarded those groups anyway.
    /// </summary>
    private static async Task SeedCompartmentCatalogAsync(DifferentialTestHarness harness, CompartmentType compartmentType, CancellationToken cancellationToken)
    {
        var urls = new HashSet<Uri>();
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
                        urls.Add(url);
                    }
                }
            }
        }

        await harness.SeedSearchParameterCatalogAsync(urls, cancellationToken);
    }

    private static async Task CreateResourceAsync(
        DifferentialTestHarness harness,
        string resourceType,
        string resourceId,
        IReadOnlyList<object>? searchIndices,
        CancellationToken cancellationToken)
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

        await harness.LegacyRepository.CreateOrUpdateAsync(resource with { }, cancellationToken);
        await harness.NewRepository.CreateOrUpdateAsync(resource with { }, cancellationToken);
    }

    private static async Task<List<SearchEntryResult>> CollectAsync(IAsyncEnumerable<SearchEntryResult> results)
    {
        var list = new List<SearchEntryResult>();
        await foreach (var result in results)
        {
            list.Add(result);
        }

        return list;
    }

    private static void AssertSameResults(IReadOnlyList<SearchEntryResult> legacy, IReadOnlyList<SearchEntryResult> @new)
    {
        legacy.Count.ShouldBe(@new.Count);
        var legacyIds = legacy.Select(r => (r.ResourceType, r.ResourceId, r.SearchMode)).OrderBy(x => x).ToList();
        var newIds = @new.Select(r => (r.ResourceType, r.ResourceId, r.SearchMode)).OrderBy(x => x).ToList();
        legacyIds.ShouldBe(newIds);
    }

    [Fact]
    public async Task GivenAForwardChainQuery_WhenSearchedOnBothEngines_ThenReturnsTheSameResults()
    {
        // Arrange -- Observation?subject:Patient.name=Smith
        await using var harness = await DifferentialTestHarness.CreateAsync(CancellationToken.None);
        var subjectParam = ParameterManager.GetSearchParameter("Observation", "subject");
        var nameParam = ParameterManager.GetSearchParameter("Patient", "name");
        await harness.SeedSearchParameterCatalogAsync([subjectParam.Url!, nameParam.Url!], CancellationToken.None);

        var matchPatientId = $"diff-chain-fwd-patient-match-{Guid.NewGuid():N}";
        var otherPatientId = $"diff-chain-fwd-patient-other-{Guid.NewGuid():N}";
        await CreateResourceAsync(harness, "Patient", matchPatientId,
            [new SearchIndexEntry(nameParam, new StringSearchValue("Smith"))], CancellationToken.None);
        await CreateResourceAsync(harness, "Patient", otherPatientId,
            [new SearchIndexEntry(nameParam, new StringSearchValue("Jones"))], CancellationToken.None);

        var matchObservationId = $"diff-chain-fwd-obs-match-{Guid.NewGuid():N}";
        var otherObservationId = $"diff-chain-fwd-obs-other-{Guid.NewGuid():N}";
        await CreateResourceAsync(harness, "Observation", matchObservationId,
            [new SearchIndexEntry(subjectParam, new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: "Patient", resourceId: matchPatientId))],
            CancellationToken.None);
        await CreateResourceAsync(harness, "Observation", otherObservationId,
            [new SearchIndexEntry(subjectParam, new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: "Patient", resourceId: otherPatientId))],
            CancellationToken.None);

        var innerPredicate = new SearchParameterPredicateExpression(nameParam, SearchComparator.Eq, modifier: null, new StringSearchValue("Smith"));
        var chain = new ChainedExpression(["Observation"], subjectParam, ["Patient"], reversed: false, new SearchParameterExpression(nameParam, innerPredicate));
        var options = new SearchOptions { ResourceType = "Observation", Expression = chain };

        // Act
        var legacyResults = await CollectAsync(harness.LegacySearchService.SearchStreamAsync(options, CancellationToken.None));
        var newResults = await CollectAsync(harness.NewSearchService.SearchStreamAsync(options, CancellationToken.None));

        // Assert
        AssertSameResults(legacyResults, newResults);
        legacyResults.Select(r => r.ResourceId).ShouldBe([matchObservationId]);
    }

    [Fact]
    public async Task GivenAReverseChainQuery_WhenSearchedOnBothEngines_ThenReturnsTheSameResults()
    {
        // Arrange -- Patient?_has:Observation:subject:status=final -- the reverse of the forward-chain
        // test above, following the SAME subject reference param backwards (Observation.status is
        // filtered on the referencing side, instead of Patient.name on the referenced side).
        await using var harness = await DifferentialTestHarness.CreateAsync(CancellationToken.None);
        var subjectParam = ParameterManager.GetSearchParameter("Observation", "subject");
        var statusParam = ParameterManager.GetSearchParameter("Observation", "status");
        await harness.SeedSearchParameterCatalogAsync([subjectParam.Url!, statusParam.Url!], CancellationToken.None);

        var matchPatientId = $"diff-chain-rev-patient-match-{Guid.NewGuid():N}";
        var otherPatientId = $"diff-chain-rev-patient-other-{Guid.NewGuid():N}";
        await CreateResourceAsync(harness, "Patient", matchPatientId, null, CancellationToken.None);
        await CreateResourceAsync(harness, "Patient", otherPatientId, null, CancellationToken.None);

        var matchObservationId = $"diff-chain-rev-obs-match-{Guid.NewGuid():N}";
        var otherObservationId = $"diff-chain-rev-obs-other-{Guid.NewGuid():N}";
        await CreateResourceAsync(harness, "Observation", matchObservationId,
            [
                new SearchIndexEntry(subjectParam, new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: "Patient", resourceId: matchPatientId)),
                new SearchIndexEntry(statusParam, new TokenSearchValue(system: null, code: "final", text: null)),
            ],
            CancellationToken.None);
        await CreateResourceAsync(harness, "Observation", otherObservationId,
            [
                new SearchIndexEntry(subjectParam, new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: "Patient", resourceId: otherPatientId)),
                new SearchIndexEntry(statusParam, new TokenSearchValue(system: null, code: "preliminary", text: null)),
            ],
            CancellationToken.None);

        var innerPredicate = new SearchParameterPredicateExpression(statusParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "final", text: null));
        var chain = new ChainedExpression(["Observation"], subjectParam, ["Patient"], reversed: true, new SearchParameterExpression(statusParam, innerPredicate));
        var options = new SearchOptions { ResourceType = "Patient", Expression = chain };

        // Act
        var legacyResults = await CollectAsync(harness.LegacySearchService.SearchStreamAsync(options, CancellationToken.None));
        var newResults = await CollectAsync(harness.NewSearchService.SearchStreamAsync(options, CancellationToken.None));

        // Assert
        AssertSameResults(legacyResults, newResults);
        legacyResults.Select(r => r.ResourceId).ShouldBe([matchPatientId]);
    }

    [Fact]
    public async Task GivenASingleTypeIncludeExpression_WhenSearchedOnBothEngines_ThenReturnsTheSameResults()
    {
        // Arrange -- Patient?family=Include1&_include=Patient:organization. A real, single non-null
        // ResourceType ("Patient") on SearchOptions -- per the design doc's corrected finding,
        // BuildIncludeQuery (legacy's single-type streaming path) already filters by SearchParamId
        // correctly, so this must show NO divergence (ordinary AssertSameResults), unlike Step 3's
        // multi-type case below.
        await using var harness = await DifferentialTestHarness.CreateAsync(CancellationToken.None);
        var familyParam = ParameterManager.GetSearchParameter("Patient", "family");
        var organizationParam = ParameterManager.GetSearchParameter("Patient", "organization");
        await harness.SeedSearchParameterCatalogAsync([familyParam.Url!, organizationParam.Url!], CancellationToken.None);

        var orgId = $"diff-include-org-{Guid.NewGuid():N}";
        await CreateResourceAsync(harness, "Organization", orgId, null, CancellationToken.None);

        var matchPatientId = $"diff-include-patient-match-{Guid.NewGuid():N}";
        var otherPatientId = $"diff-include-patient-other-{Guid.NewGuid():N}";
        await CreateResourceAsync(harness, "Patient", matchPatientId,
            [
                new SearchIndexEntry(familyParam, new StringSearchValue("Include1")),
                new SearchIndexEntry(organizationParam, new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: "Organization", resourceId: orgId)),
            ],
            CancellationToken.None);
        await CreateResourceAsync(harness, "Patient", otherPatientId,
            [new SearchIndexEntry(familyParam, new StringSearchValue("Include2"))], CancellationToken.None);

        var include = new IncludeExpression(["Patient"], organizationParam, "Patient", "Organization", null, wildCard: false, reversed: false, iterate: false);
        var options = new SearchOptions
        {
            ResourceType = "Patient",
            Expression = new SearchParameterExpression(
                familyParam,
                new SearchParameterPredicateExpression(familyParam, SearchComparator.Eq, modifier: null, new StringSearchValue("Include1"))),
            Include = [include],
        };

        // Act
        var legacyResults = await CollectAsync(harness.LegacySearchService.SearchStreamAsync(options, CancellationToken.None));
        var newResults = await CollectAsync(harness.NewSearchService.SearchStreamAsync(options, CancellationToken.None));

        // Assert
        AssertSameResults(legacyResults, newResults);
        legacyResults.Select(r => r.ResourceId).OrderBy(x => x).ShouldBe(new[] { matchPatientId, orgId }.OrderBy(x => x));
        legacyResults.Single(r => r.ResourceId == matchPatientId).SearchMode.ShouldBe(SearchEntryMode.Match);
        legacyResults.Single(r => r.ResourceId == orgId).SearchMode.ShouldBe(SearchEntryMode.Include);
    }

    [Fact]
    public async Task GivenASingleTypeRevIncludeExpression_WhenSearchedOnBothEngines_ThenReturnsTheSameResults()
    {
        // Arrange -- Patient?family=Rev1&_revinclude=Observation:subject. Same single-type/no-divergence
        // reasoning as the forward-include test above, for BuildRevIncludeQuery.
        await using var harness = await DifferentialTestHarness.CreateAsync(CancellationToken.None);
        var familyParam = ParameterManager.GetSearchParameter("Patient", "family");
        var subjectParam = ParameterManager.GetSearchParameter("Observation", "subject");
        await harness.SeedSearchParameterCatalogAsync([familyParam.Url!, subjectParam.Url!], CancellationToken.None);

        var matchPatientId = $"diff-revinclude-patient-match-{Guid.NewGuid():N}";
        var otherPatientId = $"diff-revinclude-patient-other-{Guid.NewGuid():N}";
        await CreateResourceAsync(harness, "Patient", matchPatientId,
            [new SearchIndexEntry(familyParam, new StringSearchValue("Rev1"))], CancellationToken.None);
        await CreateResourceAsync(harness, "Patient", otherPatientId,
            [new SearchIndexEntry(familyParam, new StringSearchValue("Rev2"))], CancellationToken.None);

        var matchObservationId = $"diff-revinclude-obs-match-{Guid.NewGuid():N}";
        var otherObservationId = $"diff-revinclude-obs-other-{Guid.NewGuid():N}";
        await CreateResourceAsync(harness, "Observation", matchObservationId,
            [new SearchIndexEntry(subjectParam, new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: "Patient", resourceId: matchPatientId))],
            CancellationToken.None);
        await CreateResourceAsync(harness, "Observation", otherObservationId,
            [new SearchIndexEntry(subjectParam, new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: "Patient", resourceId: otherPatientId))],
            CancellationToken.None);

        var revInclude = new IncludeExpression(["Observation"], subjectParam, "Observation", "Patient", null, wildCard: false, reversed: true, iterate: false);
        var options = new SearchOptions
        {
            ResourceType = "Patient",
            Expression = new SearchParameterExpression(
                familyParam,
                new SearchParameterPredicateExpression(familyParam, SearchComparator.Eq, modifier: null, new StringSearchValue("Rev1"))),
            RevInclude = [revInclude],
        };

        // Act
        var legacyResults = await CollectAsync(harness.LegacySearchService.SearchStreamAsync(options, CancellationToken.None));
        var newResults = await CollectAsync(harness.NewSearchService.SearchStreamAsync(options, CancellationToken.None));

        // Assert
        AssertSameResults(legacyResults, newResults);
        legacyResults.Select(r => r.ResourceId).OrderBy(x => x).ShouldBe(new[] { matchPatientId, matchObservationId }.OrderBy(x => x));
        legacyResults.Single(r => r.ResourceId == matchPatientId).SearchMode.ShouldBe(SearchEntryMode.Match);
        legacyResults.Single(r => r.ResourceId == matchObservationId).SearchMode.ShouldBe(SearchEntryMode.Include);
    }

    [Fact]
    public async Task GivenAMultiTypeWildcardIncludeOverlappingAnUnrelatedReferenceParameter_WhenSearchedOnBothEngines_ThenTheyDeliberatelyDiverge()
    {
        // Arrange -- a multi-type (ResourceType null/empty) search with _include=Patient:organization,
        // where two DIFFERENT reference search parameters on the SAME resource (Patient.organization and
        // Patient.general-practitioner) both point at a shared target resource type (Organization). Legacy's
        // multi-type dispatch (SqlEntityFrameworkSearchService.SearchStreamAsync: "For wildcard/multi-type
        // searches (ResourceType is null or empty)...") routes through IncludeProcessor.ProcessSingleIncludeAsync,
        // which filters ONLY by ReferenceResourceTypeId (confirmed: IncludeProcessor.cs's non-wildcard branch
        // builds `referenceQuery` from ResourceSurrogateId + ReferenceResourceTypeId alone -- no SearchParamId
        // predicate anywhere in that method) -- so it pulls in the Organization referenced via
        // general-practitioner too, even though only `_include=Patient:organization` was requested.
        await using var harness = await DifferentialTestHarness.CreateAsync(CancellationToken.None);
        var familyParam = ParameterManager.GetSearchParameter("Patient", "family");
        var organizationParam = ParameterManager.GetSearchParameter("Patient", "organization");
        var generalPractitionerParam = ParameterManager.GetSearchParameter("Patient", "general-practitioner");
        await harness.SeedSearchParameterCatalogAsync(
            [familyParam.Url!, organizationParam.Url!, generalPractitionerParam.Url!], CancellationToken.None);

        var wantedOrgId = $"diff-multiinclude-org-wanted-{Guid.NewGuid():N}";
        var unwantedOrgId = $"diff-multiinclude-org-unwanted-{Guid.NewGuid():N}";
        await CreateResourceAsync(harness, "Organization", wantedOrgId, null, CancellationToken.None);
        await CreateResourceAsync(harness, "Organization", unwantedOrgId, null, CancellationToken.None);

        var patientId = $"diff-multiinclude-patient-{Guid.NewGuid():N}";
        await CreateResourceAsync(harness, "Patient", patientId,
            [
                new SearchIndexEntry(familyParam, new StringSearchValue("MultiInclude1")),
                new SearchIndexEntry(organizationParam, new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: "Organization", resourceId: wantedOrgId)),
                new SearchIndexEntry(generalPractitionerParam, new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: "Organization", resourceId: unwantedOrgId)),
            ],
            CancellationToken.None);

        var include = new IncludeExpression(["Patient"], organizationParam, "Patient", "Organization", null, wildCard: false, reversed: false, iterate: false);
        var options = new SearchOptions
        {
            ResourceType = null,
            Expression = new SearchParameterExpression(
                familyParam,
                new SearchParameterPredicateExpression(familyParam, SearchComparator.Eq, modifier: null, new StringSearchValue("MultiInclude1"))),
            Include = [include],
        };

        // Act
        var legacyResults = await CollectAsync(harness.LegacySearchService.SearchStreamAsync(options, CancellationToken.None));

        // Assert -- documented divergence, per the design doc's "multi-type wildcard-include" entry --
        // but stronger than the design doc's original phrasing ("the compiler's SearchParamId-filtered
        // version correctly excludes" the unwanted resource) predicted: Lower.Run unconditionally throws
        // NotSupportedException whenever targetResourceType is null and includes/revIncludes are non-empty
        // (Lower.cs:100-109, "_include/_revinclude combined with a system-level search... is not supported --
        // BuildIncludeStages needs a concrete match resource type to compute SeedFromMatch"), regardless of
        // whether the include is wildcard or a specific SearchParamId. This is a pre-existing, deliberate,
        // self-documented compiler scope boundary from the compiler's own Phase 7/8 (not a new bug this task
        // discovered, and not something to silently "fix" -- see task report), so the compiler never gets far
        // enough to apply its SearchParamId filter at all for this shape; it refuses the whole request via
        // RequestNotValidException (400) instead. The divergence direction the design doc cares about still
        // holds -- legacy silently returns the wrong extra resource, the compiler never does -- just via
        // outright refusal rather than filtered execution.
        legacyResults.Select(r => r.ResourceId).ShouldContain(patientId);
        legacyResults.Select(r => r.ResourceId).ShouldContain(wantedOrgId);
        legacyResults.Select(r => r.ResourceId).ShouldContain(unwantedOrgId);
        legacyResults.Single(r => r.ResourceId == unwantedOrgId).SearchMode.ShouldBe(SearchEntryMode.Include);

        await Should.ThrowAsync<RequestNotValidException>(async () =>
        {
            await foreach (var _ in harness.NewSearchService.SearchStreamAsync(options, CancellationToken.None))
            {
            }
        });
    }

    [Fact]
    public async Task GivenASingleHopIterateInclude_WhenSearchedOnBothEngines_ThenReturnsTheSameResults()
    {
        // Arrange -- Patient?family=Iter1&_include=Patient:organization&_include:iterate=Organization:partof.
        // ONE hop only (org-root has no partOf reference of its own, so there is nothing for a second hop to
        // find) -- per the design doc's explicit scope boundary, the compiler supports one Kahn-sorted hop per
        // expression, unlike the live IterateProcessor's runtime fixpoint; this query set must not exercise
        // recursion beyond that.
        await using var harness = await DifferentialTestHarness.CreateAsync(CancellationToken.None);
        var familyParam = ParameterManager.GetSearchParameter("Patient", "family");
        var organizationParam = ParameterManager.GetSearchParameter("Patient", "organization");
        var partOfParam = ParameterManager.GetSearchParameter("Organization", "partof");
        await harness.SeedSearchParameterCatalogAsync(
            [familyParam.Url!, organizationParam.Url!, partOfParam.Url!], CancellationToken.None);

        var orgRootId = $"diff-iterate-org-root-{Guid.NewGuid():N}";
        await CreateResourceAsync(harness, "Organization", orgRootId, null, CancellationToken.None);

        var orgChildId = $"diff-iterate-org-child-{Guid.NewGuid():N}";
        await CreateResourceAsync(harness, "Organization", orgChildId,
            [new SearchIndexEntry(partOfParam, new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: "Organization", resourceId: orgRootId))],
            CancellationToken.None);

        var matchPatientId = $"diff-iterate-patient-match-{Guid.NewGuid():N}";
        var otherPatientId = $"diff-iterate-patient-other-{Guid.NewGuid():N}";
        await CreateResourceAsync(harness, "Patient", matchPatientId,
            [
                new SearchIndexEntry(familyParam, new StringSearchValue("Iter1")),
                new SearchIndexEntry(organizationParam, new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: "Organization", resourceId: orgChildId)),
            ],
            CancellationToken.None);
        await CreateResourceAsync(harness, "Patient", otherPatientId,
            [new SearchIndexEntry(familyParam, new StringSearchValue("Iter2"))], CancellationToken.None);

        var nonIterateInclude = new IncludeExpression(["Patient"], organizationParam, "Patient", "Organization", null, wildCard: false, reversed: false, iterate: false);
        var iterateInclude = new IncludeExpression(["Organization"], partOfParam, "Organization", "Organization", null, wildCard: false, reversed: false, iterate: true);
        var options = new SearchOptions
        {
            ResourceType = "Patient",
            Expression = new SearchParameterExpression(
                familyParam,
                new SearchParameterPredicateExpression(familyParam, SearchComparator.Eq, modifier: null, new StringSearchValue("Iter1"))),
            Include = [nonIterateInclude, iterateInclude],
        };

        // Act
        var legacyResults = await CollectAsync(harness.LegacySearchService.SearchStreamAsync(options, CancellationToken.None));
        var newResults = await CollectAsync(harness.NewSearchService.SearchStreamAsync(options, CancellationToken.None));

        // Assert
        AssertSameResults(legacyResults, newResults);
        legacyResults.Select(r => r.ResourceId).OrderBy(x => x)
            .ShouldBe(new[] { matchPatientId, orgChildId, orgRootId }.OrderBy(x => x));
        legacyResults.Single(r => r.ResourceId == matchPatientId).SearchMode.ShouldBe(SearchEntryMode.Match);
        legacyResults.Single(r => r.ResourceId == orgChildId).SearchMode.ShouldBe(SearchEntryMode.Include);
        legacyResults.Single(r => r.ResourceId == orgRootId).SearchMode.ShouldBe(SearchEntryMode.Include);
    }

    [Fact]
    public async Task GivenAnOrdinaryCompartmentSearch_WhenSearchedOnBothEngines_ThenReturnsTheSameResults()
    {
        // Arrange -- GET /Patient/{patientId}/Observation, no natural-ID collision. Most compartment
        // searches have no such collision and should show no divergence (per the design doc: only the
        // ReferenceResourceTypeId-blind case below diverges).
        await using var harness = await DifferentialTestHarness.CreateAsync(CancellationToken.None);
        var subjectParam = ParameterManager.GetSearchParameter("Observation", "subject");
        await SeedCompartmentCatalogAsync(harness, CompartmentType.Patient, CancellationToken.None);

        var patientId = $"diff-compartment-patient-{Guid.NewGuid():N}";
        var otherPatientId = $"diff-compartment-other-patient-{Guid.NewGuid():N}";
        await CreateResourceAsync(harness, "Patient", patientId, null, CancellationToken.None);
        await CreateResourceAsync(harness, "Patient", otherPatientId, null, CancellationToken.None);

        var matchObservationId = $"diff-compartment-obs-match-{Guid.NewGuid():N}";
        var otherObservationId = $"diff-compartment-obs-other-{Guid.NewGuid():N}";
        await CreateResourceAsync(harness, "Observation", matchObservationId,
            [new SearchIndexEntry(subjectParam, new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: "Patient", resourceId: patientId))],
            CancellationToken.None);
        await CreateResourceAsync(harness, "Observation", otherObservationId,
            [new SearchIndexEntry(subjectParam, new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: "Patient", resourceId: otherPatientId))],
            CancellationToken.None);

        var compartment = new CompartmentSearchExpression("Patient", patientId, new HashSet<string> { "Observation" });
        var options = new SearchOptions { ResourceType = "Observation", Expression = compartment };

        // Act
        var legacyResults = await CollectAsync(harness.LegacySearchService.SearchStreamAsync(options, CancellationToken.None));
        var newResults = await CollectAsync(harness.NewSearchService.SearchStreamAsync(options, CancellationToken.None));

        // Assert
        AssertSameResults(legacyResults, newResults);
        legacyResults.Select(r => r.ResourceId).ShouldBe([matchObservationId]);
    }

    [Fact]
    public async Task GivenACompartmentSearchWithANaturalIdCollisionAcrossResourceTypes_WhenSearchedOnBothEngines_ThenTheyDeliberatelyDiverge()
    {
        // Arrange -- two resources of DIFFERENT types (Patient and Practitioner) sharing the same natural
        // ResourceId value, one of which is a genuine compartment member and one of which is not. Legacy's
        // CompartmentSearchQueryGenerator queries ReferenceSearchParam by `SearchParamId == X &&
        // ReferenceResourceId == compartmentId` only (confirmed: CompartmentSearchQueryGenerator.cs:182-183)
        // -- it never checks ReferenceResourceTypeId, so an Observation.subject reference pointing at
        // Practitioner/{id} is indistinguishable from one pointing at Patient/{id} once only the natural ID
        // string is compared. The compiler's CompartmentSource additionally filters
        // ReferenceResourceTypeId = the compartment's own resource type ID, correctly excluding the decoy.
        await using var harness = await DifferentialTestHarness.CreateAsync(CancellationToken.None);
        var subjectParam = ParameterManager.GetSearchParameter("Observation", "subject");
        await SeedCompartmentCatalogAsync(harness, CompartmentType.Patient, CancellationToken.None);

        // ResourceId is a VARCHAR(64) column (dbo.Resource.ResourceId / dbo.ReferenceSearchParam.ReferenceResourceId)
        // -- keep every generated id here well under that limit, unlike a first pass that silently
        // truncated at write time.
        var collidingId = $"diff-cc-collide-{Guid.NewGuid():N}";
        await CreateResourceAsync(harness, "Patient", collidingId, null, CancellationToken.None);
        await CreateResourceAsync(harness, "Practitioner", collidingId, null, CancellationToken.None);

        var realMemberId = $"diff-cc-real-{Guid.NewGuid():N}";
        var decoyMemberId = $"diff-cc-decoy-{Guid.NewGuid():N}";
        await CreateResourceAsync(harness, "Observation", realMemberId,
            [new SearchIndexEntry(subjectParam, new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: "Patient", resourceId: collidingId))],
            CancellationToken.None);
        await CreateResourceAsync(harness, "Observation", decoyMemberId,
            [new SearchIndexEntry(subjectParam, new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: "Practitioner", resourceId: collidingId))],
            CancellationToken.None);

        var compartment = new CompartmentSearchExpression("Patient", collidingId, new HashSet<string> { "Observation" });
        var options = new SearchOptions { ResourceType = "Observation", Expression = compartment };

        // Act
        var legacyResults = await CollectAsync(harness.LegacySearchService.SearchStreamAsync(options, CancellationToken.None));
        var newResults = await CollectAsync(harness.NewSearchService.SearchStreamAsync(options, CancellationToken.None));

        // Assert -- documented divergence: legacy incorrectly includes the Practitioner-referencing decoy
        // (ReferenceResourceTypeId-blind), the compiler correctly excludes it.
        legacyResults.Select(r => r.ResourceId).OrderBy(x => x)
            .ShouldBe(new[] { realMemberId, decoyMemberId }.OrderBy(x => x));
        newResults.Select(r => r.ResourceId).ShouldBe([realMemberId]);
        newResults.Count.ShouldBeLessThan(legacyResults.Count);
    }
}
