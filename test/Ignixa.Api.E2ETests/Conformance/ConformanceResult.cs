using System.Text.Json.Serialization;

namespace Ignixa.Api.E2ETests.Conformance;

internal sealed record ConformanceResult(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("file")] string File,
    [property: JsonPropertyName("suite")] string Suite,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("duration_ms")] long DurationMs,
    [property: JsonPropertyName("error")] ConformanceError? Error)
{
    [JsonPropertyName("steps")]
    public IReadOnlyList<ConformanceStep> Steps { get; init; } = [];

    public static ConformanceResult CreateError(
        string file,
        string suite,
        string category,
        string assertion,
        string received) =>
        new(file, file, suite, category, "error", 0, new ConformanceError(assertion, received));
}
