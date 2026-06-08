using System.Text.Json.Serialization;

namespace Ignixa.ConformanceMatrix.Cli.Reporting;

internal sealed record Impl(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("label")] string Label);
