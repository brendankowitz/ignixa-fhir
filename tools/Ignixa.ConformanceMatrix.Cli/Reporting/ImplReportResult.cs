using System.Text.Json.Serialization;

namespace Ignixa.ConformanceMatrix.Cli.Reporting;

internal sealed record ImplReportResult
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("file")] public required string File { get; init; }
    [JsonPropertyName("status")] public required string Status { get; init; }
    [JsonPropertyName("duration_ms")] public required long DurationMs { get; init; }
    [JsonPropertyName("error")] public CellError? Error { get; init; }
}
