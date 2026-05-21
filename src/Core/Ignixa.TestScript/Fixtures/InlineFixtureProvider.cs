using System.Text.Json.Nodes;
using Ignixa.TestScript.Model;

namespace Ignixa.TestScript.Fixtures;

public sealed class InlineFixtureProvider : IFixtureProvider
{
    public ValueTask<JsonNode?> ResolveFixtureAsync(
        FixtureDefinition fixture,
        FixtureResolutionContext context,
        CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(fixture.Resource?.DeepClone());
    }
}
