using System.Text.Json.Serialization;

namespace Ignixa.Api.E2ETests.Conformance;

internal sealed record ConformanceHttpResponse(
    [property: JsonPropertyName("statusCode")] int StatusCode,
    [property: JsonPropertyName("headers")] IReadOnlyDictionary<string, string> Headers,
    [property: JsonPropertyName("body")] string? Body,
    [property: JsonPropertyName("bodyParseError")] string? BodyParseError);
