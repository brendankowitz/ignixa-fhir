// <copyright file="ChoiceElementCheckTests.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

#pragma warning disable CA1861 // Prefer static readonly fields - not applicable for test code

using System.Text.Json.Nodes;
using Ignixa.SourceNodeSerialization.SourceNodes;
using Ignixa.Validation;
using Ignixa.Validation.Checks;
using Xunit;

namespace Ignixa.Validation.Tests.Checks;

/// <summary>
/// Tests for ChoiceElementCheck (value[x] validation).
/// </summary>
public class ChoiceElementCheckTests
{
    #region Valid Scenarios

    [Fact]
    public void GivenSingleChoiceVariant_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange - Observation with single valueQuantity
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Observation"",
            ""valueQuantity"": {
                ""value"": 120,
                ""unit"": ""mmHg""
            }
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new ChoiceElementCheck("value", new[] { "Quantity", "string", "boolean" });
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenNoChoiceVariant_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange - Observation without value[x] (optional element)
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Observation"",
            ""status"": ""final""
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new ChoiceElementCheck("value", new[] { "Quantity", "string" });
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenAllowedChoiceType_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange - Condition with onsetDateTime
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Condition"",
            ""onsetDateTime"": ""2024-01-15T10:00:00Z""
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new ChoiceElementCheck("onset", new[] { "dateTime", "Age", "Period", "Range", "string" });
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    #endregion

    #region Invalid Scenarios - Multiple Variants

    [Fact]
    public void GivenMultipleChoiceVariants_WhenValidating_ThenReturnsError()
    {
        // Arrange - Observation with BOTH valueQuantity AND valueString (invalid)
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Observation"",
            ""valueQuantity"": { ""value"": 120 },
            ""valueString"": ""text value""
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new ChoiceElementCheck("value", new[] { "Quantity", "string" });
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
        Assert.Contains(result.Issues, i => i.Code == "choice-multiple");
        Assert.Contains("valueQuantity", result.Issues[0].Message);
        Assert.Contains("valueString", result.Issues[0].Message);
    }

    [Fact]
    public void GivenThreeChoiceVariants_WhenValidating_ThenReturnsError()
    {
        // Arrange - Multiple choice variants
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Element"",
            ""valueString"": ""text"",
            ""valueCode"": ""code"",
            ""valueBoolean"": true
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new ChoiceElementCheck("value", new[] { "string", "Code", "boolean" });
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
        Assert.Contains(result.Issues, i => i.Code == "choice-multiple");
    }

    #endregion

    #region Invalid Scenarios - Disallowed Type

    [Fact]
    public void GivenDisallowedChoiceType_WhenValidating_ThenReturnsError()
    {
        // Arrange - Observation with valueCodeableConcept (not in allowed types)
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Observation"",
            ""valueCodeableConcept"": {
                ""coding"": [{
                    ""system"": ""http://example.org"",
                    ""code"": ""test""
                }]
            }
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new ChoiceElementCheck("value", new[] { "Quantity", "string" }); // CodeableConcept NOT allowed
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
        Assert.Contains(result.Issues, i => i.Code == "choice-invalid-type");
        Assert.Contains("CodeableConcept", result.Issues[0].Message);
        Assert.Contains("Quantity, string", result.Issues[0].Message); // Shows allowed types
    }

    [Fact]
    public void GivenUnexpectedChoiceType_WhenValidating_ThenReturnsError()
    {
        // Arrange - Custom element with unexpected type
        var json = JsonNode.Parse(@"{
            ""valueAttachment"": {
                ""contentType"": ""application/pdf""
            }
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new ChoiceElementCheck("value", new[] { "string", "integer", "boolean" });
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
        Assert.Contains(result.Issues, i => i.Code == "choice-invalid-type");
        Assert.Contains("Attachment", result.Issues[0].Message);
    }

    #endregion

    #region Type Name Normalization

    [Fact]
    public void GivenLowercaseTypeName_WhenValidating_ThenNormalizes()
    {
        // Arrange - Type name "string" (lowercase) should match "valueString"
        var json = JsonNode.Parse(@"{
            ""valueString"": ""test""
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new ChoiceElementCheck("value", new[] { "string" }); // lowercase
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenUppercaseTypeName_WhenValidating_ThenNormalizes()
    {
        // Arrange - Type name "String" (uppercase) should match "valueString"
        var json = JsonNode.Parse(@"{
            ""valueString"": ""test""
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new ChoiceElementCheck("value", new[] { "String" }); // uppercase
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    #endregion

    #region Real-World FHIR Scenarios

    [Fact]
    public void GivenObservationWithValidValue_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange - Real Observation with valueQuantity
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Observation"",
            ""status"": ""final"",
            ""code"": {
                ""coding"": [{
                    ""system"": ""http://loinc.org"",
                    ""code"": ""8867-4""
                }]
            },
            ""valueQuantity"": {
                ""value"": 98.6,
                ""unit"": ""degF"",
                ""system"": ""http://unitsofmeasure.org"",
                ""code"": ""[degF]""
            }
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new ChoiceElementCheck("value", new[] { "Quantity", "CodeableConcept", "string", "boolean", "integer", "Range", "Ratio", "SampledData", "time", "dateTime", "Period" });
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode, settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenConditionWithValidOnset_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange - Real Condition with onsetAge
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Condition"",
            ""code"": {
                ""coding"": [{
                    ""system"": ""http://snomed.info/sct"",
                    ""code"": ""38341003""
                }]
            },
            ""onsetAge"": {
                ""value"": 45,
                ""unit"": ""years"",
                ""system"": ""http://unitsofmeasure.org"",
                ""code"": ""a""
            }
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new ChoiceElementCheck("onset", new[] { "dateTime", "Age", "Period", "Range", "string" });
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
