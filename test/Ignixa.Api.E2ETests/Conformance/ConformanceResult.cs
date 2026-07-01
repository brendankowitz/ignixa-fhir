using System.Text.Json.Serialization;

namespace Ignixa.Api.E2ETests.Conformance;

internal sealed record ConformanceResult(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("file")] string File,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("duration_ms")] long DurationMs,
    [property: JsonPropertyName("error")] ConformanceError? Error)
{
    [JsonPropertyName("steps")]
    public IReadOnlyList<ConformanceStep> Steps { get; init; } = [];

    public static ConformanceResult CreateError(string id, string file, string assertion, string received) =>
        new(id, file, "error", 0, new ConformanceError(assertion, received));
}
