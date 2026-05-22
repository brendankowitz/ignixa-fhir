using Ignixa.Serialization.SourceNodes;
using Ignixa.TestScript.Model;

namespace Ignixa.TestScript.Fixtures;

public sealed class CompositeFixtureProvider(IReadOnlyList<IFixtureProvider> providers) : IFixtureProvider
{
    public async ValueTask<ResourceJsonNode?> ResolveFixtureAsync(
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
