// <copyright file="PatternCheckTests.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

#pragma warning disable CA1861 // Avoid constant arrays as arguments

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Validation;
using Ignixa.Validation.Checks;
using Ignixa.Validation.Tests.TestHelpers;
using Xunit;

namespace Ignixa.Validation.Tests.Checks;

/// <summary>
/// Tests for PatternCheck.
/// </summary>
public class PatternCheckTests
{
    #region Basic Pattern Matching

    [Fact]
    public void GivenPatternString_WhenValueMatchesExactly_ThenReturnsSuccess()
    {
        // Arrange
        var json = JsonNode.Parse("{\"resourceType\":\"Patient\",\"gender\":\"male\"}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new PatternCheck("gender", "\"male\"");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenPatternString_WhenValueDoesNotMatch_ThenReturnsError()
    {
        // Arrange
        var json = JsonNode.Parse("{\"resourceType\":\"Patient\",\"gender\":\"female\"}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new PatternCheck("gender", "\"male\"");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
        Assert.Contains(result.Issues, i => i.Code == "pattern-mismatch");
    }

    #endregion

    #region Partial Matching (Key Feature)

    [Fact]
    public void GivenPatternOnUse_WhenActualHasAdditionalProperties_ThenReturnsSuccess()
    {
        // Arrange - Pattern requires use="official", but actual also has family and given
        var json = JsonNode.Parse(@"{
            ""resourceType"":""Patient"",
            ""name"":[{
                ""use"":""official"",
                ""family"":""Smith"",
                ""given"":[""John""]
            }]
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var pattern = @"[{""use"":""official""}]";
        var check = new PatternCheck("name", pattern);
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenPatternOnNameUse_WhenRequiredPropertyMissing_ThenReturnsError()
    {
        // Arrange - Pattern requires use="official", but actual doesn't have use
        var json = JsonNode.Parse(@"{
            ""resourceType"":""Patient"",
            ""name"":[{
                ""family"":""Smith"",
                ""given"":[""John""]
            }]
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var pattern = @"[{""use"":""official""}]";
        var check = new PatternCheck("name", pattern);
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
        Assert.Contains(result.Issues, i => i.Code == "pattern-mismatch");
        Assert.Contains("missing required property", result.Issues[0].Message);
    }

    [Fact]
    public void GivenPatternOnIdentifier_WhenActualHasAdditionalIdentifiers_ThenReturnsSuccess()
    {
        // Arrange - Pattern requires one identifier with system, actual has two identifiers
        var json = JsonNode.Parse(@"{
            ""resourceType"":""Patient"",
            ""identifier"":[
                {""system"":""http://hospital.example.org"",""value"":""12345""},
                {""system"":""http://ssn.example.org"",""value"":""123-45-6789""}
            ]
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var pattern = @"[{""system"":""http://hospital.example.org""}]";
        var check = new PatternCheck("identifier", pattern);
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    #endregion

    #region Nested Patterns

    [Fact]
    public void GivenNestedPattern_WhenAllPropertiesMatch_ThenReturnsSuccess()
    {
        // Arrange
        var json = JsonNode.Parse(@"{
            ""resourceType"":""Patient"",
            ""name"":[{
                ""use"":""official"",
                ""family"":""Smith"",
                ""given"":[""John"",""Jacob""]
            }]
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        // Pattern checks for name array with at least use=official and family=Smith
        var pattern = @"[{""use"":""official"",""family"":""Smith""}]";
        var check = new PatternCheck("name", pattern);
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenNestedPattern_WhenNestedPropertyMismatch_ThenReturnsError()
    {
        // Arrange
        var json = JsonNode.Parse(@"{
            ""resourceType"":""Patient"",
            ""name"":[{
                ""use"":""official"",
                ""family"":""Jones"",
                ""given"":[""John""]
            }]
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var pattern = @"[{""use"":""official"",""family"":""Smith""}]";
        var check = new PatternCheck("name", pattern);
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
        Assert.Contains(result.Issues, i => i.Code == "pattern-mismatch");
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void GivenPattern_WhenElementNotPresent_ThenReturnsSuccess()
    {
        // Arrange - element "code" is not present
        var json = JsonNode.Parse("{\"resourceType\":\"Observation\",\"status\":\"final\"}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var pattern = @"{""coding"":[{""system"":""http://loinc.org""}]}";
        var check = new PatternCheck("code", pattern);
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenNullPatternJson_WhenConstructing_ThenThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new PatternCheck("code", null!));
    }

    [Fact]
    public void GivenInvalidPatternJson_WhenConstructing_ThenThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new PatternCheck("code", "{invalid json"));
    }

    [Fact]
    public void GivenPatternBoolean_WhenValueMatches_ThenReturnsSuccess()
    {
        // Arrange
        var json = JsonNode.Parse("{\"resourceType\":\"Patient\",\"active\":true}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new PatternCheck("active", "true");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenPatternInteger_WhenValueMatches_ThenReturnsSuccess()
    {
        // Arrange
        var json = JsonNode.Parse("{\"resourceType\":\"Observation\",\"valueInteger\":100}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new PatternCheck("valueInteger", "100");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    #endregion
}
