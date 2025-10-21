// <copyright file="FastValidatorIntegrationTests.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System.Text.Json.Nodes;
using FluentAssertions;
using Ignixa.Specification.Generated;
using Ignixa.SourceNodeSerialization.Abstractions;
using Ignixa.SourceNodeSerialization.SourceNodes;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;

namespace Ignixa.Validation.Tests;

/// <summary>
/// Integration tests for ValidationSchema with tier-aware validation.
/// Tests that ValidationSchema correctly executes tier-appropriate checks
/// (Fast = universal, Spec = universal + schema, Profile = all checks).
/// </summary>
public class FhirValidatorIntegrationTests
{
    private readonly R4StructureDefinitionSummaryProvider _provider;
    private readonly IValidationSchemaResolver _schemaResolver;

    public FhirValidatorIntegrationTests()
    {
        _provider = new R4StructureDefinitionSummaryProvider();
        var innerResolver = new StructureDefinitionSchemaResolver(_provider);
        _schemaResolver = new CachedValidationSchemaResolver(innerResolver);
    }

    private ValidationResult ValidateResource(string resourceJson, ValidationTier tier = ValidationTier.Spec)
    {
        var json = JsonNode.Parse(resourceJson);
        var sourceNode = JsonNodeSourceNode.Create(json!);
        var resourceType = (sourceNode as IResourceTypeSupplier)?.ResourceType ?? sourceNode.Name;
        var canonicalUrl = $"http://hl7.org/fhir/StructureDefinition/{resourceType}";
        var schema = _schemaResolver.GetSchema(canonicalUrl);

        if (schema == null)
        {
            throw new InvalidOperationException($"Schema not found for {resourceType}");
        }

        var settings = new ValidationSettings { Tier = tier };
        var state = new ValidationState();
        return schema.Validate(sourceNode, settings, state);
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
        }", ValidationTier.None);

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
        }", ValidationTier.Fast);

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
        }", ValidationTier.Spec);

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
            var result = ValidateResource(json, ValidationTier.Fast);
            result.IsValid.Should().BeTrue();
        }
        var duration = DateTime.UtcNow - start;

        // Assert - Should complete quickly (< 500ms for 100 validations with caching)
        duration.Should().BeLessThan(TimeSpan.FromMilliseconds(500));
    }

    #endregion
}
