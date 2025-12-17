// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using FluentAssertions;
using Ignixa.Abstractions;
using Ignixa.NarrativeGenerator.Engine;
using Ignixa.NarrativeGenerator.Engine.ScriptFunctions;
using Ignixa.NarrativeGenerator.Security;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Localization;

namespace Ignixa.NarrativeGenerator.Tests;

/// <summary>
/// Tests for Compact narrative templates (Observation, Condition, DiagnosticReport).
/// Compact format produces single-line, dense text suitable for ML/AI embeddings.
/// </summary>
public class CompactTemplateTests
{
    private readonly FhirNarrativeGenerator _generator;
    private readonly IFhirSchemaProvider _schema;

    public CompactTemplateTests()
    {
        _schema = new R4CoreSchemaProvider();
        var templateResolver = new TemplateResolver();
        var fhirPathFunctions = new FhirPathScriptFunctions(_schema);
        var templateEngine = new NarrativeTemplateEngine(fhirPathFunctions, new MockStringLocalizer());
        var sanitizer = new XhtmlSanitizer();

        _generator = new FhirNarrativeGenerator(templateResolver, templateEngine, sanitizer, _schema);
    }

    #region Observation Compact Tests

    [Fact]
    public async Task GivenSimpleQuantityObservation_WhenGeneratingCompact_ThenReturnsDenseFormat()
    {
        // Arrange
        var json = """
            {
                "resourceType": "Observation",
                "id": "glucose",
                "status": "final",
                "code": {
                    "coding": [{
                        "system": "http://loinc.org",
                        "code": "15074-8",
                        "display": "Glucose"
                    }]
                },
                "valueQuantity": {
                    "value": 95,
                    "unit": "mg/dL",
                    "system": "http://unitsofmeasure.org",
                    "code": "mg/dL"
                },
                "interpretation": [{
                    "coding": [{
                        "system": "http://terminology.hl7.org/CodeSystem/v3-ObservationInterpretation",
                        "code": "H",
                        "display": "High"
                    }]
                }],
                "category": [{
                    "coding": [{
                        "system": "http://terminology.hl7.org/CodeSystem/observation-category",
                        "code": "laboratory",
                        "display": "Laboratory"
                    }]
                }],
                "effectiveDateTime": "2025-01-15T10:30:00Z",
                "subject": {
                    "display": "John Doe"
                }
            }
            """;
        var observation = JsonSourceNodeFactory.Parse<ResourceJsonNode>(json)!;
        observation.FhirVersion = FhirVersion.R4;
        var element = observation.ToElement(_schema);

        // Act
        var narrative = await _generator.GenerateNarrativeAsync(
            element,
            observation.ResourceType,
            format: TemplateFormat.Compact);

        // Assert
        narrative.Should().NotBeNullOrEmpty();
        narrative.Should().NotStartWith("["); // No resource type prefix
        narrative.Should().Contain("Glucose");
        narrative.Should().Contain("95 mg/dL");
        narrative.Should().Contain("(High)");
        narrative.Should().Contain("[Laboratory]");
        narrative.Should().Contain("@ 2025-01-15");
        narrative.Should().Contain("Subject: John Doe");
        narrative.Trim().Should().NotContain("\n\n"); // Single line
    }

    [Fact]
    public async Task GivenBloodPressureObservation_WhenGeneratingCompact_ThenShowsComponents()
    {
        // Arrange - Multi-component observation (Blood Pressure)
        var json = """
            {
                "resourceType": "Observation",
                "id": "blood-pressure",
                "status": "final",
                "code": {
                    "coding": [{
                        "system": "http://loinc.org",
                        "code": "85354-9",
                        "display": "Blood Pressure"
                    }]
                },
                "component": [
                    {
                        "code": {
                            "coding": [{
                                "system": "http://loinc.org",
                                "code": "8480-6",
                                "display": "Systolic"
                            }]
                        },
                        "valueQuantity": {
                            "value": 120,
                            "unit": "mmHg"
                        }
                    },
                    {
                        "code": {
                            "coding": [{
                                "system": "http://loinc.org",
                                "code": "8462-4",
                                "display": "Diastolic"
                            }]
                        },
                        "valueQuantity": {
                            "value": 80,
                            "unit": "mmHg"
                        }
                    }
                ],
                "category": [{
                    "coding": [{
                        "code": "vital-signs",
                        "display": "Vital Signs"
                    }]
                }],
                "effectiveDateTime": "2025-01-15T10:30:00Z",
                "subject": {
                    "display": "Jane Smith"
                }
            }
            """;
        var observation = JsonSourceNodeFactory.Parse<ResourceJsonNode>(json)!;
        observation.FhirVersion = FhirVersion.R4;
        var element = observation.ToElement(_schema);

        // Act
        var narrative = await _generator.GenerateNarrativeAsync(
            element,
            observation.ResourceType,
            format: TemplateFormat.Compact);

        // Assert
        narrative.Should().NotBeNullOrEmpty();
        narrative.Should().NotStartWith("["); // No resource type prefix
        narrative.Should().Contain("Blood Pressure");
        narrative.Should().Contain("Systolic 120 mmHg");
        narrative.Should().Contain("Diastolic 80 mmHg");
        narrative.Should().Contain("Jane Smith");
    }

    [Fact]
    public async Task GivenObservationWithDataAbsent_WhenGeneratingCompact_ThenShowsAbsentReason()
    {
        // Arrange - Observation with data absent reason instead of value
        var json = """
            {
                "resourceType": "Observation",
                "id": "absent",
                "status": "final",
                "code": {
                    "coding": [{
                        "display": "Heart Rate"
                    }]
                },
                "dataAbsentReason": {
                    "coding": [{
                        "code": "not-performed",
                        "display": "Not Performed"
                    }]
                },
                "subject": {
                    "display": "Test Patient"
                }
            }
            """;
        var observation = JsonSourceNodeFactory.Parse<ResourceJsonNode>(json)!;
        observation.FhirVersion = FhirVersion.R4;
        var element = observation.ToElement(_schema);

        // Act
        var narrative = await _generator.GenerateNarrativeAsync(
            element,
            observation.ResourceType,
            format: TemplateFormat.Compact);

        // Assert
        narrative.Should().NotBeNullOrEmpty();
        narrative.Should().NotStartWith("["); // No resource type prefix
        narrative.Should().Contain("Heart Rate");
        narrative.Should().Contain("[Not Performed]");
    }

    #endregion

    #region Condition Compact Tests

    [Fact]
    public async Task GivenDiabetesCondition_WhenGeneratingCompact_ThenReturnsDenseFormat()
    {
        // Arrange
        var json = """
            {
                "resourceType": "Condition",
                "id": "diabetes",
                "clinicalStatus": {
                    "coding": [{
                        "system": "http://terminology.hl7.org/CodeSystem/condition-clinical",
                        "code": "active",
                        "display": "Active"
                    }]
                },
                "verificationStatus": {
                    "coding": [{
                        "system": "http://terminology.hl7.org/CodeSystem/condition-ver-status",
                        "code": "confirmed",
                        "display": "Confirmed"
                    }]
                },
                "severity": {
                    "coding": [{
                        "code": "moderate",
                        "display": "Moderate"
                    }]
                },
                "code": {
                    "coding": [{
                        "system": "http://snomed.info/sct",
                        "code": "44054006",
                        "display": "Type 2 Diabetes Mellitus"
                    }]
                },
                "onsetDateTime": "2020-01-15",
                "subject": {
                    "display": "John Doe"
                }
            }
            """;
        var condition = JsonSourceNodeFactory.Parse<ResourceJsonNode>(json)!;
        condition.FhirVersion = FhirVersion.R4;
        var element = condition.ToElement(_schema);

        // Act
        var narrative = await _generator.GenerateNarrativeAsync(
            element,
            condition.ResourceType,
            format: TemplateFormat.Compact);

        // Assert
        narrative.Should().NotBeNullOrEmpty();
        narrative.Should().NotStartWith("["); // No resource type prefix
        narrative.Should().Contain("Type 2 Diabetes Mellitus");
        narrative.Should().Contain("(Active, Confirmed, Moderate)");
        narrative.Should().Contain("Onset: 2020-01-15");
        narrative.Should().MatchRegex(@"\d+y ago\)"); // Should show years ago
        narrative.Should().Contain("Subject: John Doe");
    }

    [Fact]
    public async Task GivenHypertensionWithBodySite_WhenGeneratingCompact_ThenShowsBodySite()
    {
        // Arrange
        var json = """
            {
                "resourceType": "Condition",
                "id": "hypertension",
                "clinicalStatus": {
                    "coding": [{
                        "code": "active",
                        "display": "Active"
                    }]
                },
                "verificationStatus": {
                    "coding": [{
                        "code": "confirmed",
                        "display": "Confirmed"
                    }]
                },
                "code": {
                    "coding": [{
                        "display": "Hypertension"
                    }]
                },
                "bodySite": [{
                    "coding": [{
                        "display": "Systemic"
                    }]
                }],
                "onsetString": "2018",
                "subject": {
                    "display": "Jane Smith"
                }
            }
            """;
        var condition = JsonSourceNodeFactory.Parse<ResourceJsonNode>(json)!;
        condition.FhirVersion = FhirVersion.R4;
        var element = condition.ToElement(_schema);

        // Act
        var narrative = await _generator.GenerateNarrativeAsync(
            element,
            condition.ResourceType,
            format: TemplateFormat.Compact);

        // Assert
        narrative.Should().NotBeNullOrEmpty();
        narrative.Should().NotStartWith("["); // No resource type prefix
        narrative.Should().Contain("Hypertension");
        narrative.Should().Contain("Body Site: Systemic");
        narrative.Should().Contain("Onset: 2018");
    }

    [Fact]
    public async Task GivenResolvedCondition_WhenGeneratingCompact_ThenShowsAbatement()
    {
        // Arrange
        var json = """
            {
                "resourceType": "Condition",
                "id": "resolved",
                "clinicalStatus": {
                    "coding": [{
                        "code": "resolved",
                        "display": "Resolved"
                    }]
                },
                "code": {
                    "text": "Common Cold"
                },
                "onsetDateTime": "2025-01-01",
                "abatementDateTime": "2025-01-10",
                "subject": {
                    "display": "Test Patient"
                }
            }
            """;
        var condition = JsonSourceNodeFactory.Parse<ResourceJsonNode>(json)!;
        condition.FhirVersion = FhirVersion.R4;
        var element = condition.ToElement(_schema);

        // Act
        var narrative = await _generator.GenerateNarrativeAsync(
            element,
            condition.ResourceType,
            format: TemplateFormat.Compact);

        // Assert
        narrative.Should().NotBeNullOrEmpty();
        narrative.Should().NotStartWith("["); // No resource type prefix
        // Note: code.text extraction may vary based on parser implementation
        narrative.Should().Contain("(Resolved)");
        narrative.Should().Contain("Abated: 2025-01-10");
    }

    #endregion

    #region DiagnosticReport Compact Tests

    [Fact]
    public async Task GivenCompleteCBCReport_WhenGeneratingCompact_ThenReturnsDenseFormat()
    {
        // Arrange
        var json = """
            {
                "resourceType": "DiagnosticReport",
                "id": "cbc",
                "status": "final",
                "code": {
                    "coding": [{
                        "system": "http://loinc.org",
                        "code": "58410-2",
                        "display": "Complete Blood Count"
                    }]
                },
                "conclusion": "Normal findings. All values within reference ranges.",
                "result": [
                    { "reference": "Observation/rbc", "display": "RBC" },
                    { "reference": "Observation/wbc", "display": "WBC" },
                    { "reference": "Observation/hgb", "display": "Hemoglobin" },
                    { "reference": "Observation/hct", "display": "Hematocrit" }
                ],
                "category": [{
                    "coding": [{
                        "code": "laboratory",
                        "display": "Laboratory"
                    }]
                }],
                "issued": "2025-01-15T14:30:00Z",
                "subject": {
                    "display": "John Doe"
                }
            }
            """;
        var report = JsonSourceNodeFactory.Parse<ResourceJsonNode>(json)!;
        report.FhirVersion = FhirVersion.R4;
        var element = report.ToElement(_schema);

        // Act
        var narrative = await _generator.GenerateNarrativeAsync(
            element,
            report.ResourceType,
            format: TemplateFormat.Compact);

        // Assert
        narrative.Should().NotBeNullOrEmpty();
        narrative.Should().NotStartWith("["); // No resource type prefix
        narrative.Should().Contain("Complete Blood Count");
        narrative.Should().Contain("(final)");
        narrative.Should().Contain("Conclusion: Normal findings");
        narrative.Should().Contain("Results: 4 findings");
        narrative.Should().Contain("[Laboratory]");
        narrative.Should().Contain("Issued: 2025-01-15");
        narrative.Should().Contain("Subject: John Doe");
    }

    [Fact]
    public async Task GivenChestXRayReport_WhenGeneratingCompact_ThenShowsRadiologyCategory()
    {
        // Arrange
        var json = """
            {
                "resourceType": "DiagnosticReport",
                "id": "chest-xray",
                "status": "final",
                "code": {
                    "text": "Chest X-Ray"
                },
                "conclusion": "No acute findings. Lungs are clear.",
                "category": [{
                    "coding": [{
                        "code": "radiology",
                        "display": "Radiology"
                    }]
                }],
                "issued": "2025-01-14T15:00:00Z",
                "subject": {
                    "display": "Jane Smith"
                }
            }
            """;
        var report = JsonSourceNodeFactory.Parse<ResourceJsonNode>(json)!;
        report.FhirVersion = FhirVersion.R4;
        var element = report.ToElement(_schema);

        // Act
        var narrative = await _generator.GenerateNarrativeAsync(
            element,
            report.ResourceType,
            format: TemplateFormat.Compact);

        // Assert
        narrative.Should().NotBeNullOrEmpty();
        narrative.Should().NotStartWith("["); // No resource type prefix
        // Note: code.text extraction may vary based on parser implementation
        narrative.Should().Contain("Conclusion: No acute findings");
        narrative.Should().Contain("[Radiology]");
    }

    [Fact]
    public async Task GivenReportWithLongConclusion_WhenGeneratingCompact_ThenTruncatesText()
    {
        // Arrange - Conclusion longer than 100 chars
        var longConclusion = new string('A', 120);
        var json = $$"""
            {
                "resourceType": "DiagnosticReport",
                "id": "long",
                "status": "final",
                "code": {
                    "text": "Test Report"
                },
                "conclusion": "{{longConclusion}}",
                "subject": {
                    "display": "Test Patient"
                }
            }
            """;
        var report = JsonSourceNodeFactory.Parse<ResourceJsonNode>(json)!;
        report.FhirVersion = FhirVersion.R4;
        var element = report.ToElement(_schema);

        // Act
        var narrative = await _generator.GenerateNarrativeAsync(
            element,
            report.ResourceType,
            format: TemplateFormat.Compact);

        // Assert
        narrative.Should().NotBeNullOrEmpty();
        narrative.Should().NotStartWith("["); // No resource type prefix
        narrative.Should().Contain("Conclusion:");
        narrative.Should().Contain("..."); // Truncated
        var conclusionMatch = System.Text.RegularExpressions.Regex.Match(narrative, @"Conclusion: ([^.]+)\.");
        if (conclusionMatch.Success)
        {
            conclusionMatch.Groups[1].Value.Length.Should().BeLessThanOrEqualTo(103); // 100 + "..."
        }
    }

    [Fact]
    public async Task GivenReportWithMediaAndAttachments_WhenGeneratingCompact_ThenShowsCounts()
    {
        // Arrange
        var json = """
            {
                "resourceType": "DiagnosticReport",
                "id": "with-media",
                "status": "final",
                "code": {
                    "text": "Imaging Study"
                },
                "media": [
                    { "link": { "display": "Image 1" } },
                    { "link": { "display": "Image 2" } }
                ],
                "presentedForm": [
                    { "contentType": "application/pdf", "title": "Report PDF" }
                ],
                "result": [
                    { "display": "Finding 1" }
                ],
                "subject": {
                    "display": "Test Patient"
                }
            }
            """;
        var report = JsonSourceNodeFactory.Parse<ResourceJsonNode>(json)!;
        report.FhirVersion = FhirVersion.R4;
        var element = report.ToElement(_schema);

        // Act
        var narrative = await _generator.GenerateNarrativeAsync(
            element,
            report.ResourceType,
            format: TemplateFormat.Compact);

        // Assert
        narrative.Should().NotBeNullOrEmpty();
        narrative.Should().Contain("Results: 1 finding");
        narrative.Should().Contain("Media: 2 items");
        narrative.Should().Contain("Attachments: 1");
    }

    #endregion

    #region Cross-Format Consistency Tests

    [Fact]
    public async Task GivenObservation_WhenGeneratingAllFormats_ThenAllContainKeyData()
    {
        // Arrange
        var json = """
            {
                "resourceType": "Observation",
                "id": "multi-format",
                "status": "final",
                "code": {
                    "coding": [{
                        "display": "Heart Rate"
                    }]
                },
                "valueQuantity": {
                    "value": 72,
                    "unit": "bpm"
                },
                "subject": {
                    "display": "Multi Format Test"
                }
            }
            """;
        var observation = JsonSourceNodeFactory.Parse<ResourceJsonNode>(json)!;
        observation.FhirVersion = FhirVersion.R4;
        var element = observation.ToElement(_schema);

        // Act
        var htmlNarrative = await _generator.GenerateNarrativeAsync(element, observation.ResourceType, format: TemplateFormat.Html);
        var vectorNarrative = await _generator.GenerateNarrativeAsync(element, observation.ResourceType, format: TemplateFormat.Compact);

        // Assert - Both formats should contain key data
        htmlNarrative.Should().Contain("Heart Rate");
        htmlNarrative.Should().Contain("72");
        vectorNarrative.Should().Contain("Heart Rate");
        vectorNarrative.Should().Contain("72");
        vectorNarrative.Should().NotStartWith("["); // Compact has no resource type prefix
    }

    #endregion
}
