using System.Text.Json.Serialization;

namespace Ignixa.Api.E2ETests.Conformance;

internal sealed record ConformanceReport(
    [property: JsonPropertyName("impl")] string Impl,
    [property: JsonPropertyName("target")] string Target,
    [property: JsonPropertyName("fhirVersion")] string FhirVersion,
    [property: JsonPropertyName("startedAt")] DateTimeOffset StartedAt,
    [property: JsonPropertyName("duration_ms")] long DurationMs,
    [property: JsonPropertyName("results")] IReadOnlyList<ConformanceResult> Results);
