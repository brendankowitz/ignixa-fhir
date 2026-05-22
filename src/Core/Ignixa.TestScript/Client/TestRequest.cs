using System.Collections.Immutable;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.TestScript.Client;

public sealed record TestRequest
{
    public required HttpMethod Method { get; init; }
    public required string Url { get; init; }
    public ResourceJsonNode? Body { get; init; }
    public IReadOnlyDictionary<string, string> Headers { get; init; } =
        ImmutableDictionary<string, string>.Empty;
}
