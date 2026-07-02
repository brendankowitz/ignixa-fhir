using System.Text.Json.Serialization;

namespace Ignixa.Api.E2ETests.Conformance;

internal sealed record ConformanceStep(
    [property: JsonPropertyName("phase")] string Phase,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("label")] string? Label,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("duration_ms")] long DurationMs,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("request")] ConformanceHttpRequest? Request,
    [property: JsonPropertyName("response")] ConformanceHttpResponse? Response);
