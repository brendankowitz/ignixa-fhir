using Ignixa.Application.Features.Search;
using Ignixa.Application.Tests.Features.Search;
using Ignixa.Search.Models;
using Shouldly;
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

/// <summary>M-L6: a real <see cref="FhirVersionContext"/> whose tenant 1 snapshot hides the membership parameter Observation.subject.</summary>
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
        using var state = await InitializedStateAsync(scenario == HiddenMembershipScenario.Pending ? [.. events, Committed(1, 2)] : events);
        return TestDefinitions.CreateContext().Publish(state);
    }

    /// <summary>Asserts one incomplete-results warning naming Observation.subject and its reason, or none when no reason is expected.</summary>
    public static void ShouldWarnOfHiddenMembership(this SearchOptions? options, string? expectedReason)
    {
        (string, string, bool)[] expected = expectedReason is null ? [] : [("warning", "incomplete", true)];
        options.ShouldNotBeNull().BundleIssues
            .Select(issue => (issue.Severity, issue.Code, issue.Diagnostics.Contains("'Observation.subject'") && issue.Diagnostics.Contains(expectedReason ?? string.Empty)))
            .ShouldBe(expected);
    }

    private static object Subject(string canonical, string package, string? overrides) =>
        Activated(canonical, "subject", 1, overrides, package, "Observation", "Observation.subject", SearchParamType.Reference, ["Patient", "Group", "Device", "Location"], "subject", "4.0");
}
