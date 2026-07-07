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

public sealed class CodeSystemSupplementContentCheckTests
{
    private static ValidationResult Validate(string json)
    {
        var element = JsonNodeSourceNode.Create(JsonNode.Parse(json)!).ToElement(TestSchemaProvider.GetR4Schema());
        return new CodeSystemSupplementContentCheck().Validate(element, new ValidationSettings(), new ValidationState());
    }

    [Fact]
    public void GivenSupplementWithNonSupplementContent_WhenValidating_ThenReturnsError()
    {
        // Arrange - declaring 'supplements' makes this a supplement, so content must be 'supplement'.
        var result = Validate("""
        { "resourceType": "CodeSystem", "status": "active",
          "content": "complete", "supplements": "http://loinc.org" }
        """);

        // Act / Assert
        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Code == "csc-1");
    }

    [Fact]
    public void GivenSupplementWithSupplementContent_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        var result = Validate("""
        { "resourceType": "CodeSystem", "status": "active",
          "content": "supplement", "supplements": "http://loinc.org" }
        """);

        // Act / Assert
        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void GivenNonSupplementCodeSystem_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange - no 'supplements' element, so the rule does not apply.
        var result = Validate("""
        { "resourceType": "CodeSystem", "status": "active", "content": "complete" }
        """);

        // Act / Assert
        result.IsValid.ShouldBeTrue();
    }
}
