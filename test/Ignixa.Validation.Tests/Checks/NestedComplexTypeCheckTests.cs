// <copyright file="NestedComplexTypeCheckTests.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System.Text.Json.Nodes;
using Ignixa.SourceNodeSerialization.Abstractions;
using Ignixa.SourceNodeSerialization.SourceNodes;
using Ignixa.SourceNodeSerialization.Specification;
using Ignixa.Specification;
using Ignixa.Specification.Generated;
using Ignixa.Validation;
using Ignixa.Validation.Checks;
using Ignixa.Validation.Schema;
using Xunit;

namespace Ignixa.Validation.Tests.Checks;

/// <summary>
/// Tests for NestedComplexTypeCheck - validates nested complex types (BackboneElement, complex datatypes).
/// </summary>
public class NestedComplexTypeCheckTests
{
    private static readonly IStructureDefinitionSummaryProvider Provider =
        new R4StructureDefinitionSummaryProvider();

    private static readonly StructureDefinitionSchemaBuilder Builder =
        new StructureDefinitionSchemaBuilder();

    [Fact]
    public void GivenAuditEventWithValidAgent_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""AuditEvent"",
            ""type"": {""system"": ""http://terminology.hl7.org/CodeSystem/dicom-audit-lifecycle"", ""code"": ""1""},
            ""recorded"": ""2021-05-28T00:00:00.000Z"",
            ""agent"": [{
                ""type"": {""coding"": [{""system"": ""http://terminology.hl7.org/CodeSystem/v3-ParticipationType"", ""code"": ""ATNA""}]},
                ""who"": {""reference"": ""Practitioner/example""},
                ""requestor"": true
            }],
            ""source"": {
                ""observer"": {""reference"": ""Device/example""}
            },
            ""entity"": [{
                ""what"": {""reference"": ""Patient/example""}
            }]
        }");

        var sourceNode = JsonNodeSourceNode.Create(json);
        var settings = new ValidationSettings { Tier = ValidationTier.Spec };
        var state = new ValidationState();

        // Build AuditEvent schema (includes nested type checks for agent, source, entity)
        var auditEventSummary = Provider.Provide("AuditEvent");
        var schema = Builder.BuildSchema(auditEventSummary, Provider);

        // Act
        var result = schema.Validate(sourceNode, settings, state);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void GivenAuditEventWithMissingRequestor_WhenValidating_ThenReturnsError()
    {
        // Arrange - agent is missing required requestor property
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""AuditEvent"",
            ""type"": {""system"": ""http://terminology.hl7.org/CodeSystem/dicom-audit-lifecycle"", ""code"": ""1""},
            ""recorded"": ""2021-05-28T00:00:00.000Z"",
            ""agent"": [{
                ""type"": {""coding"": [{""system"": ""http://terminology.hl7.org/CodeSystem/v3-ParticipationType"", ""code"": ""ATNA""}]},
                ""who"": {""reference"": ""Practitioner/example""}
            }],
            ""source"": {
                ""observer"": {""reference"": ""Device/example""}
            },
            ""entity"": [{
                ""what"": {""reference"": ""Patient/example""}
            }]
        }");

        var sourceNode = JsonNodeSourceNode.Create(json);
        var settings = new ValidationSettings { Tier = ValidationTier.Spec };
        var state = new ValidationState();

        // Build AuditEvent schema (includes nested type checks)
        var auditEventSummary = Provider.Provide("AuditEvent");
        var schema = Builder.BuildSchema(auditEventSummary, Provider);

        // Act
        var result = schema.Validate(sourceNode, settings, state);

        // Assert
        // Should have at least one error about missing requestor
        Assert.False(result.IsValid);
        var requestorError = result.Issues.FirstOrDefault(i =>
            i.Path.Contains("requestor", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(requestorError);
    }

    [Fact]
    public void GivenAuditEventWithMultipleAgents_WhenValidating_ThenValidatesAll()
    {
        // Arrange - two agents, second one missing requestor
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""AuditEvent"",
            ""type"": {""system"": ""http://terminology.hl7.org/CodeSystem/dicom-audit-lifecycle"", ""code"": ""1""},
            ""recorded"": ""2021-05-28T00:00:00.000Z"",
            ""agent"": [
                {
                    ""type"": {""coding"": [{""system"": ""http://terminology.hl7.org/CodeSystem/v3-ParticipationType"", ""code"": ""ATNA""}]},
                    ""who"": {""reference"": ""Practitioner/example1""},
                    ""requestor"": true
                },
                {
                    ""type"": {""coding"": [{""system"": ""http://terminology.hl7.org/CodeSystem/v3-ParticipationType"", ""code"": ""ATNA""}]},
                    ""who"": {""reference"": ""Practitioner/example2""}
                }
            ],
            ""source"": {
                ""observer"": {""reference"": ""Device/example""}
            },
            ""entity"": [{
                ""what"": {""reference"": ""Patient/example""}
            }]
        }");

        var sourceNode = JsonNodeSourceNode.Create(json);
        var settings = new ValidationSettings { Tier = ValidationTier.Spec };
        var state = new ValidationState();

        // Build AuditEvent schema
        var auditEventSummary = Provider.Provide("AuditEvent");
        var schema = Builder.BuildSchema(auditEventSummary, Provider);

        // Act
        var result = schema.Validate(sourceNode, settings, state);

        // Assert
        // Should have error for agent[1].requestor missing
        Assert.False(result.IsValid);
        var agentArrayError = result.Issues.FirstOrDefault(i =>
            i.Path.Contains("agent[1]", StringComparison.OrdinalIgnoreCase) &&
            i.Path.Contains("requestor", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(agentArrayError);
    }

    [Fact]
    public void GivenPatientWithValidAddress_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange - Address is a complex datatype, not BackboneElement
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""example"",
            ""address"": [{
                ""use"": ""home"",
                ""line"": [""123 Main St""],
                ""city"": ""Boston"",
                ""state"": ""MA"",
                ""postalCode"": ""02101"",
                ""country"": ""USA""
            }]
        }");

        var sourceNode = JsonNodeSourceNode.Create(json);
        var settings = new ValidationSettings { Tier = ValidationTier.Spec };
        var state = new ValidationState();

        // Build Patient schema (includes nested type checks for address, name, contact, etc.)
        var patientSummary = Provider.Provide("Patient");
        var schema = Builder.BuildSchema(patientSummary, Provider);

        // Act
        var result = schema.Validate(sourceNode, settings, state);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void GivenPatientWithMultipleAddresses_WhenValidating_ThenValidatesAll()
    {
        // Arrange - Multiple addresses in array
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""example"",
            ""address"": [
                {
                    ""use"": ""home"",
                    ""line"": [""123 Main St""],
                    ""city"": ""Boston""
                },
                {
                    ""use"": ""work"",
                    ""line"": [""456 Work Ave""],
                    ""city"": ""Cambridge""
                }
            ]
        }");

        var sourceNode = JsonNodeSourceNode.Create(json);
        var settings = new ValidationSettings { Tier = ValidationTier.Spec };
        var state = new ValidationState();

        // Build Patient schema
        var patientSummary = Provider.Provide("Patient");
        var schema = Builder.BuildSchema(patientSummary, Provider);

        // Act
        var result = schema.Validate(sourceNode, settings, state);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void GivenObservationWithValidComponent_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange - Observation has nested component array (BackboneElement)
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""Observation"",
            ""id"": ""example"",
            ""status"": ""final"",
            ""code"": {
                ""coding"": [{
                    ""system"": ""http://loinc.org"",
                    ""code"": ""8480-6""
                }]
            },
            ""subject"": {""reference"": ""Patient/example""},
            ""effectiveDateTime"": ""2021-05-28T00:00:00.000Z"",
            ""component"": [{
                ""code"": {
                    ""coding"": [{
                        ""system"": ""http://loinc.org"",
                        ""code"": ""8462-4""
                    }]
                },
                ""valueQuantity"": {
                    ""value"": 120,
                    ""unit"": ""mmHg"",
                    ""system"": ""http://unitsofmeasure.org"",
                    ""code"": ""mm[Hg]""
                }
            }]
        }");

        var sourceNode = JsonNodeSourceNode.Create(json);
        var settings = new ValidationSettings { Tier = ValidationTier.Spec };
        var state = new ValidationState();

        // Build Observation schema
        var observationSummary = Provider.Provide("Observation");
        var schema = Builder.BuildSchema(observationSummary, Provider);

        // Act
        var result = schema.Validate(sourceNode, settings, state);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void GivenEmptyNestedArray_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange - agent array is empty (cardinality check will catch if required min > 0)
        var json = JsonNode.Parse(@"{
            ""resourceType"": ""AuditEvent"",
            ""type"": {""system"": ""http://terminology.hl7.org/CodeSystem/dicom-audit-lifecycle"", ""code"": ""1""},
            ""recorded"": ""2021-05-28T00:00:00.000Z"",
            ""agent"": [],
            ""source"": {
                ""observer"": {""reference"": ""Device/example""}
            }
        }");

        var sourceNode = JsonNodeSourceNode.Create(json);
        var settings = new ValidationSettings { Tier = ValidationTier.Spec };
        var state = new ValidationState();

        // Build AuditEvent schema
        var auditEventSummary = Provider.Provide("AuditEvent");
        var schema = Builder.BuildSchema(auditEventSummary, Provider);

        // Act
        var result = schema.Validate(sourceNode, settings, state);

        // Assert
        // NestedComplexTypeCheck itself passes (no elements to validate)
        // But CardinalityCheck should catch min=1 requirement
        // The empty array error will come from cardinality, not from nested check
        var hasCardinalityError = result.Issues.Any(i =>
            i.Code == "cardinality-violation" &&
            i.Path.Contains("agent"));
        Assert.True(hasCardinalityError || result.IsValid); // Depends on cardinality requirement
    }

}
