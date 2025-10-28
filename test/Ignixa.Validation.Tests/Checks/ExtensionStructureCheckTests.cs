// <copyright file="ExtensionStructureCheckTests.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

#pragma warning disable CA1861 // Prefer static readonly fields - not applicable for test code

using System.Text.Json.Nodes;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Validation;
using Ignixa.Validation.Checks;
using Xunit;

namespace Ignixa.Validation.Tests.Checks;

/// <summary>
/// Tests for ExtensionStructureCheck.
/// </summary>
public class ExtensionStructureCheckTests
{
    #region Valid Scenarios - Simple Extensions

    [Fact]
    public void GivenSimpleExtensionWithUrlAndValue_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange - Patient with simple extension
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Patient"",
            ""extension"": [{
                ""url"": ""http://example.org/ext/race"",
                ""valueString"": ""Asian""
            }]
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new ExtensionStructureCheck("extension");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenExtensionWithDifferentValueTypes_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange - Extensions with various value types
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Patient"",
            ""extension"": [
                {
                    ""url"": ""http://example.org/ext/string"",
                    ""valueString"": ""text""
                },
                {
                    ""url"": ""http://example.org/ext/boolean"",
                    ""valueBoolean"": true
                },
                {
                    ""url"": ""http://example.org/ext/integer"",
                    ""valueInteger"": 42
                }
            ]
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new ExtensionStructureCheck("extension");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    #endregion

    #region Valid Scenarios - Complex Extensions

    [Fact]
    public void GivenComplexExtensionWithNestedExtensions_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange - Complex extension with nested extensions
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Patient"",
            ""extension"": [{
                ""url"": ""http://example.org/ext/name-parts"",
                ""extension"": [
                    {
                        ""url"": ""http://example.org/ext/prefix"",
                        ""valueString"": ""Dr.""
                    },
                    {
                        ""url"": ""http://example.org/ext/suffix"",
                        ""valueString"": ""PhD""
                    }
                ]
            }]
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new ExtensionStructureCheck("extension");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenNestedExtensionsMultipleLevels_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange - Deeply nested extension structure
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Patient"",
            ""extension"": [{
                ""url"": ""http://example.org/ext/complex"",
                ""extension"": [{
                    ""url"": ""http://example.org/ext/nested"",
                    ""extension"": [{
                        ""url"": ""http://example.org/ext/leaf"",
                        ""valueString"": ""value""
                    }]
                }]
            }]
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new ExtensionStructureCheck("extension");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    #endregion

    #region Valid Scenarios - No Extensions

    [Fact]
    public void GivenNoExtensions_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange - Patient without extensions (extensions are optional)
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Patient"",
            ""name"": [{
                ""family"": ""Doe""
            }]
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new ExtensionStructureCheck("extension");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    #endregion

    #region Invalid Scenarios - Missing URL

    [Fact]
    public void GivenExtensionWithoutUrl_WhenValidating_ThenReturnsError()
    {
        // Arrange - Extension missing required 'url' property
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Patient"",
            ""extension"": [{
                ""valueString"": ""value without url""
            }]
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new ExtensionStructureCheck("extension");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
        Assert.Contains(result.Issues, i => i.Code == "ext-url-required");
        Assert.Contains("must have a 'url' property", result.Issues[0].Message);
    }

    [Fact]
    public void GivenMultipleExtensionsOneMissingUrl_WhenValidating_ThenReturnsError()
    {
        // Arrange - One valid extension, one missing url
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Patient"",
            ""extension"": [
                {
                    ""url"": ""http://example.org/ext/valid"",
                    ""valueString"": ""valid""
                },
                {
                    ""valueString"": ""missing url""
                }
            ]
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new ExtensionStructureCheck("extension");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
        Assert.Contains(result.Issues, i => i.Code == "ext-url-required");
    }

    #endregion

    #region Invalid Scenarios - Missing Content

    [Fact]
    public void GivenExtensionWithoutValueOrNestedExtensions_WhenValidating_ThenReturnsError()
    {
        // Arrange - Extension with only url (no content)
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Patient"",
            ""extension"": [{
                ""url"": ""http://example.org/ext/empty""
            }]
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new ExtensionStructureCheck("extension");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
        Assert.Contains(result.Issues, i => i.Code == "ext-content-required");
        Assert.Contains("must have either a value[x] property or nested 'extension' array", result.Issues[0].Message);
    }

    #endregion

    #region Invalid Scenarios - Both Value and Nested Extensions

    [Fact]
    public void GivenExtensionWithBothValueAndNestedExtensions_WhenValidating_ThenReturnsError()
    {
        // Arrange - Extension with BOTH value and nested extensions (invalid)
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Patient"",
            ""extension"": [{
                ""url"": ""http://example.org/ext/invalid"",
                ""valueString"": ""simple value"",
                ""extension"": [{
                    ""url"": ""http://example.org/ext/nested"",
                    ""valueString"": ""nested value""
                }]
            }]
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new ExtensionStructureCheck("extension");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
        Assert.Contains(result.Issues, i => i.Code == "ext-both-value-and-nested");
        Assert.Contains("cannot have both a value[x] property and nested 'extension' array", result.Issues[0].Message);
    }

    #endregion

    #region Multiple Errors

    [Fact]
    public void GivenMultipleInvalidExtensions_WhenValidating_ThenReturnsMultipleErrors()
    {
        // Arrange - Multiple extensions with various issues
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Patient"",
            ""extension"": [
                {
                    ""valueString"": ""no url""
                },
                {
                    ""url"": ""http://example.org/ext/empty""
                },
                {
                    ""url"": ""http://example.org/ext/both"",
                    ""valueString"": ""value"",
                    ""extension"": [{
                        ""url"": ""nested"",
                        ""valueString"": ""nested""
                    }]
                }
            ]
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new ExtensionStructureCheck("extension");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(3, result.Issues.Count); // One error per invalid extension
        Assert.Contains(result.Issues, i => i.Code == "ext-url-required");
        Assert.Contains(result.Issues, i => i.Code == "ext-content-required");
        Assert.Contains(result.Issues, i => i.Code == "ext-both-value-and-nested");
    }

    #endregion

    #region Real-World US Core Extensions

    [Fact]
    public void GivenUSCoreRaceExtension_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange - US Core race extension (complex)
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Patient"",
            ""extension"": [{
                ""url"": ""http://hl7.org/fhir/us/core/StructureDefinition/us-core-race"",
                ""extension"": [
                    {
                        ""url"": ""ombCategory"",
                        ""valueCoding"": {
                            ""system"": ""urn:oid:2.16.840.1.113883.6.238"",
                            ""code"": ""2106-3"",
                            ""display"": ""White""
                        }
                    },
                    {
                        ""url"": ""text"",
                        ""valueString"": ""White""
                    }
                ]
            }]
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new ExtensionStructureCheck("extension");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    #endregion
}
