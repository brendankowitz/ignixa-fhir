using System.Text.Json.Serialization;

namespace Ignixa.Api.E2ETests.Conformance;

internal sealed record ConformanceError(
    [property: JsonPropertyName("assertion")] string Assertion,
    [property: JsonPropertyName("received")] string Received);
