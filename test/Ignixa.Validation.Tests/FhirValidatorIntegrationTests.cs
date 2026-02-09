// <copyright file="FastValidatorIntegrationTests.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System.Text.Json.Nodes;
using Shouldly;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Generated;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;
using Ignixa.Validation.Tests.TestHelpers;

namespace Ignixa.Validation.Tests;

/// <summary>
/// Integration tests for ValidationSchema with tier-aware validation.
/// Tests that ValidationSchema correctly executes tier-appropriate checks
/// (Fast = universal, Spec = universal + schema, Profile = all checks).
/// </summary>
public class FhirValidatorIntegrationTests
{
    private readonly ISchema _schema;
    private readonly IValidationSchemaResolver _schemaResolver;

    public FhirValidatorIntegrationTests()
    {
        _schema = new R4CoreSchemaProvider();
        var innerResolver = new StructureDefinitionSchemaResolver(_schema);
        _schemaResolver = new CachedValidationSchemaResolver(innerResolver);
    }

    private ValidationResult ValidateResource(string resourceJson, ValidationDepth depth = ValidationDepth.Spec)
    {
        var json = JsonNode.Parse(resourceJson);
        var sourceNode = JsonNodeSourceNode.Create(json!);
        var resourceType = sourceNode.ResourceType ?? sourceNode.Name;
        var canonicalUrl = $"http://hl7.org/fhir/StructureDefinition/{resourceType}";
        var schema = _schemaResolver.GetSchema(canonicalUrl);

        if (schema == null)
        {
            throw new InvalidOperationException($"Schema not found for {resourceType}");
        }

        var settings = new ValidationSettings { Depth = depth };
        var state = new ValidationState();
        return schema.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, state);
    }

    #region Tier-Aware Validation

    [Fact]
    public void GivenValidPatient_WhenValidatingWithTierNone_ThenSkipsValidation()
    {
        // Arrange & Act
        var result = ValidateResource(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""example"",
            ""active"": true
        }", ValidationDepth.Minimal);

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenInvalidId_WhenValidatingWithTierFast_ThenDetectsUniversalCheckViolation()
    {
        // Arrange & Act
        var result = ValidateResource(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""invalid id with spaces"",
            ""active"": true
        }", ValidationDepth.Minimal);

        // Assert - Fast tier runs universal checks (IdFormat)
        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Path.Contains("id"));
    }

    [Fact]
    public void GivenMissingRequiredField_WhenValidatingWithTierSpec_ThenDetectsSchemaViolation()
    {
        // Arrange & Act - Observation requires 'status' and 'code'
        var result = ValidateResource(@"{
            ""resourceType"": ""Observation"",
            ""id"": ""example""
        }", ValidationDepth.Spec);

        // Assert - Spec tier runs universal + schema checks
        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Severity == IssueSeverity.Error);
    }

    [Fact]
    public void GivenValidObservation_WhenValidatingWithTierSpec_ThenPasses()
    {
        // Arrange & Act - Valid Observation with required fields
        var result = ValidateResource(@"{
            ""resourceType"": ""Observation"",
            ""id"": ""example"",
            ""status"": ""final"",
            ""code"": {
                ""coding"": [{
                    ""system"": ""http://loinc.org"",
                    ""code"": ""8310-5""
                }]
            }
        }");

        // Assert
        result.IsValid.ShouldBeTrue();
    }

    #endregion

    #region Universal Checks

    [Fact]
    public void GivenInvalidNarrative_WhenValidating_ThenDetectsNarrativeCheckFailure()
    {
        // Arrange & Act
        var result = ValidateResource(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""example"",
            ""text"": {
                ""status"": ""invalid-status"",
                ""div"": ""<div>Test</div>""
            }
        }");

        // Assert
        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Path.Contains("text.status"));
    }

    [Fact]
    public void GivenObservationWithValidNarrative_WhenValidating_ThenShouldPass()
    {
        // Arrange & Act - This test demonstrates the xhtml validation bug
        // The Observation has a valid text.div element with proper XHTML content
        // According to FHIR R4 spec, xhtml primitive stores content directly (not in a .value child)
        // Bug: StructureDefinitionSchemaBuilder creates CardinalityCheck for div.value
        // Result: Validation incorrectly fails with "Observation.text.div.value must have at least 1 occurrence(s), but found 0"
        var result = ValidateResource(@"{
            ""resourceType"": ""Observation"",
            ""id"": ""example"",
            ""status"": ""final"",
            ""code"": {
                ""coding"": [{
                    ""system"": ""http://loinc.org"",
                    ""code"": ""15074-8"",
                    ""display"": ""Glucose [Moles/volume] in Blood""
                }]
            },
            ""text"": {
                ""status"": ""generated"",
                ""div"": ""<div xmlns=\""http://www.w3.org/1999/xhtml\""><p><b>Generated Narrative</b></p></div>""
            }
        }");

        // Assert - Should pass because text.div is present and valid
        // Currently fails due to bug in StructureDefinitionSchemaBuilder
        result.IsValid.ShouldBeTrue($"Observation with valid narrative should pass validation. Issues: {string.Join(", ", result.Issues.Select(i => i.Message))}");
    }

    #endregion

    #region Schema-Driven Checks

    [Fact]
    public void GivenBareStringReference_WhenValidating_ThenPasses()
    {
        // Arrange & Act - Bug #210-5: Bare string references should pass (MS FHIR Server behavior)
        var result = ValidateResource(@"{
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
                ""reference"": ""ijk""
            }
        }");

        // Assert
        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void GivenValidReference_WhenValidating_ThenPasses()
    {
        // Arrange & Act - Valid Observation with reference
        var result = ValidateResource(@"{
            ""resourceType"": ""Observation"",
            ""id"": ""example"",
            ""status"": ""final"",
            ""code"": {
                ""coding"": [{
                    ""system"": ""http://loinc.org"",
                    ""code"": ""8310-5""
                }]
            },
            ""subject"": {
                ""reference"": ""Patient/example""
            }
        }");

        // Assert
        result.IsValid.ShouldBeTrue();
    }

    [Trait("Category", "Regression")]
    [Fact]
    public void GivenObservationWithValueCodeableConcept_WhenValidating_ThenPasses()
    {
        // Arrange & Act - Bug #210-2: Choice type value[x] with CodeableConcept should pass
        // Previously failed with "Property 'coding' is not defined in StructureDefinition for Quantity"
        // because the wrong type (Quantity) was applied to validate the CodeableConcept children
        var result = ValidateResource(@"{
            ""resourceType"": ""Observation"",
            ""id"": ""example"",
            ""status"": ""final"",
            ""code"": {
                ""coding"": [{
                    ""system"": ""http://loinc.org"",
                    ""code"": ""8310-5""
                }]
            },
            ""valueCodeableConcept"": {
                ""coding"": [{
                    ""system"": ""http://snomed.info/sct"",
                    ""code"": ""12345""
                }],
                ""text"": ""Test value""
            }
        }");

        // Assert
        result.IsValid.ShouldBeTrue(
            $"Observation with valueCodeableConcept should pass. Issues: {string.Join(", ", result.Issues.Select(i => i.Message))}");
    }

    [Trait("Category", "Regression")]
    [Fact]
    public void GivenObservationWithValueQuantity_WhenValidating_ThenPasses()
    {
        // Arrange & Act - value[x] with Quantity type
        var result = ValidateResource(@"{
            ""resourceType"": ""Observation"",
            ""id"": ""example"",
            ""status"": ""final"",
            ""code"": {
                ""coding"": [{
                    ""system"": ""http://loinc.org"",
                    ""code"": ""8310-5""
                }]
            },
            ""valueQuantity"": {
                ""value"": 6.3,
                ""unit"": ""mmol/l"",
                ""system"": ""http://unitsofmeasure.org"",
                ""code"": ""mmol/L""
            }
        }");

        // Assert
        result.IsValid.ShouldBeTrue(
            $"Observation with valueQuantity should pass. Issues: {string.Join(", ", result.Issues.Select(i => i.Message))}");
    }

    [Trait("Category", "Regression")]
    [Fact]
    public void GivenObservationWithEffectivePeriod_WhenValidating_ThenPasses()
    {
        // Arrange & Act - effective[x] with Period type
        var result = ValidateResource(@"{
            ""resourceType"": ""Observation"",
            ""id"": ""example"",
            ""status"": ""final"",
            ""code"": {
                ""coding"": [{
                    ""system"": ""http://loinc.org"",
                    ""code"": ""8310-5""
                }]
            },
            ""effectivePeriod"": {
                ""start"": ""2023-01-01"",
                ""end"": ""2023-12-31""
            }
        }");

        // Assert
        result.IsValid.ShouldBeTrue(
            $"Observation with effectivePeriod should pass. Issues: {string.Join(", ", result.Issues.Select(i => i.Message))}");
    }

    [Trait("Category", "Regression")]
    [Fact]
    public void GivenExtensionWithValueCoding_WhenValidating_ThenPasses()
    {
        // Arrange & Act - Bug #210-5: Extension.value[x] with Coding type should be recognized
        // Previously failed with "Unknown member 'valueCoding'"
        var result = ValidateResource(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""example"",
            ""extension"": [{
                ""url"": ""http://example.org/fhir/StructureDefinition/test"",
                ""valueCoding"": {
                    ""system"": ""http://example.org"",
                    ""code"": ""test-code""
                }
            }]
        }");

        // Assert
        result.IsValid.ShouldBeTrue(
            $"Extension with valueCoding should pass. Issues: {string.Join(", ", result.Issues.Select(i => i.Message))}");
    }

    [Trait("Category", "Regression")]
    [Fact]
    public void GivenObservationWithComponentValueCodeableConcept_WhenValidating_ThenPasses()
    {
        // Arrange & Act - Bug #210-2: Nested choice type in BackboneElement
        // Observation.component.value[x] as valueCodeableConcept should not be flagged
        var result = ValidateResource(@"{
            ""resourceType"": ""Observation"",
            ""id"": ""blood-pressure"",
            ""status"": ""final"",
            ""code"": {
                ""coding"": [{
                    ""system"": ""http://loinc.org"",
                    ""code"": ""85354-9""
                }]
            },
            ""component"": [
                {
                    ""code"": {
                        ""coding"": [{
                            ""system"": ""http://loinc.org"",
                            ""code"": ""8480-6""
                        }]
                    },
                    ""valueQuantity"": {
                        ""value"": 120,
                        ""unit"": ""mmHg"",
                        ""system"": ""http://unitsofmeasure.org"",
                        ""code"": ""mm[Hg]""
                    }
                },
                {
                    ""code"": {
                        ""coding"": [{
                            ""system"": ""http://loinc.org"",
                            ""code"": ""8462-4""
                        }]
                    },
                    ""valueCodeableConcept"": {
                        ""coding"": [{
                            ""system"": ""http://snomed.info/sct"",
                            ""code"": ""12345""
                        }],
                        ""text"": ""Test value""
                    }
                }
            ]
        }");

        // Assert - Should not have "Property 'coding' is not defined in Quantity" errors
        var typeErrors = result.Issues
            .Where(i => i.Message.Contains("is not defined in the FHIR StructureDefinition"))
            .ToList();
        typeErrors.ShouldBeEmpty(
            $"Should not have unknown property errors for choice type children. Errors: {string.Join("; ", typeErrors.Select(i => i.Message))}");
    }

    [Trait("Category", "Regression")]
    [Fact]
    public void GivenObservationWithValueRange_WhenValidating_ThenPasses()
    {
        // Arrange & Act - Bug #210-2: value[x] with Range type should validate correctly
        // Range has high/low properties which should not be flagged as unknown
        var result = ValidateResource(@"{
            ""resourceType"": ""Observation"",
            ""id"": ""range-example"",
            ""status"": ""final"",
            ""code"": {
                ""coding"": [{
                    ""system"": ""http://loinc.org"",
                    ""code"": ""8310-5""
                }]
            },
            ""valueRange"": {
                ""low"": {
                    ""value"": 3.0,
                    ""unit"": ""mmol/l""
                },
                ""high"": {
                    ""value"": 6.0,
                    ""unit"": ""mmol/l""
                }
            }
        }");

        // Assert
        var typeErrors = result.Issues
            .Where(i => i.Message.Contains("is not defined in the FHIR StructureDefinition"))
            .ToList();
        typeErrors.ShouldBeEmpty(
            $"Should not have unknown property errors for Range children. Errors: {string.Join("; ", typeErrors.Select(i => i.Message))}");
    }

    [Trait("Category", "Regression")]
    [Fact]
    public void GivenQuestionnaireResponseWithValueCoding_WhenValidating_ThenPasses()
    {
        // Arrange & Act - Bug #210-5: QuestionnaireResponse.item.answer.value[x] with Coding
        var result = ValidateResource(@"{
            ""resourceType"": ""QuestionnaireResponse"",
            ""id"": ""example"",
            ""status"": ""completed"",
            ""item"": [{
                ""linkId"": ""q1"",
                ""answer"": [{
                    ""valueCoding"": {
                        ""system"": ""http://example.org"",
                        ""code"": ""test""
                    }
                }]
            }]
        }");

        // Assert
        var typeErrors = result.Issues
            .Where(i => i.Message.Contains("is not defined") || i.Message.Contains("unknown"))
            .ToList();
        typeErrors.ShouldBeEmpty(
            $"valueCoding in QuestionnaireResponse.item.answer should be valid. Errors: {string.Join("; ", typeErrors.Select(i => i.Message))}");
    }

    [Trait("Category", "Regression")]
    [Fact]
    public void GivenPatientWithPrimitiveExtensions_WhenValidating_ThenPasses()
    {
        // Arrange & Act - Bug #210-1: Primitive extensions (_property pattern) should pass full validation
        var result = ValidateResource(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""example"",
            ""birthDate"": ""1970-01-01"",
            ""_birthDate"": {
                ""extension"": [{
                    ""url"": ""http://hl7.org/fhir/StructureDefinition/patient-birthTime"",
                    ""valueDateTime"": ""1970-01-01T00:00:00Z""
                }]
            },
            ""active"": true,
            ""_active"": {
                ""extension"": [{
                    ""url"": ""http://example.org/fhir/StructureDefinition/confirmed"",
                    ""valueString"": ""confirmed""
                }]
            }
        }");

        // Assert
        result.IsValid.ShouldBeTrue(
            $"Patient with primitive extensions should pass. Issues: {string.Join(", ", result.Issues.Select(i => i.Message))}");
    }

    [Trait("Category", "Regression")]
    [Fact]
    public void GivenPatientWithExtensionOnlyPrimitive_WhenValidating_ThenPasses()
    {
        // Arrange & Act - Bug #210-1: Null primitive with only extension should pass
        var result = ValidateResource(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""example"",
            ""_birthDate"": {
                ""extension"": [{
                    ""url"": ""http://hl7.org/fhir/StructureDefinition/data-absent-reason"",
                    ""valueCode"": ""unknown""
                }]
            }
        }");

        // Assert
        result.IsValid.ShouldBeTrue(
            $"Patient with extension-only primitive should pass. Issues: {string.Join(", ", result.Issues.Select(i => i.Message))}");
    }

    #endregion

    #region Null State Handling

    [Fact]
    public void GivenOmittedValidationState_WhenValidating_ThenUsesDefaultState()
    {
        // Arrange
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""example"",
            ""active"": true
        }");
        var sourceNode = JsonNodeSourceNode.Create(json!);
        var schema = _schemaResolver.GetSchema("http://hl7.org/fhir/StructureDefinition/Patient");
        var settings = new ValidationSettings { Depth = ValidationDepth.Spec };

        // Act - Omit optional ValidationState parameter
        var result = schema!.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings);

        // Assert - Should use default state internally
        result.ShouldNotBeNull();
        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void GivenNullValidationState_WhenValidating_ThenCreatesDefaultState()
    {
        // Arrange
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""example"",
            ""active"": true
        }");
        var sourceNode = JsonNodeSourceNode.Create(json!);
        var schema = _schemaResolver.GetSchema("http://hl7.org/fhir/StructureDefinition/Patient");
        var settings = new ValidationSettings { Depth = ValidationDepth.Spec };

        // Act - Explicitly pass null for ValidationState
        var result = schema!.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, null);

        // Assert - Should not throw NullReferenceException
        result.ShouldNotBeNull();
        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void GivenNullValidationStateWithNestedTypes_WhenValidating_ThenSucceeds()
    {
        // Arrange - Patient with nested address (complex type)
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""example"",
            ""active"": true,
            ""address"": [{
                ""use"": ""home"",
                ""line"": [""123 Main St""],
                ""city"": ""Springfield""
            }]
        }");
        var sourceNode = JsonNodeSourceNode.Create(json!);
        var schema = _schemaResolver.GetSchema("http://hl7.org/fhir/StructureDefinition/Patient");
        var settings = new ValidationSettings { Depth = ValidationDepth.Full };

        // Act - Pass null for ValidationState (should handle nested complex types)
        var result = schema!.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, null);

        // Assert - Should not throw NullReferenceException in NestedComplexTypeCheck
        result.ShouldNotBeNull();
        result.IsValid.ShouldBeTrue();
    }

    #endregion

    #region Performance

    [Fact]
    public void GivenCachedSchemaResolver_WhenValidatingMultipleTimes_ThenUsesCache()
    {
        // Arrange - Use Tier.Fast to skip FHIRPath invariants for performance test
        var json = @"{
            ""resourceType"": ""Patient"",
            ""id"": ""example"",
            ""active"": true
        }";

        // Act - Multiple validations should use cached schema
        var start = DateTime.UtcNow;
        for (int i = 0; i < 100; i++)
        {
            var result = ValidateResource(json, ValidationDepth.Minimal);
            result.IsValid.ShouldBeTrue();
        }
        var duration = DateTime.UtcNow - start;

        // Assert - Should complete quickly (< 500ms for 100 validations with caching)
        duration.ShouldBeLessThan(TimeSpan.FromMilliseconds(500));
    }

    #endregion
}
