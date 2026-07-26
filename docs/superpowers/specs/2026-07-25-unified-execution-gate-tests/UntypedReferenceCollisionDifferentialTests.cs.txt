using Ignixa.Abstractions;
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
using SearchComparator = Ignixa.Specification.ValueSets.Normative.SearchComparator;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Differential;

/// <summary>
/// Executes the declared-target narrowing for an UNTYPED reference search value -- a query like
/// <c>Observation?subject={id}</c> where the value carries no <c>ResourceType/</c> prefix.
///
/// Previously the untyped case lowered to an id-only predicate, so a reference row pointing at
/// <c>Practitioner/{id}</c> was indistinguishable from one pointing at <c>Patient/{id}</c> once two
/// resources of different types happened to share the same natural id. The narrowing constrains the
/// untyped value to the search parameter's own declared target types, which is what the shipping
/// engine does.
///
/// This asserts at ROW level, not on emitted SQL text: it is the only kind of evidence that can say
/// which rows the change removed, and whether removing them was right.
/// </summary>
public class UntypedReferenceCollisionDifferentialTests
{
    private static readonly SearchParameterDefinitionManager ParameterManager = new(
        FhirVersion.R4.GetSchemaProvider(), NullLogger<SearchParameterDefinitionManager>.Instance);

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

    [Fact]
    public async Task GivenAnUntypedReferenceSearchWithANaturalIdCollisionAcrossResourceTypes_WhenSearchedOnBothEngines_ThenTheCompilerExcludesTheUndeclaredTarget()
    {
        // Arrange -- Patient/{X} and Practitioner/{X} share a natural id. Observation.subject declares
        // Patient|Group|Device|Location as its targets; Practitioner is NOT among them, so a subject
        // reference pointing at Practitioner/{X} can never be a legitimate match for subject={X}.
        await using var harness = await DifferentialTestHarness.CreateAsync(CancellationToken.None);
        var subjectParam = ParameterManager.GetSearchParameter("Observation", "subject");
        await harness.SeedSearchParameterCatalogAsync([subjectParam.Url!], CancellationToken.None);

        subjectParam.TargetResourceTypes.ShouldContain("Patient");
        subjectParam.TargetResourceTypes.ShouldNotContain("Practitioner");

        // dbo.Resource.ResourceId is VARCHAR(64) -- keep the generated id well under that.
        var collidingId = $"untyped-collide-{Guid.NewGuid():N}";
        await CreateResourceAsync(harness, "Patient", collidingId, null, CancellationToken.None);
        await CreateResourceAsync(harness, "Practitioner", collidingId, null, CancellationToken.None);

        var realMatchId = $"untyped-real-{Guid.NewGuid():N}";
        var decoyId = $"untyped-decoy-{Guid.NewGuid():N}";
        await CreateResourceAsync(harness, "Observation", realMatchId,
            [new SearchIndexEntry(subjectParam, new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: "Patient", resourceId: collidingId))],
            CancellationToken.None);
        await CreateResourceAsync(harness, "Observation", decoyId,
            [new SearchIndexEntry(subjectParam, new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: "Practitioner", resourceId: collidingId))],
            CancellationToken.None);

        // The query value is UNTYPED: no resource type, just the bare id.
        var untypedValue = new ReferenceSearchValue(ReferenceKind.InternalOrExternal, baseUri: null!, resourceType: null!, resourceId: collidingId);
        var expression = new SearchParameterExpression(
            subjectParam,
            new SearchParameterPredicateExpression(subjectParam, SearchComparator.Eq, modifier: null, untypedValue));
        var options = new SearchOptions { ResourceType = "Observation", Expression = expression };

        // Act
        var legacyResults = await CollectAsync(harness.LegacySearchService.SearchStreamAsync(options, CancellationToken.None));
        var newResults = await CollectAsync(harness.NewSearchService.SearchStreamAsync(options, CancellationToken.None));

        // Assert -- the compiler returns only the Observation whose subject points at a DECLARED target
        // type. Whatever the legacy engine does here, returning the Practitioner-referencing decoy for
        // subject={id} is a false positive: Observation.subject cannot point at a Practitioner.
        newResults.Select(r => r.ResourceId).ShouldBe([realMatchId]);
        legacyResults.ShouldNotBeNull();
    }

    [Fact]
    public async Task GivenAnUntypedReferenceSearchWithNoCollision_WhenSearchedOnBothEngines_ThenBothEnginesStillReturnTheMatch()
    {
        // Arrange -- the narrowing must not cost a legitimate match. Same shape as above, minus the
        // colliding Practitioner: the ordinary untyped search has to keep working.
        await using var harness = await DifferentialTestHarness.CreateAsync(CancellationToken.None);
        var subjectParam = ParameterManager.GetSearchParameter("Observation", "subject");
        await harness.SeedSearchParameterCatalogAsync([subjectParam.Url!], CancellationToken.None);

        var patientId = $"untyped-plain-pat-{Guid.NewGuid():N}";
        await CreateResourceAsync(harness, "Patient", patientId, null, CancellationToken.None);

        var matchId = $"untyped-plain-obs-{Guid.NewGuid():N}";
        var otherId = $"untyped-plain-other-{Guid.NewGuid():N}";
        var otherPatientId = $"untyped-plain-pat2-{Guid.NewGuid():N}";
        await CreateResourceAsync(harness, "Patient", otherPatientId, null, CancellationToken.None);
        await CreateResourceAsync(harness, "Observation", matchId,
            [new SearchIndexEntry(subjectParam, new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: "Patient", resourceId: patientId))],
            CancellationToken.None);
        await CreateResourceAsync(harness, "Observation", otherId,
            [new SearchIndexEntry(subjectParam, new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: "Patient", resourceId: otherPatientId))],
            CancellationToken.None);

        var untypedValue = new ReferenceSearchValue(ReferenceKind.InternalOrExternal, baseUri: null!, resourceType: null!, resourceId: patientId);
        var expression = new SearchParameterExpression(
            subjectParam,
            new SearchParameterPredicateExpression(subjectParam, SearchComparator.Eq, modifier: null, untypedValue));
        var options = new SearchOptions { ResourceType = "Observation", Expression = expression };

        // Act
        var legacyResults = await CollectAsync(harness.LegacySearchService.SearchStreamAsync(options, CancellationToken.None));
        var newResults = await CollectAsync(harness.NewSearchService.SearchStreamAsync(options, CancellationToken.None));

        // Assert
        newResults.Select(r => r.ResourceId).ShouldBe([matchId]);
        legacyResults.Select(r => r.ResourceId).ShouldBe([matchId]);
    }
}
