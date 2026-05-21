using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.TestScript.Fixtures;
using Ignixa.TestScript.Model;
using NSubstitute;

namespace Ignixa.TestScript.Tests.Fixtures;

public class CompositeFixtureProviderTests
{
    private readonly FixtureResolutionContext _context = new()
    {
        Schema = Substitute.For<IFhirSchemaProvider>()
    };

    [Fact]
    public async Task GivenMultipleProviders_WhenFirstResolves_ThenReturnsFirstResult()
    {
        var expected = JsonNode.Parse("""{"resourceType": "Patient"}""");
        var provider1 = Substitute.For<IFixtureProvider>();
        provider1.ResolveFixtureAsync(Arg.Any<FixtureDefinition>(), Arg.Any<FixtureResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(expected);
        var provider2 = Substitute.For<IFixtureProvider>();

        var composite = new CompositeFixtureProvider([provider1, provider2]);
        var fixture = new FixtureDefinition { Id = "test" };

        var result = await composite.ResolveFixtureAsync(fixture, _context, CancellationToken.None);

        result.ShouldNotBeNull();
        result["resourceType"]?.GetValue<string>().ShouldBe("Patient");
        await provider2.DidNotReceive().ResolveFixtureAsync(Arg.Any<FixtureDefinition>(), Arg.Any<FixtureResolutionContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenFirstReturnsNull_WhenSecondResolves_ThenReturnsFallback()
    {
        var provider1 = Substitute.For<IFixtureProvider>();
        provider1.ResolveFixtureAsync(Arg.Any<FixtureDefinition>(), Arg.Any<FixtureResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns((JsonNode?)null);

        var expected = JsonNode.Parse("""{"resourceType": "Observation"}""");
        var provider2 = Substitute.For<IFixtureProvider>();
        provider2.ResolveFixtureAsync(Arg.Any<FixtureDefinition>(), Arg.Any<FixtureResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(expected);

        var composite = new CompositeFixtureProvider([provider1, provider2]);
        var fixture = new FixtureDefinition { Id = "test" };

        var result = await composite.ResolveFixtureAsync(fixture, _context, CancellationToken.None);

        result.ShouldNotBeNull();
        result["resourceType"]?.GetValue<string>().ShouldBe("Observation");
    }

    [Fact]
    public async Task GivenNoProviderResolves_WhenResolving_ThenReturnsNull()
    {
        var provider1 = Substitute.For<IFixtureProvider>();
        provider1.ResolveFixtureAsync(Arg.Any<FixtureDefinition>(), Arg.Any<FixtureResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns((JsonNode?)null);

        var composite = new CompositeFixtureProvider([provider1]);
        var fixture = new FixtureDefinition { Id = "test" };

        var result = await composite.ResolveFixtureAsync(fixture, _context, CancellationToken.None);

        result.ShouldBeNull();
    }
}
