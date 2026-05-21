using System.Text.Json.Nodes;

namespace Ignixa.TestScript.Client;

public sealed record FhirRequest
{
    public required HttpMethod Method { get; init; }
    public required string Url { get; init; }
    public JsonNode? Body { get; init; }
    public IReadOnlyDictionary<string, string> Headers { get; init; } =
        new Dictionary<string, string>();
}
