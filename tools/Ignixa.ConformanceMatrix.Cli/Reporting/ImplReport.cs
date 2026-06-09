using System.Text.Json.Serialization;

namespace Ignixa.ConformanceMatrix.Cli.Reporting;

internal sealed record ImplReport
{
    [JsonPropertyName("impl")] public required string Impl { get; init; }
    [JsonPropertyName("startedAt")] public required DateTimeOffset StartedAt { get; init; }
    [JsonPropertyName("duration_ms")] public required long DurationMs { get; init; }
    [JsonPropertyName("results")] public required IReadOnlyList<ImplReportResult> Results { get; init; }
}
