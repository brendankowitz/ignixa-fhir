// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Validation;
using Ignixa.Validation.Checks;
using Ignixa.Validation.Tests.TestHelpers;
using Shouldly;
using Xunit;

namespace Ignixa.Validation.Tests.Checks;

public sealed class ExtensionUrlVersionCheckTests
{
    private static ValidationResult Validate(string json)
    {
        var element = JsonNodeSourceNode.Create(JsonNode.Parse(json)!).ToElement(TestSchemaProvider.GetR4Schema());
        return new ExtensionUrlVersionCheck().Validate(element, new ValidationSettings(), ValidationState.ForRoot(element));
    }

    [Fact]
    public void GivenExtensionUrlWithVersion_WhenValidating_ThenReturnsError()
    {
        // Arrange - the '|4.0.0' version suffix is not permitted on an extension instance url.
        var result = Validate("""
        {
            "resourceType": "Patient",
            "extension": [
                { "url": "http://hl7.org/fhir/StructureDefinition/patient-congregation|4.0.0",
                  "valueString": "temple" }
            ]
        }
        """);

        // Act / Assert
        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Code == "ext-url-version");
    }

    [Fact]
    public void GivenNestedExtensionUrlWithVersion_WhenValidating_ThenReturnsError()
    {
        // Arrange - extensions can nest; the walk must descend into complex extensions.
        var result = Validate("""
        {
            "resourceType": "Patient",
            "extension": [ { "url": "http://example.org/parent", "extension": [
                { "url": "http://example.org/child|1.0.0", "valueString": "x" }
            ] } ]
        }
        """);

        // Act / Assert
        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Code == "ext-url-version");
    }

    [Fact]
    public void GivenExtensionUrlWithoutVersion_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        var result = Validate("""
        {
            "resourceType": "Patient",
            "extension": [
                { "url": "http://hl7.org/fhir/StructureDefinition/patient-interpreterRequired",
                  "valueBoolean": true }
            ]
        }
        """);

        // Act / Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }
}
