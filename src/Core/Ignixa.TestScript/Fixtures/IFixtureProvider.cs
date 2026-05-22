using Ignixa.Serialization.SourceNodes;
using Ignixa.TestScript.Model;

namespace Ignixa.TestScript.Fixtures;

public interface IFixtureProvider
{
    ValueTask<ResourceJsonNode?> ResolveFixtureAsync(
        FixtureDefinition fixture,
        FixtureResolutionContext context,
        CancellationToken cancellationToken);
}
