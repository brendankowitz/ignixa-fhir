using Ignixa.Application.Features.Conformance;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Events;
using Ignixa.Conformance.Events.Models;
using SearchParamType = Ignixa.Specification.ValueSets.Normative.SearchParamType;

namespace Ignixa.Application.Tests.Features.Conformance;

/// <summary>Conformance event payloads with test defaults; an override's <see cref="OverrideInfo"/> reuses its SearchParamId.</summary>
internal static class ConformanceEvents
{
    public const string CorePackage = "hl7.fhir.r4.core@4.0.1";
    public const string CustomPackage = "custom.package@1.0";

    public static SearchParameterActivated Activated(
        string canonical,
        string code,
        int searchParamId,
        string? overrides = null,
        string package = CustomPackage,
        string resourceType = "Patient",
        string? expression = null,
        SearchParamType type = SearchParamType.Token,
        IReadOnlyList<string>? targets = null,
        string? name = null,
        string? fhirVersion = null) =>
        new(
            canonical,
            code,
            resourceType,
            expression ?? $"{resourceType}.{code}",
            type,
            package,
            overrides is null ? null : new OverrideInfo(overrides, searchParamId),
            searchParamId,
            targets,
            null,
            name,
            null,
            fhirVersion);

    public static SearchParameterDeactivated Deactivated(string canonical, string code, string resourceType = "Patient") =>
        new(canonical, code, resourceType, "test");

    /// <summary>A transition commit for one hide event that both staged an activation and hid its owner.</summary>
    public static SearchParameterTransitionCommitted Committed(int searchParamId, long hideEventId) =>
        new(searchParamId, [hideEventId], [hideEventId]);

    public static SearchParameterReindexStarted ReindexStarted(string canonical, string code, long? activationEventId, string jobId) =>
        new(canonical, code, "Patient", jobId, ["Patient"], activationEventId);

    public static SearchParameterReindexCompleted ReindexCompleted(string canonical, string code, long? activationEventId, string jobId) =>
        new(canonical, code, "Patient", jobId, 1, TimeSpan.FromSeconds(1), activationEventId);

    public static PackageActivated PackageActivated(string packageId, string version = "1.0.0", params string[] canonicals) =>
        new(packageId, version, canonicals.Select(canonical => new ActivatedResource("Patient", canonical)).ToList());

    public static PackageDeactivated PackageDeactivated(string packageId, string version = "1.0") =>
        new(packageId, version, "test");

    public static SourceEvent At(long eventId, object data, DateTimeOffset? timestamp = null) =>
        new(eventId, "test", data.GetType().Name, data, timestamp ?? DateTimeOffset.UtcNow);

    /// <summary>A projection that has applied <paramref name="events"/> as ids 1..n.</summary>
    public static ConformanceState State(params object[] events)
    {
        var state = new ConformanceState();
        for (var index = 0; index < events.Length; index++)
        {
            state.ApplyAndTrack(At(index + 1, events[index]));
        }

        return state;
    }

    /// <summary>As <see cref="State"/>, then marked initialized as on startup replay.</summary>
    public static async Task<ConformanceState> InitializedStateAsync(params object[] events)
    {
        var state = State(events);
        await state.InitializeFromEventsAsync(TestConformanceRefresher.EmptyEventStore(), CancellationToken.None);
        return state;
    }
}
