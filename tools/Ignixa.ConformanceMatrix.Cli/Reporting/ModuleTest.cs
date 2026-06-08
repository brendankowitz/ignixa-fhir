using System.Text.Json.Serialization;

namespace Ignixa.ConformanceMatrix.Cli.Reporting;

internal sealed record ModuleTest
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("title")] public required string Title { get; init; }
    [JsonPropertyName("fullName")] public required string FullName { get; init; }
    [JsonPropertyName("file")] public required string File { get; init; }
}
