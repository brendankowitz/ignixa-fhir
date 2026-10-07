using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Application.Features.Conformance;
using Ignixa.Conformance.Events.Models;
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
            ["Patient", "Observation"],
            targetResourceTypes: null);

        result.Targets.Single().AffectedResourceTypes.ShouldBe(["Observation", "Patient"], ignoreOrder: false);
        result.ResourceTypes.ShouldBe(["Observation", "Patient"], ignoreOrder: false);
    }

    [Fact]
    public void GivenTargetResourceTypes_WhenTargetsAreResolved_ThenScopeIsNarrowed()
    {
        var pending = Parameter(
            "http://example.org/SearchParameter/resource-custom",
            "Resource",
            ["Patient", "Observation"]);

        var result = ReindexTargetResolver.Resolve(
            [pending],
            ["Patient", "Observation", "Encounter"],
            ["Patient"]);

        result.Targets.Single().AffectedResourceTypes.ShouldBe(["Encounter", "Observation", "Patient"]);
        result.Targets.Single().ScheduledResourceTypes.ShouldBe(["Patient"]);
        result.Targets.Single().IsFullyCovered.ShouldBeFalse();
        result.ResourceTypes.ShouldBe(["Patient"]);
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
            targetResourceTypes: null,
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
            ["Patient", "Practitioner"],
            targetResourceTypes: null);

        result.Targets.Single().AffectedResourceTypes.ShouldBe(["Patient"]);
        result.ResourceTypes.ShouldBe(["Patient"]);
    }

    [Fact]
    public void GivenNoPendingParametersAndNoMaintenanceTypes_WhenTargetsAreResolved_ThenNothingToDoIsReturned()
    {
        var result = ReindexTargetResolver.Resolve([], ["Patient"], targetResourceTypes: null);

        result.HasWork.ShouldBeFalse();
    }

    private static ActiveSearchParameter Parameter(
        string canonical,
        string resourceType,
        IReadOnlyList<string>? targetResourceTypes = null) =>
        new()
        {
            SearchParamId = 17,
            Canonical = canonical,
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
