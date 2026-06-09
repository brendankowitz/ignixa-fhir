using System.Text.Json.Serialization;

namespace Ignixa.ConformanceMatrix.Cli.Reporting;

internal sealed record RunMeta
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("startedAt")] public required DateTimeOffset StartedAt { get; init; }
    [JsonPropertyName("duration_ms")] public required long DurationMs { get; init; }
    [JsonPropertyName("commit")] public string Commit { get; init; } = "";
    [JsonPropertyName("commitMessage")] public string CommitMessage { get; init; } = "";
    [JsonPropertyName("branch")] public string Branch { get; init; } = "";
    [JsonPropertyName("suiteVersion")] public string SuiteVersion { get; init; } = "";
    [JsonPropertyName("repoUrl")] public string RepoUrl { get; init; } = "";
}
