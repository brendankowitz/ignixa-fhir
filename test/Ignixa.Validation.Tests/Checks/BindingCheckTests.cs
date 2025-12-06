using Ignixa.Abstractions;
// <copyright file="BindingCheckTests.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

#pragma warning disable CA1861 // Avoid constant arrays as arguments

using System.Text.Json.Nodes;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Checks;
using Ignixa.Validation.Services;
using Ignixa.Validation.Tests.TestHelpers;
using Xunit;

namespace Ignixa.Validation.Tests.Checks;

/// <summary>
/// Tests for BindingCheck.
/// </summary>
public class BindingCheckTests
{
    #region Required Bindings - Valid

    [Fact]
    public void GivenRequiredBindingWithValidCode_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        var json = JsonNode.Parse("{\"resourceType\":\"Patient\",\"gender\":\"male\"}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var terminologyService = new InMemoryTerminologyService(FhirVersion.R4);
        var check = new BindingCheck(
            "gender",
            "http://hl7.org/fhir/ValueSet/administrative-gender",
            "Required",
            terminologyService);
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenRequiredBindingWithValidCoding_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        var json = JsonNode.Parse(@"{
            ""resourceType"":""Observation"",
            ""status"":""final""
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var terminologyService = new InMemoryTerminologyService(FhirVersion.R4);
        var check = new BindingCheck(
            "status",
            "http://hl7.org/fhir/ValueSet/observation-status",
            "Required",
            terminologyService);
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    #endregion

    #region Required Bindings - Invalid

    [Fact]
    public void GivenRequiredBindingWithInvalidCode_WhenValidating_ThenReturnsError()
    {
        // Arrange
        var json = JsonNode.Parse("{\"resourceType\":\"Patient\",\"gender\":\"invalid-gender\"}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var terminologyService = new InMemoryTerminologyService(FhirVersion.R4);
        var check = new BindingCheck(
            "gender",
            "http://hl7.org/fhir/ValueSet/administrative-gender",
            "Required",
            terminologyService);
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
        Assert.Contains(result.Issues, i => i.Code == "code-invalid");
    }

    #endregion

    #region Extensible Bindings - Not Validated

    [Fact]
    public void GivenExtensibleBinding_WhenValidating_ThenSkipsValidation()
    {
        // Arrange - even with invalid code, extensible bindings are not validated
        var json = JsonNode.Parse("{\"resourceType\":\"Patient\",\"gender\":\"custom-gender\"}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var terminologyService = new InMemoryTerminologyService(FhirVersion.R4);
        var check = new BindingCheck(
            "gender",
            "http://hl7.org/fhir/ValueSet/administrative-gender",
            "Extensible",
            terminologyService);
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GivenPreferredBinding_WhenValidating_ThenSkipsValidation()
    {
        // Arrange
        var json = JsonNode.Parse("{\"resourceType\":\"Patient\",\"gender\":\"anything\"}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var terminologyService = new InMemoryTerminologyService(FhirVersion.R4);
        var check = new BindingCheck(
            "gender",
            "http://hl7.org/fhir/ValueSet/administrative-gender",
            "Preferred",
            terminologyService);
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    #endregion

    #region CodeableConcept with Multiple Codings

    [Fact]
    public void GivenCodeableConceptWithValidCoding_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        var json = JsonNode.Parse(@"{
            ""resourceType"":""Observation"",
            ""code"":{
                ""coding"":[{
                    ""system"":""http://loinc.org"",
                    ""code"":""8302-2"",
                    ""display"":""Body height""
                }]
            }
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var terminologyService = new InMemoryTerminologyService(FhirVersion.R4);
        var check = new BindingCheck(
            "code",
            "http://custom.example.org/ValueSet/unknown-observation-codes", // Unknown ValueSet - will get warning
            "Required",
            terminologyService);
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert - should get warning (graceful degradation)
        Assert.True(result.IsValid);
        Assert.Single(result.Issues);
        Assert.Equal(IssueSeverity.Warning, result.Issues[0].Severity);
    }

    [Fact]
    public void GivenCodeableConceptWithOneValidCoding_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange - One valid coding (in-memory), one unknown
        var json = JsonNode.Parse(@"{
            ""resourceType"":""Patient"",
            ""gender"":{
                ""coding"":[
                    {""system"":""http://hl7.org/fhir/administrative-gender"",""code"":""male""},
                    {""system"":""http://custom.org/gender"",""code"":""custom""}
                ]
            }
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var terminologyService = new InMemoryTerminologyService(FhirVersion.R4);
        var check = new BindingCheck(
            "gender",
            "http://hl7.org/fhir/ValueSet/administrative-gender",
            "Required",
            terminologyService);
        var settings = new ValidationSettings();
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert - at least one coding is valid
        Assert.True(result.IsValid);
    }

    #endregion

    #region Graceful Degradation

    [Fact]
    public void GivenUnknownValueSet_WhenFailureModeIsWarning_ThenReturnsWarning()
    {
        // Arrange
        var json = JsonNode.Parse(@"{
            ""resourceType"":""Observation"",
            ""code"":{
                ""coding"":[{
                    ""system"":""http://loinc.org"",
                    ""code"":""8302-2""
                }]
            }
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var terminologyService = new InMemoryTerminologyService(FhirVersion.R4);
        var check = new BindingCheck(
            "code",
            "http://hl7.org/fhir/ValueSet/unknown-valueset",
            "Required",
            terminologyService);
        var settings = new ValidationSettings
        {
            TerminologyFailureMode = TerminologyFailureMode.Warning
        };
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.True(result.IsValid); // Warnings don't fail validation
        Assert.Single(result.Issues);
        Assert.Equal(IssueSeverity.Warning, result.Issues[0].Severity);
        Assert.Contains("unavailable", result.Issues[0].Message);
    }

    [Fact]
    public void GivenUnknownValueSet_WhenFailureModeIsError_ThenReturnsError()
    {
        // Arrange
        var json = JsonNode.Parse(@"{
            ""resourceType"":""Observation"",
            ""code"":{
                ""coding"":[{
                    ""system"":""http://loinc.org"",
                    ""code"":""8302-2""
                }]
            }
        }");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var terminologyService = new InMemoryTerminologyService(FhirVersion.R4);
        var check = new BindingCheck(
            "code",
            "http://hl7.org/fhir/ValueSet/unknown-valueset",
            "Required",
            terminologyService);
        var settings = new ValidationSettings
        {
            TerminologyFailureMode = TerminologyFailureMode.Error
        };
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.False(result.IsValid); // Error fails validation
        Assert.Single(result.Issues);
        Assert.Equal(IssueSeverity.Error, result.Issues[0].Severity);
    }

    #endregion

    #region Skip Terminology Validation

    [Fact]
    public void GivenSkipTerminologyValidation_WhenValidating_ThenSkipsCheck()
    {
        // Arrange
        var json = JsonNode.Parse("{\"resourceType\":\"Patient\",\"gender\":\"invalid\"}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var terminologyService = new InMemoryTerminologyService(FhirVersion.R4);
        var check = new BindingCheck(
            "gender",
            "http://hl7.org/fhir/ValueSet/administrative-gender",
            "Required",
            terminologyService);
        var settings = new ValidationSettings
        {
            SkipTerminologyValidation = true
        };
        var state = new ValidationState();

        // Act
        var result = check.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void GivenElementNotPresent_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange - gender element is not present
        var json = JsonNode.Parse("{\"resourceType\":\"Patient\",\"active\":true}");
        var sourceNode = JsonNodeSourceNode.Create(json);
        var terminologyService = new InMemoryTerminologyService(FhirVersion.R4);
        var check = new BindingCheck(
            "gender",
            "http://hl7.org/fhir/ValueSet/administrative-gender",
            "Required",
            terminologyService);
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
