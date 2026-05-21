using System.Text.Json.Nodes;

namespace Ignixa.TestScript.Client;

public sealed record FhirResponse
{
    public required int StatusCode { get; init; }
    public JsonNode? Body { get; init; }
    public IReadOnlyDictionary<string, string> Headers { get; init; } =
        new Dictionary<string, string>();
}
