// <copyright file="CodingStructureCheckTests.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Checks;
using Ignixa.Validation.Tests.TestHelpers;
using Xunit;

namespace Ignixa.Validation.Tests.Checks;

/// <summary>
/// Tests for CodingStructureCheck.
/// </summary>
public class CodingStructureCheckTests
{
    [Fact]
    public void GivenCodingWithAbsoluteSystem_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        var json = JsonNode.Parse(
            "{\"resourceType\":\"Patient\",\"maritalStatus\":{\"coding\":[{\"system\":\"http://terminology.hl7.org/CodeSystem/v3-MaritalStatus\",\"code\":\"M\"}]}}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new CodingStructureCheck("maritalStatus");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenCodingWithUrnSystem_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        var json = JsonNode.Parse(
            "{\"resourceType\":\"Patient\",\"maritalStatus\":{\"coding\":[{\"system\":\"urn:iso:std:iec:61883-6:amdl\",\"code\":\"M\"}]}}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new CodingStructureCheck("maritalStatus");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenCodingWithRelativeSystem_WhenValidating_ThenReturnsError()
    {
        // Arrange - This is the error from validate.fhir.org (line 152 of SearchDataBatch.json)
        var json = JsonNode.Parse(
            "{\"resourceType\":\"Encounter\",\"class\":{\"coding\":[{\"system\":\"system\",\"code\":\"code\"}]}}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new CodingStructureCheck("class");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
        Assert.Equal("coding-system-absolute", result.Issues[0].Code);
        Assert.Contains("must be an absolute reference", result.Issues[0].Message);
    }

    [Fact]
    public void GivenCodingWithoutSystemOrCode_WhenValidating_ThenReturnsWarning()
    {
        // Arrange - Coding with neither system nor code (only display)
        var json = JsonNode.Parse(
            "{\"resourceType\":\"Patient\",\"maritalStatus\":{\"coding\":[{\"display\":\"Married\"}]}}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new CodingStructureCheck("maritalStatus");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        // CodingStructureCheck returns failure if any issues exist (including warnings)
        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
        Assert.Equal("coding-1", result.Issues[0].Code);
        Assert.Equal(IssueSeverity.Warning, result.Issues[0].Severity);
    }

    [Fact]
    public void GivenCodeableConceptWithMultipleCodings_WhenValidating_ThenValidatesAllCodings()
    {
        // Arrange - Mix of valid and invalid codings
        var json = JsonNode.Parse(
            "{\"resourceType\":\"Patient\",\"maritalStatus\":{\"coding\":[{\"system\":\"http://terminology.hl7.org/CodeSystem/v3-MaritalStatus\",\"code\":\"M\"},{\"system\":\"localSystem\",\"code\":\"M\"}]}}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new CodingStructureCheck("maritalStatus");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Issues); // One error for the local system
        Assert.Equal("coding-system-absolute", result.Issues[0].Code);
    }

    [Fact]
    public void GivenCodeableConceptWithoutCoding_WhenValidating_ThenReturnsWarning()
    {
        // Arrange - CodeableConcept with only text (no coding array, no system or code)
        // This is treated as a bare Coding without system or code, which produces a warning
        var json = JsonNode.Parse(
            "{\"resourceType\":\"Patient\",\"maritalStatus\":{\"text\":\"Married\"}}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new CodingStructureCheck("maritalStatus");
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        // CodingStructureCheck treats missing coding array as a direct Coding
        // and warns if no system and code are present
        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
        Assert.Equal("coding-1", result.Issues[0].Code);
        Assert.Equal(IssueSeverity.Warning, result.Issues[0].Severity);
    }

    [Fact]
    public void GivenRelativeSystemUri_WhenValidatingAtCompatibilityDepth_ThenAccepts()
    {
        // Arrange - Encounter with relative system URI (non-absolute)
        var json = JsonNode.Parse(
            "{\"resourceType\":\"Encounter\",\"class\":{\"coding\":[{\"system\":\"local-system\",\"code\":\"test\"}]}}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new CodingStructureCheck("class");
        var settings = new ValidationSettings { Depth = ValidationDepth.Compatibility };
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenRelativeSystemUri_WhenValidatingAtSpecDepth_ThenRejects()
    {
        // Arrange - Encounter with relative system URI (non-absolute)
        var json = JsonNode.Parse(
            "{\"resourceType\":\"Encounter\",\"class\":{\"coding\":[{\"system\":\"local-system\",\"code\":\"test\"}]}}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var check = new CodingStructureCheck("class");
        var settings = new ValidationSettings { Depth = ValidationDepth.Spec };
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
        Assert.Contains(result.Issues, i => i.Code == "coding-system-absolute");
    }

    [Fact]
    public void GivenMedicationWithRelativeTagSystem_WhenValidatingAtCompatibilityDepth_ThenAccepts()
    {
        // Arrange - Medication with relative URI in meta.tag (common in Microsoft FHIR Server)
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Medication"",
            ""meta"": {
                ""tag"": [{
                    ""system"": ""internal-tags"",
                    ""code"": ""test-medication""
                }]
            },
            ""code"": {
                ""coding"": [{
                    ""system"": ""http://www.nlm.nih.gov/research/umls/rxnorm"",
                    ""code"": ""123456""
                }]
            }
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var element = sourceNode.ToElement(TestSchemaProvider.GetR4Schema());

        // Navigate to meta element
        var metaChildren = element.Children("meta");
        var meta = metaChildren[0];

        var check = new CodingStructureCheck("tag");
        var settings = new ValidationSettings { Depth = ValidationDepth.Compatibility };
        var state = new ValidationState();

        // Act
        var result = check.Validate(meta, settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }
}
