using Ignixa.Abstractions;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Search;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Events;
using Ignixa.Conformance.Events.Models;
using Ignixa.Search.Definition;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
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
        using var state = new ConformanceState();
        await state.InitializeFromEventsAsync(TestConformanceRefresher.EmptyEventStore(), CancellationToken.None);
        foreach (var evt in R4ShadowEvents().Take(replayedEvents))
        {
            state.ApplyAndTrack(evt);
        }

        using var context = CreateContext();
        Publish(context, state, FhirVersion.R4, R4TenantId);
        Publish(context, state, FhirVersion.R5, R5TenantId);
        Publish(context, state, FhirVersion.Stu3, Stu3TenantId);

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
        using var state = new ConformanceState();
        await state.InitializeFromEventsAsync(TestConformanceRefresher.EmptyEventStore(), CancellationToken.None);
        state.ApplyAndTrack(CustomActivation(1, "custom-r5", "5.0"));
        using var context = CreateContext();
        Publish(context, state, FhirVersion.R4, R4TenantId);
        Publish(context, state, FhirVersion.R5, R5TenantId);

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
        using var state = new ConformanceState();
        await state.InitializeFromEventsAsync(TestConformanceRefresher.EmptyEventStore(), CancellationToken.None);
        state.ApplyAndTrack(CustomActivation(1, "custom-legacy", fhirVersion: null));
        using var context = CreateContext();
        Publish(context, state, FhirVersion.R4, R4TenantId);
        Publish(context, state, FhirVersion.R5, R5TenantId);

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
            """
            {
              "resourceType": "Observation",
              "status": "final",
              "code": { "text": "weight" },
              "subject": { "reference": "Patient/p1" }
            }
            """).ToElement(handle.SchemaProvider);
        return handle.Indexer.Extract(observation);
    }

    private static FhirVersionContext CreateContext() =>
        new(
            NullLoggerFactory.Instance,
            new SearchParameterResolutionOptions { EagerLoadPackageSearchParameters = true },
            NullFhirBaseUriProvider.Instance);

    private static void Publish(FhirVersionContext context, ConformanceState state, FhirVersion version, int tenantId) =>
        context.PublishConformanceDefinitionsSnapshot(
            version,
            tenantId,
            context.CreateConformanceDefinitionsSnapshot(version, tenantId, state.CreateSnapshot(), state.LastProcessedEventId));

    // The events an R4 package activation appends when it shadows Observation.patient: the R4 base owner is
    // materialised, the override is staged under the shared identity, and the transition commits.
    private static IEnumerable<SourceEvent> R4ShadowEvents()
    {
        yield return new SourceEvent(
            1,
            "package:r4.shadow@1.0.0",
            nameof(SearchParameterActivated),
            new SearchParameterActivated(
                BaseCanonical,
                "patient",
                "Observation",
                "Observation.subject.where(resolve() is Patient)",
                SearchParamType.Reference,
                "hl7.fhir.r4.core@4.0.1",
                null,
                1,
                ["Patient"],
                null,
                "patient",
                null,
                "4.0"),
            DateTimeOffset.UtcNow);
        yield return new SourceEvent(
            2,
            "package:r4.shadow@1.0.0",
            nameof(SearchParameterActivated),
            new SearchParameterActivated(
                OverrideCanonical,
                "patient",
                "Observation",
                "Observation.subject",
                SearchParamType.Reference,
                "r4.shadow@1.0.0",
                new OverrideInfo(BaseCanonical, 1),
                1,
                ["Patient"],
                null,
                "patient",
                null,
                "4.0"),
            DateTimeOffset.UtcNow);
        yield return new SourceEvent(
            3,
            "transition:2",
            nameof(SearchParameterTransitionCommitted),
            new SearchParameterTransitionCommitted(1, [2], [2]),
            DateTimeOffset.UtcNow);
    }

    private static SourceEvent CustomActivation(long eventId, string code, string? fhirVersion) =>
        new(
            eventId,
            "package:custom@1.0.0",
            nameof(SearchParameterActivated),
            new SearchParameterActivated(
                $"http://example.org/SearchParameter/{code}",
                code,
                "Patient",
                "Patient.name",
                SearchParamType.String,
                "custom@1.0.0",
                null,
                (int)eventId + 100,
                null,
                null,
                null,
                null,
                fhirVersion),
            DateTimeOffset.UtcNow);
}
