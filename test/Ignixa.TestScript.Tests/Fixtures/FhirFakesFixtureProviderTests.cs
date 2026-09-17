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

    [Fact]
    public async Task GivenFixtureWithCanonicalComplexExtension_WhenResolving_ThenAppliesGenericOptions()
    {
        var schema = BuildSchema("Observation");
        var context = BuildContext(schema);
        var fixture = new FixtureDefinition
        {
            Id = "configured-observation",
            Resource = JsonSourceNodeFactory.Parse("""
                {
                    "resourceType": "Basic",
                    "extension": [
                        {
                            "url": "http://ignixa.io/fhir/StructureDefinition/testscript-fhirfakes",
                            "valueCode": "Patient",
                            "extension": [
                                { "url": "resourceType", "valueCode": "Observation" },
                                { "url": "seed", "valueInteger": 123 },
                                { "url": "density", "valueCode": "maximum" },
                                { "url": "theme", "valueCode": "cardiology" },
                                { "url": "profile", "valueCanonical": "http://example.org/fhir/StructureDefinition/test-observation" },
                                { "url": "tag", "valueString": "test-run-1" }
                            ]
                        }
                    ]
                }
                """)
        };

        var result = await _provider.ResolveFixtureAsync(fixture, context, CancellationToken.None);

        result.ShouldNotBeNull();
        result.ResourceType.ShouldBe("Observation");
        result.MutableNode["meta"]!["profile"]![0]!.GetValue<string>()
            .ShouldBe("http://example.org/fhir/StructureDefinition/test-observation");
        result.MutableNode["meta"]!["tag"]![0]!["code"]!.GetValue<string>().ShouldBe("test-run-1");
    }

    [Fact]
    public async Task GivenPatientConfiguration_WhenResolving_ThenAppliesPatientOptions()
    {
        var schema = BuildSchema("Patient");
        var context = BuildContext(schema);
        var fixture = new FixtureDefinition
        {
            Id = "configured-patient",
            Resource = JsonSourceNodeFactory.Parse("""
                {
                    "resourceType": "Basic",
                    "extension": [
                        {
                            "url": "http://ignixa.io/testscript/fhirfakes",
                            "extension": [
                                { "url": "resourceType", "valueCode": "Patient" },
                                { "url": "seed", "valueInteger": 456 },
                                { "url": "tag", "valueString": "patient-test-run" },
                                { "url": "profile", "valueCanonical": "http://example.org/fhir/StructureDefinition/test-patient" },
                                {
                                    "url": "patient",
                                    "extension": [
                                        { "url": "givenName", "valueString": "Ada" },
                                        { "url": "familyName", "valueString": "Lovelace" },
                                        { "url": "gender", "valueCode": "female" },
                                        { "url": "birthDate", "valueDate": "1985-12-10" },
                                        { "url": "city", "valueString": "London" },
                                        { "url": "state", "valueString": "Greater London" },
                                        { "url": "zipCode", "valueString": "SW1A" },
                                        { "url": "active", "valueBoolean": false },
                                        { "url": "bmi", "valueDecimal": 21.5 },
                                        {
                                            "url": "identifier",
                                            "extension": [
                                                { "url": "system", "valueUri": "http://example.org/mrn" },
                                                { "url": "value", "valueString": "MRN-123" }
                                            ]
                                        }
                                    ]
                                }
                            ]
                        }
                    ]
                }
                """)
        };

        var result = await _provider.ResolveFixtureAsync(fixture, context, CancellationToken.None);

        result.ShouldNotBeNull();
        result.ResourceType.ShouldBe("Patient");
        result.MutableNode["name"]![0]!["given"]![0]!.GetValue<string>().ShouldBe("Ada");
        result.MutableNode["name"]![0]!["family"]!.GetValue<string>().ShouldBe("Lovelace");
        result.MutableNode["gender"]!.GetValue<string>().ShouldBe("female");
        result.MutableNode["birthDate"]!.GetValue<string>().ShouldBe("1985-12-10");
        result.MutableNode["active"]!.GetValue<bool>().ShouldBeFalse();
        result.MutableNode["identifier"]![0]!["system"]!.GetValue<string>().ShouldBe("http://example.org/mrn");
        result.MutableNode["identifier"]![0]!["value"]!.GetValue<string>().ShouldBe("MRN-123");
        result.MutableNode["meta"]!["profile"]![0]!.GetValue<string>()
            .ShouldBe("http://example.org/fhir/StructureDefinition/test-patient");
        result.MutableNode["meta"]!["tag"]![0]!["code"]!.GetValue<string>().ShouldBe("patient-test-run");
    }

    [Fact]
    public async Task GivenNestedResourceTypeAndLegacyValueCode_WhenResolving_ThenNestedResourceTypeWins()
    {
        var schema = BuildSchema("Observation");
        var context = BuildContext(schema);
        var fixture = new FixtureDefinition
        {
            Id = "nested-wins",
            Resource = JsonSourceNodeFactory.Parse("""
                {
                    "resourceType": "Basic",
                    "extension": [
                        {
                            "url": "http://ignixa.io/testscript/fhirfakes",
                            "valueCode": "Patient",
                            "extension": [
                                { "url": "resourceType", "valueCode": "Observation" }
                            ]
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
    public async Task GivenInvalidDensity_WhenResolving_ThenThrowsFormatException()
    {
        var schema = BuildSchema("Patient");
        var context = BuildContext(schema);
        var fixture = new FixtureDefinition
        {
            Id = "bad-density",
            Resource = JsonSourceNodeFactory.Parse("""
                {
                    "resourceType": "Basic",
                    "extension": [
                        {
                            "url": "http://ignixa.io/testscript/fhirfakes",
                            "extension": [
                                { "url": "resourceType", "valueCode": "Patient" },
                                { "url": "density", "valueCode": "very-dense" }
                            ]
                        }
                    ]
                }
                """)
        };

        await Should.ThrowAsync<FormatException>(() => _provider.ResolveFixtureAsync(fixture, context, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task GivenEdgeCaseSelectors_WhenResolving_ThenStillGeneratesRequestedResource()
    {
        var schema = BuildSchema("Patient");
        var context = BuildContext(schema);
        var fixture = new FixtureDefinition
        {
            Id = "edge-case-patient",
            Resource = JsonSourceNodeFactory.Parse("""
                {
                    "resourceType": "Basic",
                    "extension": [
                        {
                            "url": "http://ignixa.io/testscript/fhirfakes",
                            "extension": [
                                { "url": "resourceType", "valueCode": "Patient" },
                                { "url": "seed", "valueInteger": 789 },
                                {
                                    "url": "edgeCase",
                                    "extension": [
                                        { "url": "selector", "valueCode": "unicode" },
                                        { "url": "selector", "valueCode": "temporal" },
                                        { "url": "seed", "valueInteger": 101112 }
                                    ]
                                }
                            ]
                        }
                    ]
                }
                """)
        };

        var result = await _provider.ResolveFixtureAsync(fixture, context, CancellationToken.None);

        result.ShouldNotBeNull();
        result.ResourceType.ShouldBe("Patient");
    }
}
