using Ignixa.Abstractions;
using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Search;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Events;
using Ignixa.Conformance.Events.Models;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Definition;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.BackgroundOperations.Reindex;

public class ReindexTargetResolverTests
{
    [Fact]
    public void GivenPendingBaseParameter_WhenTargetsAreResolved_ThenConcreteTypesAreExpanded()
    {
        var pending = Parameter("http://example.org/SearchParameter/resource-custom", "Resource");

        var result = ReindexTargetResolver.Resolve(
            [pending],
            ["Patient", "Observation"]);

        result.Targets.Single().AffectedResourceTypes.ShouldBe(["Observation", "Patient"], ignoreOrder: false);
        result.ResourceTypes.ShouldBe(["Observation", "Patient"], ignoreOrder: false);
    }

    [Fact]
    public void GivenDomainResourceParameter_WhenTargetsAreResolved_ThenOnlyConcreteDescendantsAreExpanded()
    {
        var pending = Parameter(
            "http://example.org/SearchParameter/domain-custom",
            "DomainResource");

        var result = ReindexTargetResolver.Resolve(
            [pending],
            ["Binary", "Observation", "Patient"],
            new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["DomainResource"] = ["Observation", "Patient"]
            });

        result.Targets.Single().AffectedResourceTypes.ShouldBe(["Observation", "Patient"]);
        result.ResourceTypes.ShouldBe(["Observation", "Patient"]);
    }

    [Fact]
    public void GivenReferenceParameter_WhenTargetsAreResolved_ThenReferenceTargetsDoNotChangeReindexScope()
    {
        var pending = Parameter(
            "http://example.org/SearchParameter/patient-practitioner",
            "Patient",
            ["Practitioner"]);

        var result = ReindexTargetResolver.Resolve(
            [pending],
            ["Patient", "Practitioner"]);

        result.Targets.Single().AffectedResourceTypes.ShouldBe(["Patient"]);
        result.ResourceTypes.ShouldBe(["Patient"]);
    }

    [Fact]
    public void GivenNoPendingParametersAndNoMaintenanceTypes_WhenTargetsAreResolved_ThenNothingToDoIsReturned()
    {
        var result = ReindexTargetResolver.Resolve([], ["Patient"]);

        result.HasWork.ShouldBeFalse();
    }

    [Fact]
    public void GivenVersionedParameter_WhenTargetsAreResolved_ThenTheTargetCarriesTheVersion()
    {
        var pending = Parameter("http://example.org/SearchParameter/r5-custom", "Patient", fhirVersion: "5.0");

        var result = ReindexTargetResolver.Resolve([pending], ["Patient"]);

        result.Targets.Single().FhirVersion.ShouldBe("5.0");
        result.Targets.Single().AppliesToTenant("5.0.0").ShouldBeTrue();
        result.Targets.Single().AppliesToTenant("4.0").ShouldBeFalse();
    }

    [Theory]
    [InlineData("4.0", new[] { 1 })]
    [InlineData("5.0", new[] { 2 })]
    [InlineData(null, new[] { 1, 2 })]
    public async Task GivenTenantsOnTwoVersions_WhenAPendingParameterIsResolved_ThenOnlyItsVersionsTenantsAreTargeted(
        string? fhirVersion,
        int[] expectedTenantIds)
    {
        var resolver = CreateResolver(
            [Parameter("http://example.org/SearchParameter/custom", "Resource", fhirVersion: fhirVersion)],
            Tenant(1, "4.0"),
            Tenant(2, "5.0"));

        var plan = await resolver.ResolveAsync(CancellationToken.None);

        plan.TenantIds.ShouldBe(expectedTenantIds);
        var target = plan.Resolution.Targets.ShouldHaveSingleItem();
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
                Parameter("http://example.org/SearchParameter/r4-custom", "DomainResource", fhirVersion: "4.0"),
                Parameter("http://example.org/SearchParameter/r5-custom", "ActorDefinition", fhirVersion: "5.0")
            ],
            Tenant(1, "4.0"),
            Tenant(2, "5.0"));

        var plan = await resolver.ResolveAsync(CancellationToken.None);

        plan.TenantIds.ShouldBe([1, 2]);
        plan.Resolution.Targets.Count.ShouldBe(2);
        plan.Resolution.Targets.Single(target => target.FhirVersion == "4.0")
            .AffectedResourceTypes.ShouldNotContain("ActorDefinition");
        plan.Resolution.Targets.Single(target => target.FhirVersion == "5.0")
            .AffectedResourceTypes.ShouldBe(["ActorDefinition"]);
        plan.Resolution.ResourceTypes.ShouldContain("ActorDefinition");
        plan.Resolution.ResourceTypes.ShouldContain("Patient");
    }

    private static ReindexTargetResolver CreateResolver(
        IEnumerable<ActiveSearchParameter> parameters,
        params TenantConfiguration[] tenants)
    {
        var state = new ConformanceState();
        foreach (var (parameter, index) in parameters.Select((parameter, index) => (parameter, index)))
        {
            state.ApplyAndTrack(new SourceEvent(
                index + 1,
                "package:custom@1.0.0",
                nameof(SearchParameterActivated),
                new SearchParameterActivated(
                    parameter.Canonical,
                    parameter.Code,
                    parameter.ResourceType,
                    parameter.Expression,
                    parameter.ParamType,
                    parameter.SourcePackage,
                    null,
                    parameter.SearchParamId + index,
                    parameter.TargetResourceTypes,
                    null,
                    null,
                    null,
                    parameter.FhirVersion),
                DateTimeOffset.UtcNow));
        }

        var tenantStore = Substitute.For<ITenantConfigurationStore>();
        tenantStore.GetAllTenantsAsync(Arg.Any<CancellationToken>()).Returns(tenants);
        return new ReindexTargetResolver(
            tenantStore,
            new FhirVersionContext(
                NullLoggerFactory.Instance,
                new SearchParameterResolutionOptions(),
                NullFhirBaseUriProvider.Instance),
            state,
            TestConformanceRefresher.EmptyEventStore());
    }

    private static TenantConfiguration Tenant(int tenantId, string fhirVersion) =>
        new()
        {
            TenantId = tenantId,
            DisplayName = $"Tenant {tenantId}",
            FhirVersion = fhirVersion,
            IsActive = true
        };

    private static ActiveSearchParameter Parameter(
        string canonical,
        string resourceType,
        IReadOnlyList<string>? targetResourceTypes = null,
        string? fhirVersion = null) =>
        new()
        {
            SearchParamId = 17,
            Canonical = canonical,
            Code = "custom",
            ResourceType = resourceType,
            Expression = "Resource.id",
            ParamType = Ignixa.Specification.ValueSets.Normative.SearchParamType.String,
            SourcePackage = "example@1.0.0",
            FhirVersion = fhirVersion,
            TargetResourceTypes = targetResourceTypes,
            ActivationEventId = 42,
            Status = SearchParameterStatus.Pending
        };
}
