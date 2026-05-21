using System.Text.Json.Nodes;
using Ignixa.TestScript.Fixtures;
using Ignixa.TestScript.Model;

namespace Ignixa.TestScript.FhirFakes;

public sealed class FhirFakesFixtureProvider : IFixtureProvider
{
    private const string FhirFakesExtensionUrl = "http://ignixa.io/testscript/fhirfakes";

    public ValueTask<JsonNode?> ResolveFixtureAsync(
        FixtureDefinition fixture,
        FixtureResolutionContext context,
        CancellationToken cancellationToken)
    {
        var resourceType = GetFhirFakesResourceType(fixture);
        if (resourceType is null)
            return ValueTask.FromResult<JsonNode?>(null);

        var faker = new Ignixa.FhirFakes.SchemaBasedFhirResourceFaker(context.Schema);
        var resource = faker.Generate(resourceType);

        return ValueTask.FromResult<JsonNode?>(resource.MutableNode.DeepClone());
    }

    private static string? GetFhirFakesResourceType(FixtureDefinition fixture)
    {
        if (fixture.Resource is not JsonObject obj) return null;

        var extensions = obj["extension"]?.AsArray();
        if (extensions is null) return null;

        foreach (var ext in extensions)
        {
            if (ext is not JsonObject extObj) continue;
            if (extObj["url"]?.GetValue<string>() == FhirFakesExtensionUrl)
                return extObj["valueCode"]?.GetValue<string>();
        }

        return null;
    }
}
