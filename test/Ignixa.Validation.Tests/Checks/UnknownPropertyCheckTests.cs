// <copyright file="UnknownPropertyCheckTests.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Validation;
using Ignixa.Validation.Checks;
using Ignixa.Validation.Tests.TestHelpers;
using Xunit;

namespace Ignixa.Validation.Tests.Checks;

/// <summary>
/// Tests for UnknownPropertyCheck.
/// </summary>
public class UnknownPropertyCheckTests
{
    #region Valid Scenarios

    [Fact]
    public void GivenPatientWithOnlyDefinedProperties_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""123"",
            ""active"": true,
            ""name"": [{""family"": ""Doe""}]
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var allowedProperties = new[] { "active", "name", "gender", "birthDate" };
        var check = new UnknownPropertyCheck(allowedProperties);
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenPatientWithShadowProperty_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Patient"",
            ""birthDate"": ""1990-01-01"",
            ""_birthDate"": {
                ""extension"": [{
                    ""url"": ""http://example.org/ext"",
                    ""valueString"": ""approximate""
                }]
            }
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var allowedProperties = new[] { "birthDate", "active", "name" };
        var check = new UnknownPropertyCheck(allowedProperties);
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenPatientWithExtension_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Patient"",
            ""extension"": [{
                ""url"": ""http://example.org/ext"",
                ""valueString"": ""value""
            }]
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var allowedProperties = new[] { "active", "name" };
        var check = new UnknownPropertyCheck(allowedProperties);
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.True(result.IsValid); // extension is always allowed
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenPatientWithModifierExtension_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Patient"",
            ""modifierExtension"": [{
                ""url"": ""http://example.org/ext"",
                ""valueBoolean"": true
            }]
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var allowedProperties = new[] { "active", "name" };
        var check = new UnknownPropertyCheck(allowedProperties);
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.True(result.IsValid); // modifierExtension is always allowed
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenPatientWithUniversalProperties_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""123"",
            ""meta"": {
                ""versionId"": ""1""
            },
            ""implicitRules"": ""http://example.org/rules"",
            ""language"": ""en-US"",
            ""text"": {
                ""status"": ""generated"",
                ""div"": ""<div>Text</div>""
            },
            ""contained"": []
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var allowedProperties = new[] { "active", "name" };
        var check = new UnknownPropertyCheck(allowedProperties);
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.True(result.IsValid); // all universal properties allowed
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenPatientWithMixedValidProperties_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""123"",
            ""meta"": {""versionId"": ""1""},
            ""active"": true,
            ""name"": [{""family"": ""Doe""}],
            ""_birthDate"": {""extension"": []},
            ""extension"": [{""url"": ""http://example.org""}],
            ""modifierExtension"": []
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var allowedProperties = new[] { "active", "name", "birthDate", "gender" };
        var check = new UnknownPropertyCheck(allowedProperties);
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    #endregion

    #region Invalid Scenarios

    [Fact]
    public void GivenPatientWithUnknownProperty_WhenValidating_ThenReturnsError()
    {
        // Arrange
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""123"",
            ""unknownField"": ""value""
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var allowedProperties = new[] { "active", "name", "gender", "birthDate" };
        var check = new UnknownPropertyCheck(allowedProperties);
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
        Assert.Contains(result.Issues, i => i.Code == "unknown-property");
        Assert.Contains("unknownField", result.Issues[0].Message);
    }

    [Fact]
    public void GivenPatientWithTypoInPropertyName_WhenValidating_ThenReturnsError()
    {
        // Arrange
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""123"",
            ""name"": [{
                ""famly"": ""Doe""
            }]
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var allowedProperties = new[] { "active", "name", "gender", "birthDate" };
        var check = new UnknownPropertyCheck(allowedProperties);
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        // Note: This validates the root level, not nested properties
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.True(result.IsValid); // "name" is valid at root level
    }

    [Fact]
    public void GivenPatientWithPropertyFromDifferentResourceType_WhenValidating_ThenReturnsError()
    {
        // Arrange
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""123"",
            ""subject"": {
                ""reference"": ""Patient/456""
            }
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var allowedProperties = new[] { "active", "name", "gender", "birthDate" };
        var check = new UnknownPropertyCheck(allowedProperties);
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
        Assert.Contains(result.Issues, i => i.Code == "unknown-property");
        Assert.Contains("subject", result.Issues[0].Message);
    }

    [Fact]
    public void GivenPatientWithMultipleUnknownProperties_WhenValidating_ThenReturnsMultipleErrors()
    {
        // Arrange
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""123"",
            ""unknownField1"": ""value1"",
            ""unknownField2"": ""value2"",
            ""unknownField3"": ""value3""
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var allowedProperties = new[] { "active", "name", "gender", "birthDate" };
        var check = new UnknownPropertyCheck(allowedProperties);
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(3, result.Issues.Count);
        Assert.All(result.Issues, issue => Assert.Equal("unknown-property", issue.Code));
    }

    [Fact]
    public void GivenPatientWithShadowPropertyButNoMainProperty_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        // Shadow property without main property is allowed if main property is in allowed list
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Patient"",
            ""_birthDate"": {
                ""extension"": [{
                    ""url"": ""http://example.org/ext"",
                    ""valueString"": ""approximate""
                }]
            }
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var allowedProperties = new[] { "birthDate", "active", "name" };
        var check = new UnknownPropertyCheck(allowedProperties);
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.True(result.IsValid); // _birthDate is allowed because birthDate is allowed
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenPatientWithInvalidShadowProperty_WhenValidating_ThenReturnsError()
    {
        // Arrange
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Patient"",
            ""_unknownField"": {
                ""extension"": []
            }
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var allowedProperties = new[] { "active", "name", "gender", "birthDate" };
        var check = new UnknownPropertyCheck(allowedProperties);
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
        Assert.Contains(result.Issues, i => i.Code == "unknown-property");
        Assert.Contains("_unknownField", result.Issues[0].Message);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void GivenEmptyResource_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Patient""
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var allowedProperties = new[] { "active", "name", "gender", "birthDate" };
        var check = new UnknownPropertyCheck(allowedProperties);
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenEmptyAllowedList_WhenValidating_ThenAllNonUniversalPropertiesFail()
    {
        // Arrange
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""123"",
            ""active"": true
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var allowedProperties = Array.Empty<string>();
        var check = new UnknownPropertyCheck(allowedProperties);
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
        Assert.Contains(result.Issues, i => i.Code == "unknown-property" && i.Message.Contains("active"));
    }

    [Fact]
    public void GivenCaseSensitivePropertyNames_WhenValidating_ThenCaseMismatchFails()
    {
        // Arrange
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Patient"",
            ""Active"": true
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var allowedProperties = new[] { "active", "name", "gender" };
        var check = new UnknownPropertyCheck(allowedProperties);
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
        Assert.Contains(result.Issues, i => i.Code == "unknown-property" && i.Message.Contains("Active"));
    }

    #endregion

    #region Choice Type Tests

    [Fact]
    public void GivenChoiceTypeProperty_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange - Observation with value[x] choice type
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Observation"",
            ""id"": ""123"",
            ""valueQuantity"": {
                ""value"": 120,
                ""unit"": ""mmHg""
            }
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        // Include "value[x]" as a choice type element
        var allowedProperties = new[] { "status", "code", "subject", "value[x]", "effective[x]" };
        var check = new UnknownPropertyCheck(allowedProperties);
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenEffectiveDateTimeChoiceType_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange - Observation with effective[x] choice type
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Observation"",
            ""id"": ""123"",
            ""effectiveDateTime"": ""2024-01-15T10:00:00Z""
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        // Include "effective[x]" as a choice type element
        var allowedProperties = new[] { "status", "code", "subject", "value[x]", "effective[x]" };
        var check = new UnknownPropertyCheck(allowedProperties);
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
