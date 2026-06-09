using System.Text.Json.Serialization;

namespace Ignixa.ConformanceMatrix.Cli.Reporting;

internal sealed record IndexEntry
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("startedAt")] public required DateTimeOffset StartedAt { get; init; }
    [JsonPropertyName("duration_ms")] public required long DurationMs { get; init; }
    [JsonPropertyName("commit")] public string Commit { get; init; } = "";
    [JsonPropertyName("commitMessage")] public string CommitMessage { get; init; } = "";
    [JsonPropertyName("branch")] public string Branch { get; init; } = "";
    [JsonPropertyName("impls")] public required IReadOnlyList<string> Impls { get; init; }
    [JsonPropertyName("pass")] public required int Pass { get; init; }
    [JsonPropertyName("fail")] public required int Fail { get; init; }
    [JsonPropertyName("skipped")] public required int Skipped { get; init; }
}
