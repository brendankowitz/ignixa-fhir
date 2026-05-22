using Ignixa.Abstractions;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.TestScript.FhirFakes;
using Ignixa.TestScript.Fixtures;
using Ignixa.TestScript.Model;
using NSubstitute;

namespace Ignixa.TestScript.Tests.Fixtures;

public class FhirFakesFixtureProviderTests
{
    private readonly FhirFakesFixtureProvider _provider = new();

    private static IFhirSchemaProvider BuildSchema(string resourceType)
    {
        var typeDefinition = Substitute.For<IType>();
        typeDefinition.Info.Returns(new TypeInfo(resourceType, isResource: true));
        typeDefinition.Children.Returns([]);

        var valueSetProvider = Substitute.For<IValueSetProvider>();
        valueSetProvider.GetCodes(Arg.Any<string>()).Returns((IReadOnlyList<FhirCode>?)null);
        valueSetProvider.IsKnownValueSet(Arg.Any<string>()).Returns(false);

        var schema = Substitute.For<IFhirSchemaProvider>();
        schema.ResourceTypeNames.Returns(new HashSet<string>(StringComparer.Ordinal) { resourceType });
        schema.GetTypeDefinition(resourceType).Returns(typeDefinition);
        schema.ValueSetProvider.Returns(valueSetProvider);

        return schema;
    }

    private static FixtureResolutionContext BuildContext(IFhirSchemaProvider schema, string? resourceType = null) =>
        new() { Schema = schema, ResourceType = resourceType };

    [Fact]
    public async Task GivenFixtureWithFhirFakesExtension_WhenResolving_ThenGeneratesResourceOfDeclaredType()
    {
        var schema = BuildSchema("Patient");
        var context = BuildContext(schema);
        var fixture = new FixtureDefinition
        {
            Id = "patient-fixture",
            Resource = JsonSourceNodeFactory.Parse("""
                {
                    "resourceType": "Basic",
                    "extension": [
                        {
                            "url": "http://ignixa.io/testscript/fhirfakes",
                            "valueCode": "Patient"
                        }
                    ]
                }
                """)
        };

        var result = await _provider.ResolveFixtureAsync(fixture, context, CancellationToken.None);

        result.ShouldNotBeNull();
        result.ResourceType.ShouldBe("Patient");
    }

    [Fact]
    public async Task GivenFixtureWithNoExtension_WhenResolving_ThenReturnsNull()
    {
        var schema = Substitute.For<IFhirSchemaProvider>();
        var context = BuildContext(schema);
        var fixture = new FixtureDefinition
        {
            Id = "no-ext-fixture",
            Resource = JsonSourceNodeFactory.Parse("""{"resourceType": "Basic"}""")
        };

        var result = await _provider.ResolveFixtureAsync(fixture, context, CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task GivenFixtureWithNullResource_WhenResolving_ThenReturnsNull()
    {
        var schema = Substitute.For<IFhirSchemaProvider>();
        var context = BuildContext(schema);
        var fixture = new FixtureDefinition
        {
            Id = "null-resource-fixture",
            Resource = null
        };

        var result = await _provider.ResolveFixtureAsync(fixture, context, CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task GivenFixtureWithExtensionWithWrongUrl_WhenResolving_ThenReturnsNull()
    {
        var schema = Substitute.For<IFhirSchemaProvider>();
        var context = BuildContext(schema);
        var fixture = new FixtureDefinition
        {
            Id = "wrong-url-fixture",
            Resource = JsonSourceNodeFactory.Parse("""
                {
                    "resourceType": "Basic",
                    "extension": [
                        {
                            "url": "http://example.com/other",
                            "valueCode": "Patient"
                        }
                    ]
                }
                """)
        };

        var result = await _provider.ResolveFixtureAsync(fixture, context, CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task GivenFixtureWithMultipleExtensions_WhenFhirFakesExtensionIsSecond_ThenGeneratesCorrectType()
    {
        var schema = BuildSchema("Observation");
        var context = BuildContext(schema);
        var fixture = new FixtureDefinition
        {
            Id = "multi-ext-fixture",
            Resource = JsonSourceNodeFactory.Parse("""
                {
                    "resourceType": "Basic",
                    "extension": [
                        {
                            "url": "http://example.com/other",
                            "valueCode": "Patient"
                        },
                        {
                            "url": "http://ignixa.io/testscript/fhirfakes",
                            "valueCode": "Observation"
                        }
                    ]
                }
                """)
        };

        var result = await _provider.ResolveFixtureAsync(fixture, context, CancellationToken.None);

        result.ShouldNotBeNull();
        result.ResourceType.ShouldBe("Observation");
    }

    [Fact]
    public async Task GivenFixtureWithMatchingExtensionMissingValueCode_WhenResolving_ThenReturnsNull()
    {
        var schema = Substitute.For<IFhirSchemaProvider>();
        var context = BuildContext(schema);
        var fixture = new FixtureDefinition
        {
            Id = "no-valuecode-fixture",
            Resource = JsonSourceNodeFactory.Parse("""
                {
                    "resourceType": "Basic",
                    "extension": [
                        {
                            "url": "http://ignixa.io/testscript/fhirfakes"
                        }
                    ]
                }
                """)
        };

        var result = await _provider.ResolveFixtureAsync(fixture, context, CancellationToken.None);

        result.ShouldBeNull();
    }
}
