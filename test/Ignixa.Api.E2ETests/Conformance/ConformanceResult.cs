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

    /// <summary>
    /// True only for errors originating in the harness itself — a suite that fails to parse or
    /// an evaluator that throws. These mean the conformance infrastructure is broken and must
    /// fail CI. A behavioral "error" outcome (a server response that cascades a dependent step,
    /// e.g. a 404 export kick-off breaking its waitFor poll) is not infrastructure: it is a real
    /// finding published to the matrix, so it stays false.
    /// </summary>
    [JsonIgnore]
    public bool IsInfrastructureError { get; init; }

    public static ConformanceResult CreateError(
        string file,
        string suite,
        string category,
        string assertion,
        string received) =>
        new(file, file, suite, category, "error", 0, new ConformanceError(assertion, received))
        {
            IsInfrastructureError = true
        };
}
