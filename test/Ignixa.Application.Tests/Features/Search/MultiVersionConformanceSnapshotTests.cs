using Ignixa.Abstractions;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Tests.Features.Conformance;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Extensions;
using Shouldly;
using SearchParamType = Ignixa.Specification.ValueSets.Normative.SearchParamType;

namespace Ignixa.Application.Tests.Features.Search;

/// <summary>
/// The conformance projection is shared by every tenant; a package activated for one FHIR version must leave
/// the definitions, searchability and extraction of tenants on the other versions untouched.
/// </summary>
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
        using var state = await ConformanceEvents.InitializedStateAsync([.. R4ShadowEvents().Take(replayedEvents)]);
        using var context = TestDefinitions.CreateContext()
            .Publish(state, FhirVersion.R4, R4TenantId)
            .Publish(state, FhirVersion.R5, R5TenantId)
            .Publish(state, FhirVersion.Stu3, Stu3TenantId);

        foreach (var (version, tenantId) in new[] { (FhirVersion.R5, R5TenantId), (FhirVersion.Stu3, Stu3TenantId) })
        {
            var expected = context.GetSearchParameterDefinitionManager(version).GetSearchParameter("Observation", "patient");
            var actual = context.GetSearchParameterDefinitionManager(version, tenantId).GetSearchParameter("Observation", "patient");
            actual.Url.ShouldBe(expected.Url);
            actual.Expression.ShouldBe(expected.Expression);
            actual.OverridesUrl.ShouldBeNull();
            actual.IsSearchable.ShouldBeTrue();
            context.GetSearchableSearchParameterDefinitionManager(version, tenantId)
                .TryGetSearchParameter("Observation", "patient", out _)
                .ShouldBeTrue($"{version} tenant must still search Observation.patient");

            var extracted = Extract(context, version, tenantId)
                .Where(entry => entry.SearchParameter.Code == "patient")
                .Select(entry => entry.SearchParameter)
                .ToList();
            extracted.ShouldNotBeEmpty();
            extracted.ShouldAllBe(parameter => parameter.Url == expected.Url && parameter.OverridesUrl == null);
        }

        context.GetSearchableSearchParameterDefinitionManager(FhirVersion.R4, R4TenantId)
            .TryGetSearchParameter("Observation", "patient", out _)
            .ShouldBeFalse("the R4 tenant is mid-transition and must stay hidden");
    }

    [Fact]
    public async Task GivenR5OnlyPackage_WhenR4TenantResolves_ThenItsDefinitionsDoNotIncludeThePackage()
    {
        using var state = await ConformanceEvents.InitializedStateAsync(CustomActivation("custom-r5", "5.0"));
        using var context = TestDefinitions.CreateContext()
            .Publish(state, FhirVersion.R4, R4TenantId)
            .Publish(state, FhirVersion.R5, R5TenantId);

        var r4 = context.GetSearchParameterDefinitionManager(FhirVersion.R4, R4TenantId);
        var r5 = context.GetSearchParameterDefinitionManager(FhirVersion.R5, R5TenantId);

        r4.TryGetSearchParameter("Patient", "custom-r5", out _).ShouldBeFalse();
        r4.AllSearchParameters.ShouldNotContain(parameter => parameter.Code == "custom-r5");
        r4.TryGetSearchParameter(new Uri("http://example.org/SearchParameter/custom-r5"), out _).ShouldBeFalse();
        r5.TryGetSearchParameter("Patient", "custom-r5", out _).ShouldBeTrue();
        r5.AllSearchParameters.ShouldContain(parameter => parameter.Code == "custom-r5");
    }

    [Fact]
    public async Task GivenActivationRecordedWithoutAVersion_WhenAnyTenantResolves_ThenItAppliesToEveryVersion()
    {
        using var state = await ConformanceEvents.InitializedStateAsync(CustomActivation("custom-legacy", fhirVersion: null));
        using var context = TestDefinitions.CreateContext()
            .Publish(state, FhirVersion.R4, R4TenantId)
            .Publish(state, FhirVersion.R5, R5TenantId);

        context.GetSearchParameterDefinitionManager(FhirVersion.R4, R4TenantId)
            .TryGetSearchParameter("Patient", "custom-legacy", out _).ShouldBeTrue();
        context.GetSearchParameterDefinitionManager(FhirVersion.R5, R5TenantId)
            .TryGetSearchParameter("Patient", "custom-legacy", out _).ShouldBeTrue();
    }

    private static IReadOnlyCollection<Ignixa.Search.Indexing.SearchIndexEntry> Extract(
        FhirVersionContext context,
        FhirVersion version,
        int tenantId)
    {
        var handle = context.GetDefinitionsHandle(version, tenantId);
        var observation = ResourceJsonNode.Parse(
            """{ "resourceType": "Observation", "status": "final", "code": { "text": "weight" }, "subject": { "reference": "Patient/p1" } }""")
            .ToElement(handle.SchemaProvider);
        return handle.Indexer.Extract(observation);
    }

    // The events an R4 package activation appends when it shadows Observation.patient: the R4 base owner is
    // materialised, the override is staged under the shared identity, and the transition commits.
    private static object[] R4ShadowEvents() =>
    [
        ObservationPatient(BaseCanonical, ConformanceEvents.CorePackage, null, "Observation.subject.where(resolve() is Patient)"),
        ObservationPatient(OverrideCanonical, "r4.shadow@1.0.0", BaseCanonical, "Observation.subject"),
        ConformanceEvents.Committed(1, 2),
    ];

    private static object ObservationPatient(string canonical, string package, string? overrides, string expression) =>
        ConformanceEvents.Activated(
            canonical,
            "patient",
            1,
            overrides,
            package,
            "Observation",
            expression,
            SearchParamType.Reference,
            ["Patient"],
            "patient",
            "4.0");

    private static object CustomActivation(string code, string? fhirVersion) =>
        ConformanceEvents.Activated(
            $"http://example.org/SearchParameter/{code}",
            code,
            101,
            package: "custom@1.0.0",
            expression: "Patient.name",
            type: SearchParamType.String,
            fhirVersion: fhirVersion);
}
