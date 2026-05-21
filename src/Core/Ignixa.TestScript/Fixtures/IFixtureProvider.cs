using System.Text.Json.Nodes;
using Ignixa.TestScript.Model;

namespace Ignixa.TestScript.Fixtures;

public interface IFixtureProvider
{
    ValueTask<JsonNode?> ResolveFixtureAsync(
        FixtureDefinition fixture,
        FixtureResolutionContext context,
        CancellationToken cancellationToken);
}
