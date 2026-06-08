using System.Text.Json.Serialization;

namespace Ignixa.ConformanceMatrix.Cli.Reporting;

internal sealed record CellError
{
    [JsonPropertyName("assertion")] public required string Assertion { get; init; }
    [JsonPropertyName("expected")] public string Expected { get; init; } = "";
    [JsonPropertyName("received")] public string Received { get; init; } = "";
    [JsonPropertyName("stack")] public IReadOnlyList<string> Stack { get; init; } = [];
}
