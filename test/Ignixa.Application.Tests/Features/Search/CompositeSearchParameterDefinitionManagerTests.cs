using Ignixa.Abstractions;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Search;
using Ignixa.Search.Definition;
using Ignixa.Specification.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using static Ignixa.Application.Tests.Features.Conformance.ConformanceEvents;
using SearchParamType = Ignixa.Specification.ValueSets.Normative.SearchParamType;

namespace Ignixa.Application.Tests.Features.Search;

public class CompositeSearchParameterDefinitionManagerTests
{
    private const string ClinicalPatient = "http://hl7.org/fhir/SearchParameter/clinical-patient";

    [Fact]
    public async Task GivenOverrideOfAMultiBaseParameterForOneType_WhenAllParametersAreListed_ThenTheBaseRemainsForUnshadowedTypes()
    {
        using var state = await InitializedStateAsync(
            Subject("http://example.org/SearchParameter/Observation-patient", "patient", "Observation", 7, ClinicalPatient));
        var manager = CreateManager(state);

        var all = manager.AllSearchParameters.ToList();
        var searchable = new SearchableSearchParameterDefinitionManager(manager).AllSearchParameters.ToList();

        all.ShouldContain(parameter =>
            parameter.Url == new Uri(ClinicalPatient) &&
            parameter.BaseResourceTypes.Contains("AllergyIntolerance"));
        searchable.ShouldContain(parameter =>
            parameter.Code == "patient" &&
            parameter.BaseResourceTypes.Contains("AllergyIntolerance"));
        all.ShouldContain(parameter =>
            parameter.Url == new Uri("http://example.org/SearchParameter/Observation-patient"));
    }

    [Fact]
    public async Task GivenPackageParameterActivatedForTwoBaseTypes_WhenAllParametersAreListed_ThenEachBaseTypeKeepsItsEntry()
    {
        const string canonical = "http://example.org/SearchParameter/shared-tag";
        using var state = await InitializedStateAsync(
            Activated(canonical, "shared-tag", 11, package: "custom@1.0.0", expression: "Patient.subject"),
            Activated(canonical, "shared-tag", 11, package: "custom@1.0.0", resourceType: "Practitioner", expression: "Practitioner.subject"));
        var manager = CreateManager(state);

        var entries = manager.AllSearchParameters
            .Where(parameter => parameter.Url == new Uri(canonical))
            .SelectMany(parameter => parameter.BaseResourceTypes)
            .ToList();

        entries.ShouldBe(["Patient", "Practitioner"], ignoreOrder: true);
    }

    [Fact]
    public async Task GivenOverrideOfASingleBaseParameter_WhenAllParametersAreListed_ThenOnlyTheOverrideRepresentsThatIdentity()
    {
        const string patientIdentifier = "http://hl7.org/fhir/SearchParameter/Patient-identifier";
        using var state = await InitializedStateAsync(Activated(
            "http://example.org/SearchParameter/Patient-identifier",
            "identifier",
            5,
            patientIdentifier,
            "custom@1.0.0",
            expression: "Patient.subject"));
        var manager = CreateManager(state);

        manager.AllSearchParameters
            .Where(parameter => (parameter.OverridesUrl ?? parameter.Url) == new Uri(patientIdentifier))
            .Select(parameter => parameter.Url)
            .ShouldBe([new Uri("http://example.org/SearchParameter/Patient-identifier")]);
    }

    private static CompositeSearchParameterDefinitionManager CreateManager(ConformanceState state)
    {
        var baseManager = new SearchParameterDefinitionManager(
            FhirVersion.R4.GetSchemaProvider(),
            NullLogger<SearchParameterDefinitionManager>.Instance);
        var manager = new CompositeSearchParameterDefinitionManager(
            baseManager,
            state.CreateSnapshot(),
            "4.0",
            NullLogger<CompositeSearchParameterDefinitionManager>.Instance,
            new SearchParameterResolutionOptions());
        manager.ReloadFromConformanceState();
        return manager;
    }

    private static object Subject(string canonical, string code, string resourceType, int searchParamId, string overrides) =>
        Activated(
            canonical,
            code,
            searchParamId,
            overrides,
            "custom@1.0.0",
            resourceType,
            $"{resourceType}.subject",
            SearchParamType.Reference,
            ["Patient"]);
}
