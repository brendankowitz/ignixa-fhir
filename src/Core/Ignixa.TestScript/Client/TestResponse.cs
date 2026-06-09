using System.Collections.Immutable;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.TestScript.Client;

public sealed record TestResponse
{
    public required int StatusCode { get; init; }
    public ResourceJsonNode? Body { get; init; }
    public string? BodyParseError { get; init; }
    public ImmutableDictionary<string, string> Headers { get; init; } =
        ImmutableDictionary<string, string>.Empty;
}
