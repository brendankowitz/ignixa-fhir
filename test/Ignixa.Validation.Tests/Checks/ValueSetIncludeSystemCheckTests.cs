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

public sealed class ValueSetIncludeSystemCheckTests
{
    private static ValidationResult Validate(string json)
    {
        var element = JsonNodeSourceNode.Create(JsonNode.Parse(json)!).ToElement(TestSchemaProvider.GetR4Schema());
        return new ValueSetIncludeSystemCheck().Validate(element, new ValidationSettings(), ValidationState.ForRoot(element));
    }

    [Fact]
    public void GivenFragmentIncludeSystem_WhenValidating_ThenReturnsError()
    {
        // Arrange - '#c1' references a contained code system, which is not an absolute URI.
        var result = Validate("""
        {
            "resourceType": "ValueSet", "status": "active",
            "compose": { "include": [ { "system": "#c1" } ] }
        }
        """);

        // Act / Assert
        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Code == "vs-1");
    }

    [Fact]
    public void GivenRelativeIncludeSystem_WhenValidating_ThenReturnsError()
    {
        // Arrange
        var result = Validate("""
        {
            "resourceType": "ValueSet", "status": "active",
            "compose": { "exclude": [ { "system": "CodeSystem/local" } ] }
        }
        """);

        // Act / Assert
        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Code == "vs-1");
    }

    [Fact]
    public void GivenAbsoluteIncludeSystems_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        var result = Validate("""
        {
            "resourceType": "ValueSet", "status": "active",
            "compose": { "include": [
                { "system": "http://loinc.org" },
                { "system": "urn:uuid:6a2ee390-978e-42c6-8f88-c17dff3bd8a3" },
                { "valueSet": [ "http://example.org/vs" ] }
            ] }
        }
        """);

        // Act / Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }
}
