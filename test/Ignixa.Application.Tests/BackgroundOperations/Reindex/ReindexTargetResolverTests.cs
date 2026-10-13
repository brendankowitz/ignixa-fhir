using Ignixa.Abstractions;
using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Search;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Models;
using Ignixa.Search.Definition;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using static Ignixa.Application.Tests.BackgroundOperations.Reindex.ReindexTestHelper;

namespace Ignixa.Application.Tests.BackgroundOperations.Reindex;

public class ReindexTargetResolverTests
{
    [Theory]
    [InlineData("Resource", null, "Binary,Observation,Patient,Practitioner")]
    [InlineData("DomainResource", null, "Observation,Patient")]
    [InlineData("Patient", "Practitioner", "Patient")]
    public void GivenAPendingParameter_WhenTargetsAreResolved_ThenItsConcreteBaseTypesAreReindexedInOrderButNotItsReferenceTargets(
        string baseType,
        string? referenceTarget,
        string expectedTypes)
    {
        var pending = Parameter(baseType, referenceTarget is null ? null : [referenceTarget]);

        var result = ReindexTargetResolver.Resolve(
            [pending],
            ["Patient", "Practitioner", "Observation", "Binary"],
            new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["DomainResource"] = ["Observation", "Patient"]
            });

        string.Join(",", result.Single().AffectedResourceTypes).ShouldBe(expectedTypes);
    }

    [Theory]
    [InlineData("4.0", new[] { 1 })]
    [InlineData("5.0", new[] { 2 })]
    [InlineData(null, new[] { 1, 2 })]
    public async Task GivenTenantsOnTwoVersions_WhenAPendingParameterIsResolved_ThenOnlyItsVersionsTenantsAreTargeted(
        string? fhirVersion,
        int[] expectedTenantIds)
    {
        var resolver = CreateResolver([Activation(resourceType: "Resource", fhirVersion: fhirVersion)]);

        var plan = await resolver.ResolveAsync(CancellationToken.None);

        plan.TenantIds.ShouldBe(expectedTenantIds);
        var target = plan.Targets.ShouldHaveSingleItem();
        target.FhirVersion.ShouldBe(fhirVersion);
        // ActorDefinition exists only from R5: an R4 parameter must not reach it, an R5 one must.
        target.AffectedResourceTypes.Contains("ActorDefinition").ShouldBe(fhirVersion != "4.0");
        target.AffectedResourceTypes.ShouldContain("Patient");
    }

    [Fact]
    public async Task GivenPendingParametersOnTwoVersions_WhenResolved_ThenBothVersionsTenantsAreTargetedWithTheirOwnTypes()
    {
        var resolver = CreateResolver(
        [
            Activation(Canonical + "-r4", eventId: 1, resourceType: "DomainResource", fhirVersion: "4.0"),
            Activation(Canonical + "-r5", eventId: 2, resourceType: "ActorDefinition", searchParamId: 18, fhirVersion: "5.0")
        ]);

        var plan = await resolver.ResolveAsync(CancellationToken.None);

        plan.TenantIds.ShouldBe([1, 2]);
        plan.Targets.Count.ShouldBe(2);
        plan.Targets.Single(target => target.FhirVersion == "4.0")
            .AffectedResourceTypes.ShouldNotContain("ActorDefinition");
        plan.Targets.Single(target => target.FhirVersion == "5.0")
            .AffectedResourceTypes.ShouldBe(["ActorDefinition"]);
        plan.ResourceTypes.ShouldContain("ActorDefinition");
        plan.ResourceTypes.ShouldContain("Patient");
    }

    /// <summary>A resolver over the given activations, with tenant 1 on R4 and tenant 2 on R5.</summary>
    private static ReindexTargetResolver CreateResolver(IEnumerable<SourceEvent> activations)
    {
        var state = new ConformanceState();
        foreach (var activation in activations)
        {
            state.ApplyAndTrack(activation);
        }

        return new ReindexTargetResolver(
            TenantStore(Tenant(1, fhirVersion: "4.0"), Tenant(2, fhirVersion: "5.0")),
            new FhirVersionContext(
                NullLoggerFactory.Instance,
                new SearchParameterResolutionOptions(),
                NullFhirBaseUriProvider.Instance),
            state,
            TestConformanceRefresher.EmptyEventStore());
    }

    private static ActiveSearchParameter Parameter(string resourceType, IReadOnlyList<string>? targetResourceTypes) =>
        new()
        {
            SearchParamId = 17,
            Canonical = Canonical,
            Code = "custom",
            ResourceType = resourceType,
            Expression = "Resource.id",
            ParamType = Ignixa.Specification.ValueSets.Normative.SearchParamType.String,
            SourcePackage = "example@1.0.0",
            TargetResourceTypes = targetResourceTypes,
            ActivationEventId = 42,
            Status = SearchParameterStatus.Pending
        };
}
