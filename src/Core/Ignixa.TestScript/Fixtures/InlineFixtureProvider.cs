using Ignixa.Serialization.SourceNodes;
using Ignixa.TestScript.Model;

namespace Ignixa.TestScript.Fixtures;

public sealed class InlineFixtureProvider : IFixtureProvider
{
    public ValueTask<ResourceJsonNode?> ResolveFixtureAsync(
        FixtureDefinition fixture,
        FixtureResolutionContext context,
        CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(fixture.Resource);
    }
}
