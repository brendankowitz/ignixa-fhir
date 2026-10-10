using Ignixa.Abstractions;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Search;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Events;
using Ignixa.Conformance.Events.Models;
using Ignixa.Search.Definition;
using Microsoft.Extensions.Logging.Abstractions;
using SearchParamType = Ignixa.Specification.ValueSets.Normative.SearchParamType;

namespace Ignixa.Application.Tests.Features;

public enum HiddenMembershipScenario
{
    /// <summary>The base Observation.subject owner is Disabling: a package override is staged against it.</summary>
    Disabling,

    /// <summary>The override of Observation.subject committed and is Pending reindex.</summary>
    Pending,
}

/// <summary>
/// Builds a real <see cref="FhirVersionContext"/> with an R4 snapshot published for tenant 1 in which the
/// Patient-compartment membership parameter Observation.subject is hidden from search.
/// </summary>
internal static class HiddenMembershipFixture
{
    private const string BaseCanonical = "http://hl7.org/fhir/SearchParameter/Observation-subject";
    private const string OverrideCanonical = "http://example.org/SearchParameter/observation-subject";

    public static async Task<IFhirVersionContext> CreateVersionContextAsync(HiddenMembershipScenario scenario)
    {
        using var state = new ConformanceState();
        await state.InitializeFromEventsAsync(TestConformanceRefresher.EmptyEventStore(), CancellationToken.None);
        foreach (var evt in Events(scenario))
        {
            state.ApplyAndTrack(evt);
        }

        var context = new FhirVersionContext(
            NullLoggerFactory.Instance,
            new SearchParameterResolutionOptions { EagerLoadPackageSearchParameters = true },
            NullFhirBaseUriProvider.Instance);
        context.PublishConformanceDefinitionsSnapshot(
            FhirVersion.R4,
            1,
            context.CreateConformanceDefinitionsSnapshot(FhirVersion.R4, 1, state.CreateSnapshot(), state.LastProcessedEventId));
        return context;
    }

    private static IEnumerable<SourceEvent> Events(HiddenMembershipScenario scenario)
    {
        yield return Activation(1, BaseCanonical, "hl7.fhir.r4.core@4.0.1", overrides: null);
        yield return Activation(2, OverrideCanonical, "example.package@1.0.0", new OverrideInfo(BaseCanonical, 1));
        if (scenario == HiddenMembershipScenario.Pending)
        {
            yield return new SourceEvent(
                3,
                "transition:2",
                nameof(SearchParameterTransitionCommitted),
                new SearchParameterTransitionCommitted(1, [2], [2]),
                DateTimeOffset.UtcNow);
        }
    }

    private static SourceEvent Activation(long eventId, string canonical, string sourcePackage, OverrideInfo? overrides) =>
        new(
            eventId,
            "package:example.package@1.0.0",
            nameof(SearchParameterActivated),
            new SearchParameterActivated(
                canonical,
                "subject",
                "Observation",
                "Observation.subject",
                SearchParamType.Reference,
                sourcePackage,
                overrides,
                1,
                ["Patient", "Group", "Device", "Location"],
                null,
                "subject",
                null,
                "4.0"),
            DateTimeOffset.UtcNow);
}
