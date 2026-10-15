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
    // §5.1: affected types are the targeted params' base types, expanded to concrete types.
    [Theory]
    [InlineData("Resource", null, "Binary,Observation,Patient,Practitioner")]
    [InlineData("Patient", "Practitioner", "Patient")]
    public void GivenAPendingParameter_WhenTargetsAreResolved_ThenItsConcreteBaseTypesAreReindexedInOrderButNotItsReferenceTargets(
        string baseType, string? referenceTarget, string expectedTypes)
    {
        var pending = Parameter(baseType, referenceTarget is null ? null : [referenceTarget]);

        var result = ReindexTargetResolver.Resolve(
            [pending],
            ["Patient", "Practitioner", "Observation", "Binary"],
            new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Resource"] = ["Patient", "Practitioner", "Observation", "Binary"],
                ["DomainResource"] = ["Observation", "Patient"]
            });

        string.Join(",", result.Single().AffectedResourceTypes).ShouldBe(expectedTypes);
    }

    // §4.4: a job targets a parameter only on tenants of its FHIR version (tenant 1 is R4, tenant 2 R5).
    [Theory]
    [InlineData("5.0", new[] { 2 })]
    [InlineData(null, new[] { 1, 2 })]
    public async Task GivenTenantsOnTwoVersions_WhenAPendingParameterIsResolved_ThenOnlyItsVersionsTenantsAreTargeted(
        string? fhirVersion, int[] expectedTenantIds)
    {
        var state = new ConformanceState();
        state.ApplyAndTrack(Activation(resourceType: "Resource", fhirVersion: fhirVersion));
        var versions = new FhirVersionContext(NullLoggerFactory.Instance, new SearchParameterResolutionOptions(), NullFhirBaseUriProvider.Instance);
        var resolver = new ReindexTargetResolver(
            TenantStore(Tenant(1, fhirVersion: "4.0"), Tenant(2, fhirVersion: "5.0")), versions, state, TestConformanceRefresher.EmptyEventStore());

        var plan = await resolver.ResolveAsync(CancellationToken.None);

        plan.TenantIds.ShouldBe(expectedTenantIds);
        var target = plan.Targets.ShouldHaveSingleItem();
        target.FhirVersion.ShouldBe(fhirVersion);
        // ActorDefinition exists only from R5: an R4 parameter must not reach it, an R5 one must.
        target.AffectedResourceTypes.Contains("ActorDefinition").ShouldBe(fhirVersion != "4.0");
        target.AffectedResourceTypes.ShouldContain("Patient");
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
