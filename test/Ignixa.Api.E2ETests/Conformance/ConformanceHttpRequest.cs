using System.Text.Json.Serialization;

namespace Ignixa.Api.E2ETests.Conformance;

internal sealed record ConformanceHttpRequest(
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("headers")] IReadOnlyDictionary<string, string> Headers,
    [property: JsonPropertyName("body")] string? Body);
