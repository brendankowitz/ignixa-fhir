using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Search;
using Ignixa.Search.Definition;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using static Ignixa.Application.Tests.Features.Conformance.ConformanceEvents;
using SearchParamType = Ignixa.Specification.ValueSets.Normative.SearchParamType;

namespace Ignixa.Application.Tests.Features.Search;

/// <summary>Spec §4.4 (B-L2, M-L3): listing all parameters keeps one entry per storage identity and base type, whether base or override.</summary>
public class CompositeSearchParameterDefinitionManagerTests
{
    private const string ClinicalPatient = "http://hl7.org/fhir/SearchParameter/clinical-patient";
    private const string ObservationPatient = "http://example.org/SearchParameter/Observation-patient";
    private const string SharedTag = "http://example.org/SearchParameter/shared-tag";
    private const string PatientIdentifier = "http://hl7.org/fhir/SearchParameter/Patient-identifier";
    private const string OverrideIdentifier = "http://example.org/SearchParameter/Patient-identifier";

    // An override of one base type of a multi-base parameter leaves the base for the others, a parameter activated
    // for two base types keeps both, and an override of a single-base parameter is the only entry for its identity.
    [Fact]
    public async Task GivenOverridesAndAMultiBaseActivation_WhenAllParametersAreListed_ThenEachIdentityAndBaseTypeIsListedOnce()
    {
        using var state = await InitializedStateAsync(
            Activated(ObservationPatient, "patient", 7, ClinicalPatient, "custom@1.0.0", "Observation", "Observation.subject", SearchParamType.Reference, ["Patient"]),
            Activated(SharedTag, "shared-tag", 11, package: "custom@1.0.0", expression: "Patient.subject"),
            Activated(SharedTag, "shared-tag", 11, package: "custom@1.0.0", resourceType: "Practitioner", expression: "Practitioner.subject"),
            Activated(OverrideIdentifier, "identifier", 5, PatientIdentifier, "custom@1.0.0", expression: "Patient.subject"));
        var manager = CreateManager(state);

        var all = manager.AllSearchParameters.ToList();

        all.ShouldContain(parameter => parameter.Url == new Uri(ClinicalPatient) && parameter.BaseResourceTypes.Contains("AllergyIntolerance"));
        new SearchableSearchParameterDefinitionManager(manager).AllSearchParameters
            .ShouldContain(parameter => parameter.Code == "patient" && parameter.BaseResourceTypes.Contains("AllergyIntolerance"));
        all.ShouldContain(parameter => parameter.Url == new Uri(ObservationPatient));
        all.Where(parameter => parameter.Url == new Uri(SharedTag)).SelectMany(parameter => parameter.BaseResourceTypes)
            .ShouldBe(["Patient", "Practitioner"], ignoreOrder: true);
        all.Where(parameter => (parameter.OverridesUrl ?? parameter.Url) == new Uri(PatientIdentifier)).Select(parameter => parameter.Url)
            .ShouldBe([new Uri(OverrideIdentifier)]);
    }

    private static CompositeSearchParameterDefinitionManager CreateManager(ConformanceState state)
    {
        var manager = new CompositeSearchParameterDefinitionManager(
            TestDefinitions.BaseManager(), state.CreateSnapshot(), "4.0", NullLogger<CompositeSearchParameterDefinitionManager>.Instance, new SearchParameterResolutionOptions());
        manager.ReloadFromConformanceState();
        return manager;
    }
}
