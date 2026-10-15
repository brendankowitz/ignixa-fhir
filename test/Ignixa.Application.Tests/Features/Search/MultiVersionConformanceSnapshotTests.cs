using Ignixa.Abstractions;
using Ignixa.Application.Features.Search;
using Ignixa.Serialization.SourceNodes;
using Shouldly;
using static Ignixa.Application.Tests.Features.Conformance.ConformanceEvents;
using SearchParamType = Ignixa.Specification.ValueSets.Normative.SearchParamType;

namespace Ignixa.Application.Tests.Features.Search;

/// <summary>Review 5 N4: an activation for one FHIR version leaves other versions' tenants untouched; tenants resolve only published projections.</summary>
public class MultiVersionConformanceSnapshotTests
{
    private const string BaseCanonical = "http://hl7.org/fhir/SearchParameter/clinical-patient";
    private const string OverrideCanonical = "http://example.org/SearchParameter/r4-observation-patient";
    private const int R4TenantId = 1;
    private const int R5TenantId = 2;
    private const int Stu3TenantId = 3;

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task GivenR4PackageShadowsABaseCode_WhenOtherVersionTenantsResolve_ThenTheirBaseDefinitionIsUnchanged(
        int replayedEvents)
    {
        using var state = await InitializedStateAsync([.. R4ShadowEvents().Take(replayedEvents)]);
        using var context = TestDefinitions.CreateContext()
            .Publish(state, FhirVersion.R4, R4TenantId)
            .Publish(state, FhirVersion.R5, R5TenantId)
            .Publish(state, FhirVersion.Stu3, Stu3TenantId);

        foreach (var (version, tenantId) in new[] { (FhirVersion.R5, R5TenantId), (FhirVersion.Stu3, Stu3TenantId) })
        {
            var expected = context.GetSearchParameterDefinitionManager(version).GetSearchParameter("Observation", "patient");
            var actual = context.GetSearchParameterDefinitionManager(version, tenantId).GetSearchParameter("Observation", "patient");
            (actual.Url, actual.Expression, actual.OverridesUrl, actual.IsSearchable).ShouldBe((expected.Url, expected.Expression, null, true));
            context.GetSearchableSearchParameterDefinitionManager(version, tenantId)
                .TryGetSearchParameter("Observation", "patient", out _)
                .ShouldBeTrue($"{version} tenant must still search Observation.patient");

            var extracted = Extract(context, version, tenantId).Where(entry => entry.SearchParameter.Code == "patient").ToList();
            extracted.ShouldNotBeEmpty();
            extracted.ShouldAllBe(entry => entry.SearchParameter.Url == expected.Url && entry.SearchParameter.OverridesUrl == null);
        }

        context.GetSearchableSearchParameterDefinitionManager(FhirVersion.R4, R4TenantId)
            .TryGetSearchParameter("Observation", "patient", out _)
            .ShouldBeFalse("the R4 tenant is mid-transition and must stay hidden");
    }

    // Review 5 N4: an activation applies to its own FHIR version's tenants; one recorded before versions applies to all.
    [Theory]
    [InlineData("5.0", false)]
    [InlineData(null, true)]
    public async Task GivenACustomActivation_WhenR4AndR5TenantsResolve_ThenOnlyTenantsOfItsRecordedVersionSeeIt(
        string? fhirVersion,
        bool visibleToR4)
    {
        using var state = await InitializedStateAsync(Custom("custom", 101, fhirVersion));
        using var context = TestDefinitions.CreateContext()
            .Publish(state, FhirVersion.R4, R4TenantId)
            .Publish(state, FhirVersion.R5, R5TenantId);

        var r4 = context.GetSearchParameterDefinitionManager(FhirVersion.R4, R4TenantId);
        var r5 = context.GetSearchParameterDefinitionManager(FhirVersion.R5, R5TenantId);

        r4.TryGetSearchParameter("Patient", "custom", out _).ShouldBe(visibleToR4);
        r4.AllSearchParameters.Any(parameter => parameter.Code == "custom").ShouldBe(visibleToR4);
        r4.TryGetSearchParameter(new Uri("http://example.org/SearchParameter/custom"), out _).ShouldBe(visibleToR4);
        r5.TryGetSearchParameter("Patient", "custom", out _).ShouldBeTrue();
        r5.AllSearchParameters.ShouldContain(parameter => parameter.Code == "custom");
    }

    [Fact]
    public async Task GivenLiveStateAdvancedPastThePublishedProjection_WhenAnUnpublishedTenantResolves_ThenItUsesThePublishedProjection()
    {
        using var state = await InitializedStateAsync(Custom("published-code", 101));
        using var context = TestDefinitions.CreateContext().Publish(state);
        state.ApplyAndTrack(At(2, Custom("unpublished-code", 102)));

        var definitions = context.GetSearchParameterDefinitionManager(FhirVersion.R4, 2);

        definitions.TryGetSearchParameter("Patient", "published-code", out _).ShouldBeTrue();
        definitions.TryGetSearchParameter("Patient", "unpublished-code", out _).ShouldBeFalse();
        context.GetDefinitionsHandle(FhirVersion.R4, 2).DefinitionsEventId.ShouldBe(1);
    }

    private static IReadOnlyCollection<Ignixa.Search.Indexing.SearchIndexEntry> Extract(FhirVersionContext context, FhirVersion version, int tenantId)
    {
        var handle = context.GetDefinitionsHandle(version, tenantId);
        var observation = ResourceJsonNode.Parse(
            """{ "resourceType": "Observation", "status": "final", "code": { "text": "weight" }, "subject": { "reference": "Patient/p1" } }""")
            .ToElement(handle.SchemaProvider);
        return handle.Indexer.Extract(observation);
    }

    // An R4 shadow of Observation.patient: the materialised base, the override under its identity, and the commit.
    private static object[] R4ShadowEvents() =>
    [
        ObservationPatient(BaseCanonical, CorePackage, null, "Observation.subject.where(resolve() is Patient)"),
        ObservationPatient(OverrideCanonical, "r4.shadow@1.0.0", BaseCanonical, "Observation.subject"),
        Committed(1, 2),
    ];

    private static object ObservationPatient(string canonical, string package, string? overrides, string expression) =>
        Activated(canonical, "patient", 1, overrides, package, "Observation", expression, SearchParamType.Reference, ["Patient"], "patient", "4.0");

    private static object Custom(string code, int searchParamId, string? fhirVersion = null) =>
        Activated(
            $"http://example.org/SearchParameter/{code}", code, searchParamId, package: "custom@1.0.0", expression: "Patient.name", type: SearchParamType.String, fhirVersion: fhirVersion);
}
