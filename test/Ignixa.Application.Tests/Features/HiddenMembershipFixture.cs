using Ignixa.Application.Features.Search;
using Ignixa.Application.Tests.Features.Search;
using static Ignixa.Application.Tests.Features.Conformance.ConformanceEvents;
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

    public static async Task<IFhirVersionContext> CreateVersionContextAsync(HiddenMembershipScenario scenario)
    {
        object[] events =
        [
            Subject(BaseCanonical, CorePackage, overrides: null),
            Subject("http://example.org/SearchParameter/observation-subject", "example.package@1.0.0", BaseCanonical),
        ];
        using var state = await InitializedStateAsync(
            scenario == HiddenMembershipScenario.Pending ? [.. events, Committed(1, 2)] : events);
        return TestDefinitions.CreateContext().Publish(state);
    }

    private static object Subject(string canonical, string package, string? overrides) =>
        Activated(
            canonical,
            "subject",
            1,
            overrides,
            package,
            "Observation",
            "Observation.subject",
            SearchParamType.Reference,
            ["Patient", "Group", "Device", "Location"],
            "subject",
            "4.0");
}
