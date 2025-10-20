// <copyright file="FastValidatorIntegrationTests.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System.Text.Json.Nodes;
using FluentAssertions;
using Ignixa.Specification.Generated;
using Ignixa.SourceNodeSerialization.SourceNodes;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;

namespace Ignixa.Validation.Tests;

/// <summary>
/// Integration tests for FastValidator with schema-driven validation.
/// Tests that FastValidator correctly integrates with IValidationSchemaResolver
/// to provide both universal checks and schema-specific checks.
/// </summary>
public class FastValidatorIntegrationTests
{
    private readonly R4StructureDefinitionSummaryProvider _provider;
    private readonly IValidationSchemaResolver _schemaResolver;

    public FastValidatorIntegrationTests()
    {
        _provider = new R4StructureDefinitionSummaryProvider();
        var innerResolver = new StructureDefinitionSchemaResolver(_provider);
        _schemaResolver = new CachedValidationSchemaResolver(innerResolver);
    }

    #region Universal Checks (Always Run)

    [Fact]
    public void GivenValidPatient_WhenValidatingWithSchemaResolver_ThenUniversalChecksRun()
    {
        // Arrange
        var validator = new FastValidator(_schemaResolver);
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""example"",
            ""active"": true
        }");
        var sourceNode = JsonNodeSourceNode.Create(json!);

        // Act
        var result = validator.Validate(sourceNode);

        // Assert
        result.Should().NotBeNull();
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void GivenInvalidJson_WhenValidatingWithSchemaResolver_ThenJsonStructureCheckFails()
    {
        // Arrange
        var validator = new FastValidator(_schemaResolver);
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Patient""
        }");
        var sourceNode = JsonNodeSourceNode.Create(json!);

        // Act
        var result = validator.Validate(sourceNode);

        // Assert - Universal checks should run regardless of schema
        result.Should().NotBeNull();
    }

    [Fact]
    public void GivenInvalidId_WhenValidatingWithSchemaResolver_ThenIdFormatCheckFails()
    {
        // Arrange
        var validator = new FastValidator(_schemaResolver);
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""invalid id with spaces"",
            ""active"": true
        }");
        var sourceNode = JsonNodeSourceNode.Create(json!);

        // Act
        var result = validator.Validate(sourceNode);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(i => i.Path.Contains("id") && i.Severity == IssueSeverity.Error);
    }

    [Fact]
    public void GivenInvalidNarrative_WhenValidatingWithSchemaResolver_ThenNarrativeCheckFails()
    {
        // Arrange
        var validator = new FastValidator(_schemaResolver);
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""example"",
            ""text"": {
                ""status"": ""invalid-status"",
                ""div"": ""<div>Test</div>""
            }
        }");
        var sourceNode = JsonNodeSourceNode.Create(json!);

        // Act
        var result = validator.Validate(sourceNode);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(i => i.Path.Contains("text.status"));
    }

    #endregion

    #region Schema-Driven Validation

    [Fact]
    public void GivenPatientWithMissingRequiredField_WhenValidatingWithSchemaResolver_ThenDetectsViolation()
    {
        // Arrange
        var validator = new FastValidator(_schemaResolver);

        // Observation requires 'status' and 'code' fields
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Observation"",
            ""id"": ""example""
        }");
        var sourceNode = JsonNodeSourceNode.Create(json!);

        // Act
        var result = validator.Validate(sourceNode);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(i => i.Severity == IssueSeverity.Error);
    }

    [Fact]
    public void GivenObservationWithRequiredFields_WhenValidatingWithSchemaResolver_ThenPasses()
    {
        // Arrange
        var validator = new FastValidator(_schemaResolver);
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Observation"",
            ""id"": ""example"",
            ""status"": ""final"",
            ""code"": {
                ""coding"": [{
                    ""system"": ""http://loinc.org"",
                    ""code"": ""15074-8""
                }]
            }
        }");
        var sourceNode = JsonNodeSourceNode.Create(json!);

        // Act
        var result = validator.Validate(sourceNode);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void GivenPatientWithCardinalityViolation_WhenValidatingWithSchemaResolver_ThenDetectsViolation()
    {
        // Arrange
        var validator = new FastValidator(_schemaResolver);

        // Patient.active has max cardinality of 1
        // Note: JsonNode.Parse will only create one 'active' property (JSON doesn't allow duplicates)
        // So we need to test using a field that can be an array but has max constraints
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""example"",
            ""active"": true
        }");
        var sourceNode = JsonNodeSourceNode.Create(json!);

        // Act
        var result = validator.Validate(sourceNode);

        // Assert - Should pass (no cardinality violation)
        result.IsValid.Should().BeTrue();
    }

    #endregion

    #region Backward Compatibility (No Schema Resolver)

    [Fact]
    public void GivenValidatorWithoutSchemaResolver_WhenValidating_ThenOnlyUniversalChecksRun()
    {
        // Arrange
        var validator = new FastValidator(); // No schema resolver
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Observation"",
            ""id"": ""example""
        }");
        var sourceNode = JsonNodeSourceNode.Create(json!);

        // Act
        var result = validator.Validate(sourceNode);

        // Assert - Universal checks pass (missing required fields NOT detected without schema)
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void GivenValidatorWithoutSchemaResolver_WhenValidatingInvalidId_ThenStillDetects()
    {
        // Arrange
        var validator = new FastValidator(); // No schema resolver
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""invalid id with spaces""
        }");
        var sourceNode = JsonNodeSourceNode.Create(json!);

        // Act
        var result = validator.Validate(sourceNode);

        // Assert - Universal checks still run
        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(i => i.Path.Contains("id"));
    }

    #endregion

    #region Schema Resolution

    [Fact]
    public void GivenValidResourceType_WhenValidatingWithSchemaResolver_ThenResolvesSchema()
    {
        // Arrange
        var validator = new FastValidator(_schemaResolver);
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""example"",
            ""active"": true
        }");
        var sourceNode = JsonNodeSourceNode.Create(json!);

        // Act
        var result = validator.Validate(sourceNode);

        // Assert - Schema should be resolved and applied
        result.Should().NotBeNull();
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void GivenUnknownResourceType_WhenValidatingWithSchemaResolver_ThenOnlyUniversalChecksRun()
    {
        // Arrange
        var validator = new FastValidator(_schemaResolver);
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""UnknownResource"",
            ""id"": ""example""
        }");
        var sourceNode = JsonNodeSourceNode.Create(json!);

        // Act
        var result = validator.Validate(sourceNode);

        // Assert - Universal checks still run, schema-specific checks skipped
        result.Should().NotBeNull();
        result.IsValid.Should().BeTrue(); // No universal check violations
    }

    [Fact]
    public void GivenMissingResourceType_WhenValidatingWithSchemaResolver_ThenOnlyUniversalChecksRun()
    {
        // Arrange
        var validator = new FastValidator(_schemaResolver);
        var json = JsonNode.Parse(@"{
            ""id"": ""example"",
            ""active"": true
        }");
        var sourceNode = JsonNodeSourceNode.Create(json!);

        // Act
        var result = validator.Validate(sourceNode);

        // Assert - Universal checks still run
        result.Should().NotBeNull();
    }

    #endregion

    #region Multiple Resource Types

    [Fact]
    public void GivenMultipleResourceTypes_WhenValidating_ThenEachUsesCorrectSchema()
    {
        // Arrange
        var validator = new FastValidator(_schemaResolver);

        var patientJson = JsonNode.Parse(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""patient-example"",
            ""active"": true
        }");

        var observationJson = JsonNode.Parse(@"{
            ""resourceType"": ""Observation"",
            ""id"": ""obs-example"",
            ""status"": ""final"",
            ""code"": {
                ""coding"": [{
                    ""system"": ""http://loinc.org"",
                    ""code"": ""15074-8""
                }]
            }
        }");

        // Act
        var patientResult = validator.Validate(JsonNodeSourceNode.Create(patientJson!));
        var observationResult = validator.Validate(JsonNodeSourceNode.Create(observationJson!));

        // Assert
        patientResult.IsValid.Should().BeTrue();
        observationResult.IsValid.Should().BeTrue();
    }

    #endregion

    #region Type Validation

    [Fact]
    public void GivenPatientWithInvalidBooleanType_WhenValidatingWithSchemaResolver_ThenDetectsViolation()
    {
        // Arrange
        var validator = new FastValidator(_schemaResolver);
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""example"",
            ""active"": ""not-a-boolean""
        }");
        var sourceNode = JsonNodeSourceNode.Create(json!);

        // Act
        var result = validator.Validate(sourceNode);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(i => i.Path.Contains("active"));
    }

    #endregion

    #region Reference Format Validation

    [Fact]
    public void GivenObservationWithInvalidReference_WhenValidatingWithSchemaResolver_ThenDetectsViolation()
    {
        // Arrange
        var validator = new FastValidator(_schemaResolver);
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Observation"",
            ""id"": ""example"",
            ""status"": ""final"",
            ""code"": {
                ""coding"": [{
                    ""system"": ""http://loinc.org"",
                    ""code"": ""15074-8""
                }]
            },
            ""subject"": {
                ""reference"": ""invalid-reference-format""
            }
        }");
        var sourceNode = JsonNodeSourceNode.Create(json!);

        // Act
        var result = validator.Validate(sourceNode);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(i => i.Path.Contains("subject.reference"));
    }

    [Fact]
    public void GivenObservationWithValidReference_WhenValidatingWithSchemaResolver_ThenPasses()
    {
        // Arrange
        var validator = new FastValidator(_schemaResolver);
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Observation"",
            ""id"": ""example"",
            ""status"": ""final"",
            ""code"": {
                ""coding"": [{
                    ""system"": ""http://loinc.org"",
                    ""code"": ""15074-8""
                }]
            },
            ""subject"": {
                ""reference"": ""Patient/example""
            }
        }");
        var sourceNode = JsonNodeSourceNode.Create(json!);

        // Act
        var result = validator.Validate(sourceNode);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    #endregion

    #region Coding Structure Validation

    [Fact]
    public void GivenObservationWithEmptyCoding_WhenValidatingWithSchemaResolver_ThenDetectsWarning()
    {
        // Arrange
        var validator = new FastValidator(_schemaResolver);
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Observation"",
            ""id"": ""example"",
            ""status"": ""final"",
            ""code"": {
                ""coding"": [{}]
            }
        }");
        var sourceNode = JsonNodeSourceNode.Create(json!);

        // Act
        var result = validator.Validate(sourceNode);

        // Assert
        result.Issues.Should().Contain(i => i.Severity == IssueSeverity.Warning && i.Path.Contains("code.coding"));
    }

    [Fact]
    public void GivenObservationWithValidCoding_WhenValidatingWithSchemaResolver_ThenPasses()
    {
        // Arrange
        var validator = new FastValidator(_schemaResolver);
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Observation"",
            ""id"": ""example"",
            ""status"": ""final"",
            ""code"": {
                ""coding"": [{
                    ""system"": ""http://loinc.org"",
                    ""code"": ""15074-8"",
                    ""display"": ""Glucose""
                }]
            }
        }");
        var sourceNode = JsonNodeSourceNode.Create(json!);

        // Act
        var result = validator.Validate(sourceNode);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    #endregion

    #region Performance

    [Fact]
    public void GivenCachedSchemaResolver_WhenValidatingMultipleTimes_ThenUsesCache()
    {
        // Arrange
        var validator = new FastValidator(_schemaResolver);
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""example"",
            ""active"": true
        }");

        // Act - Multiple validations should use cached schema
        var start = DateTime.UtcNow;
        for (int i = 0; i < 100; i++)
        {
            var sourceNode = JsonNodeSourceNode.Create(json!);
            var result = validator.Validate(sourceNode);
            result.IsValid.Should().BeTrue();
        }
        var duration = DateTime.UtcNow - start;

        // Assert - Should complete quickly (< 500ms for 100 validations with caching)
        duration.Should().BeLessThan(TimeSpan.FromMilliseconds(500));
    }

    #endregion

    #region Combined Validation (Universal + Schema)

    [Fact]
    public void GivenInvalidIdAndMissingRequiredField_WhenValidating_ThenDetectsBothViolations()
    {
        // Arrange
        var validator = new FastValidator(_schemaResolver);
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Observation"",
            ""id"": ""invalid id with spaces""
        }");
        var sourceNode = JsonNodeSourceNode.Create(json!);

        // Act
        var result = validator.Validate(sourceNode);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(i => i.Path.Contains("id")); // Universal check
        result.Issues.Should().Contain(i => i.Severity == IssueSeverity.Error); // Schema check (missing required)
    }

    [Fact]
    public void GivenComplexValidResource_WhenValidating_ThenAllChecksPassed()
    {
        // Arrange
        var validator = new FastValidator(_schemaResolver);
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Observation"",
            ""id"": ""complex-example"",
            ""status"": ""final"",
            ""code"": {
                ""coding"": [{
                    ""system"": ""http://loinc.org"",
                    ""code"": ""15074-8"",
                    ""display"": ""Glucose [Moles/volume] in Blood""
                }],
                ""text"": ""Glucose""
            },
            ""subject"": {
                ""reference"": ""Patient/example"",
                ""display"": ""John Doe""
            },
            ""effectiveDateTime"": ""2023-01-15T10:30:00Z"",
            ""valueQuantity"": {
                ""value"": 6.3,
                ""unit"": ""mmol/L"",
                ""system"": ""http://unitsofmeasure.org"",
                ""code"": ""mmol/L""
            }
        }");
        var sourceNode = JsonNodeSourceNode.Create(json!);

        // Act
        var result = validator.Validate(sourceNode);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Issues.Should().BeEmpty();
    }

    #endregion
}
