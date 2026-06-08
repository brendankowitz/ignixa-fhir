using System.Text.Json.Serialization;

namespace Ignixa.ConformanceMatrix.Cli.Reporting;

internal sealed record RunMeta
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("startedAt")] public required string StartedAt { get; init; }
    [JsonPropertyName("duration_ms")] public required long DurationMs { get; init; }
    [JsonPropertyName("commit")] public string Commit { get; init; } = "";
    [JsonPropertyName("commitMessage")] public string CommitMessage { get; init; } = "";
    [JsonPropertyName("branch")] public string Branch { get; init; } = "";
    [JsonPropertyName("suiteVersion")] public string SuiteVersion { get; init; } = "";
    [JsonPropertyName("repoUrl")] public string RepoUrl { get; init; } = "";
}

internal sealed record Impl(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("label")] string Label);

internal sealed record ModuleTest
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("title")] public required string Title { get; init; }
    [JsonPropertyName("fullName")] public required string FullName { get; init; }
    [JsonPropertyName("file")] public required string File { get; init; }
}

internal sealed record Module
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("label")] public required string Label { get; init; }
    [JsonPropertyName("tests")] public required IReadOnlyList<ModuleTest> Tests { get; init; }
}

internal sealed record Cell
{
    [JsonPropertyName("status")] public required string Status { get; init; }
    [JsonPropertyName("duration_ms")] public long? DurationMs { get; init; }
    [JsonPropertyName("error")] public CellError? Error { get; init; }
}

internal sealed record Run
{
    [JsonPropertyName("meta")] public required RunMeta Meta { get; init; }
    [JsonPropertyName("impls")] public required IReadOnlyList<Impl> Impls { get; init; }
    [JsonPropertyName("modules")] public required IReadOnlyList<Module> Modules { get; init; }
    [JsonPropertyName("statuses")] public required IReadOnlyDictionary<string, Dictionary<string, Dictionary<string, Cell>>> Statuses { get; init; }
}

internal sealed record IndexEntry
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("startedAt")] public required string StartedAt { get; init; }
    [JsonPropertyName("duration_ms")] public required long DurationMs { get; init; }
    [JsonPropertyName("commit")] public string Commit { get; init; } = "";
    [JsonPropertyName("commitMessage")] public string CommitMessage { get; init; } = "";
    [JsonPropertyName("branch")] public string Branch { get; init; } = "";
    [JsonPropertyName("impls")] public required IReadOnlyList<string> Impls { get; init; }
    [JsonPropertyName("pass")] public required int Pass { get; init; }
    [JsonPropertyName("fail")] public required int Fail { get; init; }
    [JsonPropertyName("skipped")] public required int Skipped { get; init; }
}
