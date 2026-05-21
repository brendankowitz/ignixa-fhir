using System.Text.Json.Nodes;
using Ignixa.TestScript.Model;

namespace Ignixa.TestScript.Fixtures;

public sealed class CompositeFixtureProvider(IReadOnlyList<IFixtureProvider> providers) : IFixtureProvider
{
    public async ValueTask<JsonNode?> ResolveFixtureAsync(
        FixtureDefinition fixture,
        FixtureResolutionContext context,
        CancellationToken cancellationToken)
    {
        foreach (var provider in providers)
        {
            var result = await provider.ResolveFixtureAsync(fixture, context, cancellationToken);
            if (result is not null)
                return result;
        }
        return null;
    }
}
