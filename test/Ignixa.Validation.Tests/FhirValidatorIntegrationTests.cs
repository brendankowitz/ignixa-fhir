// <copyright file="FastValidatorIntegrationTests.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System.Text.Json.Nodes;
using FluentAssertions;
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
        result.IsValid.Should().BeTrue();
        result.Issues.Should().BeEmpty();
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
        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(i => i.Path.Contains("id"));
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
        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(i => i.Severity == IssueSeverity.Error);
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
        result.IsValid.Should().BeTrue();
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
        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(i => i.Path.Contains("text.status"));
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
        result.IsValid.Should().BeTrue($"Observation with valid narrative should pass validation. Issues: {string.Join(", ", result.Issues.Select(i => i.Message))}");
    }

    #endregion

    #region Schema-Driven Checks

    [Fact]
    public void GivenInvalidReference_WhenValidating_ThenDetectsReferenceFormatViolation()
    {
        // Arrange & Act
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
                ""reference"": ""invalid-reference-format""
            }
        }");

        // Assert
        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(i => i.Path.Contains("subject.reference"));
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
        result.IsValid.Should().BeTrue();
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
            result.IsValid.Should().BeTrue();
        }
        var duration = DateTime.UtcNow - start;

        // Assert - Should complete quickly (< 500ms for 100 validations with caching)
        duration.Should().BeLessThan(TimeSpan.FromMilliseconds(500));
    }

    #endregion
}
