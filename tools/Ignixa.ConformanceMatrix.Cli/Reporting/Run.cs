using System.Text.Json.Serialization;

namespace Ignixa.ConformanceMatrix.Cli.Reporting;

internal sealed record Run
{
    [JsonPropertyName("meta")] public required RunMeta Meta { get; init; }
    [JsonPropertyName("impls")] public required IReadOnlyList<Impl> Impls { get; init; }
    [JsonPropertyName("modules")] public required IReadOnlyList<Module> Modules { get; init; }
    [JsonPropertyName("statuses")] public required IReadOnlyDictionary<string, Dictionary<string, Dictionary<string, Cell>>> Statuses { get; init; }
}
