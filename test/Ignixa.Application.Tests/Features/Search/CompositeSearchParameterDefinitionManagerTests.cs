using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Search;
using Ignixa.Search.Definition;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using static Ignixa.Application.Tests.Features.Conformance.ConformanceEvents;
using SearchParamType = Ignixa.Specification.ValueSets.Normative.SearchParamType;

namespace Ignixa.Application.Tests.Features.Search;

/// <summary>Listing all parameters keeps one entry per storage identity and base type, whether base or override.</summary>
public class CompositeSearchParameterDefinitionManagerTests
{
    private const string ClinicalPatient = "http://hl7.org/fhir/SearchParameter/clinical-patient";
    private const string ObservationPatient = "http://example.org/SearchParameter/Observation-patient";

    [Fact]
    public async Task GivenOverrideOfAMultiBaseParameterForOneType_WhenAllParametersAreListed_ThenTheBaseRemainsForUnshadowedTypes()
    {
        using var state = await InitializedStateAsync(Activated(
            ObservationPatient, "patient", 7, ClinicalPatient, "custom@1.0.0", "Observation", "Observation.subject", SearchParamType.Reference, ["Patient"]));
        var manager = CreateManager(state);

        var all = manager.AllSearchParameters.ToList();
        var searchable = new SearchableSearchParameterDefinitionManager(manager).AllSearchParameters.ToList();

        all.ShouldContain(parameter => parameter.Url == new Uri(ClinicalPatient) && parameter.BaseResourceTypes.Contains("AllergyIntolerance"));
        searchable.ShouldContain(parameter => parameter.Code == "patient" && parameter.BaseResourceTypes.Contains("AllergyIntolerance"));
        all.ShouldContain(parameter => parameter.Url == new Uri(ObservationPatient));
    }

    [Fact]
    public async Task GivenPackageParameterActivatedForTwoBaseTypes_WhenAllParametersAreListed_ThenEachBaseTypeKeepsItsEntry()
    {
        const string canonical = "http://example.org/SearchParameter/shared-tag";
        using var state = await InitializedStateAsync(
            Activated(canonical, "shared-tag", 11, package: "custom@1.0.0", expression: "Patient.subject"),
            Activated(canonical, "shared-tag", 11, package: "custom@1.0.0", resourceType: "Practitioner", expression: "Practitioner.subject"));

        CreateManager(state).AllSearchParameters
            .Where(parameter => parameter.Url == new Uri(canonical))
            .SelectMany(parameter => parameter.BaseResourceTypes)
            .ShouldBe(["Patient", "Practitioner"], ignoreOrder: true);
    }

    [Fact]
    public async Task GivenOverrideOfASingleBaseParameter_WhenAllParametersAreListed_ThenOnlyTheOverrideRepresentsThatIdentity()
    {
        const string patientIdentifier = "http://hl7.org/fhir/SearchParameter/Patient-identifier";
        const string overrideCanonical = "http://example.org/SearchParameter/Patient-identifier";
        using var state = await InitializedStateAsync(Activated(overrideCanonical, "identifier", 5, patientIdentifier, "custom@1.0.0", expression: "Patient.subject"));

        CreateManager(state).AllSearchParameters
            .Where(parameter => (parameter.OverridesUrl ?? parameter.Url) == new Uri(patientIdentifier))
            .Select(parameter => parameter.Url)
            .ShouldBe([new Uri(overrideCanonical)]);
    }

    private static CompositeSearchParameterDefinitionManager CreateManager(ConformanceState state)
    {
        var manager = new CompositeSearchParameterDefinitionManager(
            TestDefinitions.BaseManager(), state.CreateSnapshot(), "4.0", NullLogger<CompositeSearchParameterDefinitionManager>.Instance, new SearchParameterResolutionOptions());
        manager.ReloadFromConformanceState();
        return manager;
    }
}
