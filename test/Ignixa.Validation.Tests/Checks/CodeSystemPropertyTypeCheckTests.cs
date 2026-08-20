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

public sealed class CodeSystemPropertyTypeCheckTests
{
    private static ValidationResult Validate(string json)
    {
        var element = JsonNodeSourceNode.Create(JsonNode.Parse(json)!).ToElement(TestSchemaProvider.GetR4Schema());
        return new CodeSystemPropertyTypeCheck().Validate(element, new ValidationSettings(), ValidationState.ForRoot(element));
    }

    [Fact]
    public void GivenConceptPropertyWithWrongValueType_WhenValidating_ThenReturnsError()
    {
        // Arrange - 'flag' is declared dateTime but the concept supplies a valueBoolean.
        var result = Validate("""
        {
            "resourceType": "CodeSystem", "status": "active", "content": "complete",
            "property": [ { "code": "flag", "type": "dateTime" } ],
            "concept": [ { "code": "c1", "property": [ { "code": "flag", "valueBoolean": false } ] } ]
        }
        """);

        // Act / Assert
        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Code == "csp-1" && i.Message.Contains("boolean", StringComparison.Ordinal));
    }

    [Fact]
    public void GivenConceptPropertyWithMatchingValueType_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        var result = Validate("""
        {
            "resourceType": "CodeSystem", "status": "active", "content": "complete",
            "property": [ { "code": "weight", "type": "decimal" } ],
            "concept": [ { "code": "c1", "property": [ { "code": "weight", "valueDecimal": 1.5 } ] } ]
        }
        """);

        // Act / Assert
        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void GivenUndeclaredConceptProperty_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange - an undeclared property draws an unknown-property warning elsewhere, not this error.
        var result = Validate("""
        {
            "resourceType": "CodeSystem", "status": "active", "content": "complete",
            "property": [ { "code": "flag", "type": "dateTime" } ],
            "concept": [ { "code": "c1", "property": [ { "code": "other", "valueBoolean": true } ] } ]
        }
        """);

        // Act / Assert
        result.IsValid.ShouldBeTrue();
    }
}
