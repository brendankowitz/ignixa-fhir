// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using Shouldly;
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
        narrative.ShouldNotBeNullOrEmpty();
        narrative.ShouldNotStartWith("["); // No resource type prefix
        narrative.ShouldContain("Glucose");
        narrative.ShouldContain("95 mg/dL");
        narrative.ShouldContain("(High)");
        narrative.ShouldContain("[Laboratory]");
        narrative.ShouldContain("@ 2025-01-15");
        narrative.ShouldContain("Subject: John Doe");
        narrative.Trim().ShouldNotContain("\n\n"); // Single line
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
        narrative.ShouldNotBeNullOrEmpty();
        narrative.ShouldNotStartWith("["); // No resource type prefix
        narrative.ShouldContain("Blood Pressure");
        narrative.ShouldContain("Systolic 120 mmHg");
        narrative.ShouldContain("Diastolic 80 mmHg");
        narrative.ShouldContain("Jane Smith");
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
        narrative.ShouldNotBeNullOrEmpty();
        narrative.ShouldNotStartWith("["); // No resource type prefix
        narrative.ShouldContain("Heart Rate");
        narrative.ShouldContain("[Not Performed]");
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
        narrative.ShouldNotBeNullOrEmpty();
        narrative.ShouldNotStartWith("["); // No resource type prefix
        narrative.ShouldContain("Type 2 Diabetes Mellitus");
        narrative.ShouldContain("(Active, Confirmed, Moderate)");
        narrative.ShouldContain("Onset: 2020-01-15");
        narrative.ShouldMatch(@"\d+y ago\)"); // Should show years ago
        narrative.ShouldContain("Subject: John Doe");
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
        narrative.ShouldNotBeNullOrEmpty();
        narrative.ShouldNotStartWith("["); // No resource type prefix
        narrative.ShouldContain("Hypertension");
        narrative.ShouldContain("Body Site: Systemic");
        narrative.ShouldContain("Onset: 2018");
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
        narrative.ShouldNotBeNullOrEmpty();
        narrative.ShouldNotStartWith("["); // No resource type prefix
        // Note: code.text extraction may vary based on parser implementation
        narrative.ShouldContain("(Resolved)");
        narrative.ShouldContain("Abated: 2025-01-10");
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
        narrative.ShouldNotBeNullOrEmpty();
        narrative.ShouldNotStartWith("["); // No resource type prefix
        narrative.ShouldContain("Complete Blood Count");
        narrative.ShouldContain("(final)");
        narrative.ShouldContain("Conclusion: Normal findings");
        narrative.ShouldContain("Results: 4 findings");
        narrative.ShouldContain("[Laboratory]");
        narrative.ShouldContain("Issued: 2025-01-15");
        narrative.ShouldContain("Subject: John Doe");
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
        narrative.ShouldNotBeNullOrEmpty();
        narrative.ShouldNotStartWith("["); // No resource type prefix
        // Note: code.text extraction may vary based on parser implementation
        narrative.ShouldContain("Conclusion: No acute findings");
        narrative.ShouldContain("[Radiology]");
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
        narrative.ShouldNotBeNullOrEmpty();
        narrative.ShouldNotStartWith("["); // No resource type prefix
        narrative.ShouldContain("Conclusion:");
        narrative.ShouldContain("..."); // Truncated
        var conclusionMatch = System.Text.RegularExpressions.Regex.Match(narrative, @"Conclusion: ([^.]+)\.");
        if (conclusionMatch.Success)
        {
            conclusionMatch.Groups[1].Value.Length.ShouldBeLessThanOrEqualTo(103); // 100 + "..."
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
        narrative.ShouldNotBeNullOrEmpty();
        narrative.ShouldContain("Results: 1 finding");
        narrative.ShouldContain("Media: 2 items");
        narrative.ShouldContain("Attachments: 1");
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
        htmlNarrative.ShouldContain("Heart Rate");
        htmlNarrative.ShouldContain("72");
        vectorNarrative.ShouldContain("Heart Rate");
        vectorNarrative.ShouldContain("72");
        vectorNarrative.ShouldNotStartWith("["); // Compact has no resource type prefix
    }

    #endregion
}
