// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Shouldly;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;
using Ignixa.Validation.Tests.TestHelpers;

namespace Ignixa.Validation.Tests;

/// <summary>
/// Tests for validating FHIR resources with contained resources.
/// Ensures that contained resources are validated against their own StructureDefinition,
/// not the parent resource's schema.
/// </summary>
public class ContainedResourceValidationTests
{
    private readonly IValidationSchemaResolver _schemaResolver;

    public ContainedResourceValidationTests()
    {
        var schema = new R4CoreSchemaProvider();
        var innerResolver = new StructureDefinitionSchemaResolver(schema);
        _schemaResolver = new CachedValidationSchemaResolver(innerResolver);
    }

    private ValidationResult ValidateResource(string resourceJson, ValidationDepth depth = ValidationDepth.Spec)
    {
        var json = JsonNode.Parse(resourceJson);
        var sourceNode = JsonNodeSourceNode.Create(json!);
        var resourceType = sourceNode.ResourceType ?? sourceNode.Name;
        var schema = _schemaResolver.GetSchema(resourceType);

        if (schema == null)
        {
            throw new InvalidOperationException($"Schema not found for {resourceType}");
        }

        var settings = new ValidationSettings { Depth = depth };
        var state = new ValidationState();
        return schema.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);
    }

    #region Valid Contained Resources

    [Fact]
    public void GivenObservationWithContainedPatient_WhenValidating_ThenShouldPass()
    {
        // Arrange
        var observationJson = @"{
            ""resourceType"": ""Observation"",
            ""id"": ""example"",
            ""contained"": [
                {
                    ""resourceType"": ""Patient"",
                    ""id"": ""patient-1"",
                    ""identifier"": [{
                        ""system"": ""http://example.org/mrn"",
                        ""value"": ""12345""
                    }],
                    ""active"": true,
                    ""name"": [{
                        ""family"": ""Doe"",
                        ""given"": [""John""]
                    }]
                }
            ],
            ""status"": ""final"",
            ""code"": {
                ""coding"": [{
                    ""system"": ""http://loinc.org"",
                    ""code"": ""15074-8""
                }]
            },
            ""subject"": {
                ""reference"": ""#patient-1""
            }
        }";

        // Act
        var result = ValidateResource(observationJson);

        // Assert
        result.IsValid.ShouldBeTrue($"Observation with contained Patient should pass validation. Issues: {string.Join(", ", result.Issues.Select(i => $"{i.Path}: {i.Message}"))}");
    }

    [Fact]
    public void GivenObservationWithContainedOrganization_WhenValidating_ThenShouldPass()
    {
        // Arrange
        var observationJson = @"{
            ""resourceType"": ""Observation"",
            ""id"": ""example"",
            ""contained"": [
                {
                    ""resourceType"": ""Organization"",
                    ""id"": ""org-1"",
                    ""name"": ""Acme Laboratory"",
                    ""telecom"": [{
                        ""system"": ""phone"",
                        ""value"": ""555-1234""
                    }]
                }
            ],
            ""status"": ""final"",
            ""code"": {
                ""coding"": [{
                    ""system"": ""http://loinc.org"",
                    ""code"": ""15074-8""
                }]
            },
            ""performer"": [{
                ""reference"": ""#org-1""
            }]
        }";

        // Act
        var result = ValidateResource(observationJson);

        // Assert
        result.IsValid.ShouldBeTrue($"Observation with contained Organization should pass validation. Issues: {string.Join(", ", result.Issues.Select(i => $"{i.Path}: {i.Message}"))}");
    }

    [Fact]
    public void GivenObservationWithMultipleContainedResources_WhenValidating_ThenShouldPass()
    {
        // Arrange
        var observationJson = @"{
            ""resourceType"": ""Observation"",
            ""id"": ""example"",
            ""contained"": [
                {
                    ""resourceType"": ""Patient"",
                    ""id"": ""patient-1"",
                    ""active"": true,
                    ""name"": [{
                        ""family"": ""Smith""
                    }]
                },
                {
                    ""resourceType"": ""Practitioner"",
                    ""id"": ""practitioner-1"",
                    ""active"": true,
                    ""name"": [{
                        ""family"": ""Jones""
                    }]
                }
            ],
            ""status"": ""final"",
            ""code"": {
                ""coding"": [{
                    ""system"": ""http://loinc.org"",
                    ""code"": ""15074-8""
                }]
            },
            ""subject"": {
                ""reference"": ""#patient-1""
            },
            ""performer"": [{
                ""reference"": ""#practitioner-1""
            }]
        }";

        // Act
        var result = ValidateResource(observationJson);

        // Assert
        result.IsValid.ShouldBeTrue($"Observation with multiple contained resources should pass validation. Issues: {string.Join(", ", result.Issues.Select(i => $"{i.Path}: {i.Message}"))}");
    }

    #endregion

    #region Invalid Contained Resources - Schema Violations

    [Fact]
    public void GivenContainedPatientWithInvalidIdentifier_WhenValidating_ThenShouldFailWithCorrectPath()
    {
        // Arrange - identifier with unknown child property should fail validation
        // Note: Identifier.value is 0..1 (optional), so we test with an unknown property instead
        var observationJson = @"{
            ""resourceType"": ""Observation"",
            ""id"": ""example"",
            ""contained"": [
                {
                    ""resourceType"": ""Patient"",
                    ""id"": ""patient-1"",
                    ""identifier"": [{
                        ""system"": ""http://example.org/mrn"",
                        ""invalidProperty"": ""should fail""
                    }],
                    ""active"": true
                }
            ],
            ""status"": ""final"",
            ""code"": {
                ""coding"": [{
                    ""system"": ""http://loinc.org"",
                    ""code"": ""15074-8""
                }]
            }
        }";

        // Act
        var result = ValidateResource(observationJson);

        // Assert
        result.IsValid.ShouldBeFalse("Contained Patient with invalid identifier property should fail validation");
        result.Issues.ShouldContain(i => i.Path.Contains("contained") && i.Path.Contains("identifier"));
    }

    [Fact]
    public void GivenContainedPatientWithUnknownProperty_WhenValidating_ThenShouldFailWithCorrectResourceType()
    {
        // Arrange - "unknownField" is not a valid Patient property
        var observationJson = @"{
            ""resourceType"": ""Observation"",
            ""id"": ""example"",
            ""contained"": [
                {
                    ""resourceType"": ""Patient"",
                    ""id"": ""patient-1"",
                    ""active"": true,
                    ""unknownField"": ""should fail""
                }
            ],
            ""status"": ""final"",
            ""code"": {
                ""coding"": [{
                    ""system"": ""http://loinc.org"",
                    ""code"": ""15074-8""
                }]
            }
        }";

        // Act
        var result = ValidateResource(observationJson);

        // Assert
        result.IsValid.ShouldBeFalse("Contained Patient with unknown property should fail validation");
        result.Issues.ShouldContain(i =>
            i.Message.Contains("unknownField") &&
            i.Message.Contains("Patient"),
            $"Error should reference Patient StructureDefinition, not Observation. Issues: {string.Join(", ", result.Issues.Select(i => $"{i.Path}: {i.Message}"))}");
    }

    [Fact]
    public void GivenContainedOrganizationWithInvalidTelecom_WhenValidating_ThenShouldFailWithCorrectPath()
    {
        // Arrange - telecom with unknown child property should fail validation
        // Note: ContactPoint.system is 0..1 (optional), so we test with an unknown property instead
        var observationJson = @"{
            ""resourceType"": ""Observation"",
            ""id"": ""example"",
            ""contained"": [
                {
                    ""resourceType"": ""Organization"",
                    ""id"": ""org-1"",
                    ""name"": ""Test Lab"",
                    ""telecom"": [{
                        ""value"": ""555-1234"",
                        ""invalidProperty"": ""should fail""
                    }]
                }
            ],
            ""status"": ""final"",
            ""code"": {
                ""coding"": [{
                    ""system"": ""http://loinc.org"",
                    ""code"": ""15074-8""
                }]
            }
        }";

        // Act
        var result = ValidateResource(observationJson);

        // Assert
        result.IsValid.ShouldBeFalse("Contained Organization with invalid telecom property should fail validation");
        result.Issues.ShouldContain(i => i.Path.Contains("contained") && i.Path.Contains("telecom"));
    }

    #endregion

    #region Contained Resources - Different Resource Types

    [Fact]
    public void GivenDiagnosticReportWithContainedObservation_WhenValidating_ThenShouldPass()
    {
        // Arrange
        var reportJson = @"{
            ""resourceType"": ""DiagnosticReport"",
            ""id"": ""example"",
            ""contained"": [
                {
                    ""resourceType"": ""Observation"",
                    ""id"": ""obs-1"",
                    ""status"": ""final"",
                    ""code"": {
                        ""coding"": [{
                            ""system"": ""http://loinc.org"",
                            ""code"": ""15074-8""
                        }]
                    }
                }
            ],
            ""status"": ""final"",
            ""code"": {
                ""coding"": [{
                    ""system"": ""http://loinc.org"",
                    ""code"": ""24323-8""
                }]
            },
            ""result"": [{
                ""reference"": ""#obs-1""
            }]
        }";

        // Act
        var result = ValidateResource(reportJson);

        // Assert
        result.IsValid.ShouldBeTrue($"DiagnosticReport with contained Observation should pass validation. Issues: {string.Join(", ", result.Issues.Select(i => $"{i.Path}: {i.Message}"))}");
    }

    [Fact]
    public void GivenMedicationRequestWithContainedMedication_WhenValidating_ThenShouldPass()
    {
        // Arrange
        var requestJson = @"{
            ""resourceType"": ""MedicationRequest"",
            ""id"": ""example"",
            ""contained"": [
                {
                    ""resourceType"": ""Medication"",
                    ""id"": ""med-1"",
                    ""code"": {
                        ""coding"": [{
                            ""system"": ""http://www.nlm.nih.gov/research/umls/rxnorm"",
                            ""code"": ""582620""
                        }]
                    }
                }
            ],
            ""status"": ""active"",
            ""intent"": ""order"",
            ""medicationReference"": {
                ""reference"": ""#med-1""
            },
            ""subject"": {
                ""reference"": ""Patient/example""
            }
        }";

        // Act
        var result = ValidateResource(requestJson);

        // Assert
        result.IsValid.ShouldBeTrue($"MedicationRequest with contained Medication should pass validation. Issues: {string.Join(", ", result.Issues.Select(i => $"{i.Path}: {i.Message}"))}");
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void GivenResourceWithEmptyContainedArray_WhenValidating_ThenShouldPass()
    {
        // Arrange
        var observationJson = @"{
            ""resourceType"": ""Observation"",
            ""id"": ""example"",
            ""contained"": [],
            ""status"": ""final"",
            ""code"": {
                ""coding"": [{
                    ""system"": ""http://loinc.org"",
                    ""code"": ""15074-8""
                }]
            }
        }";

        // Act
        var result = ValidateResource(observationJson);

        // Assert
        result.IsValid.ShouldBeTrue($"Observation with empty contained array should pass validation. Issues: {string.Join(", ", result.Issues.Select(i => $"{i.Path}: {i.Message}"))}");
    }

    [Fact]
    public void GivenContainedResourceWithMissingResourceType_WhenValidating_ThenShouldFail()
    {
        // Arrange
        var observationJson = @"{
            ""resourceType"": ""Observation"",
            ""id"": ""example"",
            ""contained"": [
                {
                    ""id"": ""patient-1"",
                    ""active"": true
                }
            ],
            ""status"": ""final"",
            ""code"": {
                ""coding"": [{
                    ""system"": ""http://loinc.org"",
                    ""code"": ""15074-8""
                }]
            }
        }";

        // Act
        var result = ValidateResource(observationJson);

        // Assert
        result.IsValid.ShouldBeFalse("Contained resource without resourceType should fail validation");
        result.Issues.ShouldContain(i => i.Path.Contains("contained"));
    }

    [Fact]
    public void GivenContainedResourceWithInvalidResourceType_WhenValidating_ThenShouldFail()
    {
        // Arrange
        var observationJson = @"{
            ""resourceType"": ""Observation"",
            ""id"": ""example"",
            ""contained"": [
                {
                    ""resourceType"": ""InvalidResourceType"",
                    ""id"": ""invalid-1""
                }
            ],
            ""status"": ""final"",
            ""code"": {
                ""coding"": [{
                    ""system"": ""http://loinc.org"",
                    ""code"": ""15074-8""
                }]
            }
        }";

        // Act
        var result = ValidateResource(observationJson);

        // Assert
        result.IsValid.ShouldBeFalse("Contained resource with invalid resourceType should fail validation");
    }

    #endregion

    #region Nested Contained Resources

    [Fact]
    public void GivenContainedPatientWithComplexExtension_WhenValidating_ThenShouldPass()
    {
        // Arrange
        var observationJson = @"{
            ""resourceType"": ""Observation"",
            ""id"": ""example"",
            ""contained"": [
                {
                    ""resourceType"": ""Patient"",
                    ""id"": ""patient-1"",
                    ""active"": true,
                    ""extension"": [{
                        ""url"": ""http://example.org/fhir/StructureDefinition/patient-importance"",
                        ""valueCodeableConcept"": {
                            ""coding"": [{
                                ""system"": ""http://example.org/importance"",
                                ""code"": ""high""
                            }]
                        }
                    }]
                }
            ],
            ""status"": ""final"",
            ""code"": {
                ""coding"": [{
                    ""system"": ""http://loinc.org"",
                    ""code"": ""15074-8""
                }]
            }
        }";

        // Act
        var result = ValidateResource(observationJson);

        // Assert
        result.IsValid.ShouldBeTrue($"Observation with contained Patient with extension should pass validation. Issues: {string.Join(", ", result.Issues.Select(i => $"{i.Path}: {i.Message}"))}");
    }

    #endregion

    #region Bug Reproduction - Original Issue

    [Fact]
    public void GivenContainedPatientPropertiesValidatedAgainstObservation_WhenValidating_ThenShouldNotReportObservationSchemaErrors()
    {
        // Arrange - This reproduces the original bug where contained Patient properties
        // (identifier, active) were incorrectly validated against Observation schema
        var observationJson = @"{
            ""resourceType"": ""Observation"",
            ""id"": ""example"",
            ""contained"": [
                {
                    ""resourceType"": ""Patient"",
                    ""id"": ""patient-1"",
                    ""identifier"": [{
                        ""system"": ""http://example.org/mrn"",
                        ""value"": ""12345""
                    }],
                    ""active"": true,
                    ""name"": [{
                        ""family"": ""Test""
                    }]
                }
            ],
            ""status"": ""final"",
            ""code"": {
                ""coding"": [{
                    ""system"": ""http://loinc.org"",
                    ""code"": ""15074-8""
                }]
            }
        }";

        // Act
        var result = ValidateResource(observationJson);

        // Assert
        // The bug would show errors like:
        // "Property 'identifier' is not defined in the FHIR StructureDefinition for this resource type"
        // "Property 'active' is not defined in the FHIR StructureDefinition for this resource type"
        // These errors incorrectly reference Observation schema instead of Patient schema
        result.Issues.ShouldNotContain(i =>
            i.Message.Contains("identifier") &&
            i.Message.Contains("not defined") &&
            i.Path.Contains("contained[0].identifier"),
            "Should not report 'identifier' as unknown property - it's valid for Patient");

        result.Issues.ShouldNotContain(i =>
            i.Message.Contains("active") &&
            i.Message.Contains("not defined") &&
            i.Path.Contains("contained[0].active"),
            "Should not report 'active' as unknown property - it's valid for Patient");
    }

    #endregion
}
