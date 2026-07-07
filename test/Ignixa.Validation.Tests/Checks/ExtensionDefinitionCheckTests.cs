// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Checks;
using Ignixa.Validation.Tests.TestHelpers;
using Shouldly;
using Xunit;

namespace Ignixa.Validation.Tests.Checks;

public sealed class ExtensionDefinitionCheckTests
{
    private static ValidationResult Validate(string json)
    {
        var element = JsonNodeSourceNode.Create(JsonNode.Parse(json)!).ToElement(TestSchemaProvider.GetR4Schema());
        return new ExtensionDefinitionCheck().Validate(
            element,
            new ValidationSettings { Depth = ValidationDepth.Full },
            new ValidationState());
    }

    [Fact]
    public void GivenExtensionUrlOnReservedExampleHost_WhenValidating_ThenReturnsError()
    {
        // Arrange - example.org is an RFC 2606 reserved host; it can never be a real extension identity.
        var result = Validate("""
        {
            "resourceType": "Patient",
            "birthDate": "1975",
            "_birthDate": {
                "extension": [
                    { "url": "http://example.org/fhir/StructureDefinition/something",
                      "valueString": "x" }
                ]
            }
        }
        """);

        // Act / Assert
        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Code == "ext-example-url");
    }

    [Fact]
    public void GivenExtensionUrlOnRealHost_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange - a vendor extension url is unresolvable in this pipeline, but that is not, by itself,
        // an error: the check stays silent rather than reject every resource carrying an extension.
        var result = Validate("""
        {
            "resourceType": "Patient",
            "birthDate": "1975",
            "_birthDate": {
                "extension": [
                    { "url": "http://validitron.unimelb.edu.au/fhir/StructureDefinition/age",
                      "valueString": "x" }
                ]
            }
        }
        """);

        // Act / Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenNestedExampleUrlUnderPrimitiveShadow_WhenValidating_ThenReturnsError()
    {
        // Arrange - the walk must descend into complex (nested) extensions, carrying the shadow scope.
        var result = Validate("""
        {
            "resourceType": "Patient",
            "birthDate": "1975",
            "_birthDate": {
                "extension": [ { "url": "http://acme.org/parent", "extension": [
                    { "url": "http://fhir.example.com/child", "valueString": "x" }
                ] } ]
            }
        }
        """);

        // Act / Assert
        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Code == "ext-example-url");
    }

    [Fact]
    public void GivenExampleUrlOnRootExtension_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange - root (non-primitive) extensions are left alone: the reference validator accepts
        // example.org there (matchetype template resources), so enforcement would be over-strict.
        var result = Validate("""
        {
            "resourceType": "Patient",
            "extension": [
                { "url": "http://example.org/fhir/StructureDefinition/test", "valueBoolean": true }
            ],
            "name": [ { "text": "test-name" } ]
        }
        """);

        // Act / Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenExampleHostReferenceButNotExtensionUrl_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange - example.org is ubiquitous in reference/id fields; only extension URLs are constrained.
        var result = Validate("""
        {
            "resourceType": "Patient",
            "_birthDate": { "extension": [ { "url": "http://acme.org/x", "valueString": "y" } ] },
            "generalPractitioner": [ { "reference": "http://example.org/fhir/Organization/2342" } ]
        }
        """);

        // Act / Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenExampleExtensionUrl_WhenBelowFullDepth_ThenReturnsSuccess()
    {
        // Arrange - profile-tier rule must not fire at Compatibility/Spec depth (same input that
        // errors at Full below).
        var element = JsonNodeSourceNode.Create(JsonNode.Parse("""
        {
            "resourceType": "Patient",
            "_birthDate": { "extension": [ { "url": "http://example.org/x", "valueString": "y" } ] }
        }
        """)!).ToElement(TestSchemaProvider.GetR4Schema());

        var result = new ExtensionDefinitionCheck().Validate(
            element,
            new ValidationSettings { Depth = ValidationDepth.Spec },
            new ValidationState());

        // Act / Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }
}
