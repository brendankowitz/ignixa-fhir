using Ignixa.Abstractions;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Search;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Events;
using Ignixa.Conformance.Events.Models;
using Ignixa.Search.Definition;
using Ignixa.Specification.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using SearchParamType = Ignixa.Specification.ValueSets.Normative.SearchParamType;

namespace Ignixa.Application.Tests.Features.Search;

public class CompositeSearchParameterDefinitionManagerTests
{
    private const string ClinicalPatient = "http://hl7.org/fhir/SearchParameter/clinical-patient";

    [Fact]
    public async Task GivenOverrideOfAMultiBaseParameterForOneType_WhenAllParametersAreListed_ThenTheBaseRemainsForUnshadowedTypes()
    {
        using var state = await CreateInitializedStateAsync();
        state.ApplyAndTrack(Activation(
            1,
            "http://example.org/SearchParameter/Observation-patient",
            "patient",
            "Observation",
            SearchParamType.Reference,
            new OverrideInfo(ClinicalPatient, 7),
            searchParamId: 7));
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
        using var state = await CreateInitializedStateAsync();
        state.ApplyAndTrack(Activation(1, canonical, "shared-tag", "Patient", SearchParamType.Token, null, 11));
        state.ApplyAndTrack(Activation(2, canonical, "shared-tag", "Practitioner", SearchParamType.Token, null, 11));
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
        using var state = await CreateInitializedStateAsync();
        state.ApplyAndTrack(Activation(
            1,
            "http://example.org/SearchParameter/Patient-identifier",
            "identifier",
            "Patient",
            SearchParamType.Token,
            new OverrideInfo(patientIdentifier, 5),
            searchParamId: 5));
        var manager = CreateManager(state);

        manager.AllSearchParameters
            .Where(parameter => (parameter.OverridesUrl ?? parameter.Url) == new Uri(patientIdentifier))
            .Select(parameter => parameter.Url)
            .ShouldBe([new Uri("http://example.org/SearchParameter/Patient-identifier")]);
    }

    private static async Task<ConformanceState> CreateInitializedStateAsync()
    {
        var state = new ConformanceState();
        await state.InitializeFromEventsAsync(TestConformanceRefresher.EmptyEventStore(), CancellationToken.None);
        return state;
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

    private static SourceEvent Activation(
        long eventId,
        string canonical,
        string code,
        string resourceType,
        SearchParamType type,
        OverrideInfo? overrides,
        int searchParamId) =>
        new(
            eventId,
            "package:custom@1.0.0",
            nameof(SearchParameterActivated),
            new SearchParameterActivated(
                canonical,
                code,
                resourceType,
                $"{resourceType}.subject",
                type,
                "custom@1.0.0",
                overrides,
                searchParamId,
                type == SearchParamType.Reference ? ["Patient"] : null,
                null,
                null,
                null),
            DateTimeOffset.UtcNow);
}
