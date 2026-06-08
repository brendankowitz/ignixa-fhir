using System.Text.Json.Serialization;

namespace Ignixa.ConformanceMatrix.Cli.Reporting;

internal sealed record Cell
{
    [JsonPropertyName("status")] public required string Status { get; init; }
    [JsonPropertyName("duration_ms")] public long? DurationMs { get; init; }
    [JsonPropertyName("error")] public CellError? Error { get; init; }
}
