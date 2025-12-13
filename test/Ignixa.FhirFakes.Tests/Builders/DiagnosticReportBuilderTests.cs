// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.FhirFakes.Builders;
using Ignixa.Specification;
using Ignixa.Specification.Generated;
using Xunit;

namespace Ignixa.FhirFakes.Tests.Builders;

/// <summary>
/// Unit tests for DiagnosticReportBuilder.
/// Tests basic diagnostic report generation with codes, subjects, and results.
/// </summary>
public class DiagnosticReportBuilderTests
{
    private readonly IFhirSchemaProvider _schemaProvider = new R4CoreSchemaProvider();

    #region Basic Building Tests

    [Fact]
    public void GivenDiagnosticReportBuilder_WhenBuildingWithMinimalFields_ThenCreatesDiagnosticReport()
    {
        // Arrange & Act
        var report = DiagnosticReportBuilder.Create(_schemaProvider)
            .WithCode("58410-2", "http://loinc.org", "Complete blood count")
            .Build();

        // Assert
        report.Should().NotBeNull();
        report.ResourceType.Should().Be("DiagnosticReport");
        report.Id.Should().NotBeNullOrEmpty();
        report.MutableNode["status"]?.GetValue<string>().Should().Be("final");

        var code = report.MutableNode["code"]?.AsObject();
        code.Should().NotBeNull();
        var coding = code?["coding"]?.AsArray()?[0]?.AsObject();
        coding?["system"]?.GetValue<string>().Should().Be("http://loinc.org");
        coding?["code"]?.GetValue<string>().Should().Be("58410-2");
        coding?["display"]?.GetValue<string>().Should().Be("Complete blood count");
    }

    [Fact]
    public void GivenDiagnosticReportBuilder_WhenBuildingWithId_ThenUsesProvidedId()
    {
        // Arrange
        var expectedId = "report-123";

        // Act
        var report = DiagnosticReportBuilder.Create(_schemaProvider)
            .WithId(expectedId)
            .WithCode("24331-1", "http://loinc.org", "Lipid panel")
            .Build();

        // Assert
        report.Id.Should().Be(expectedId);
    }

    [Fact]
    public void GivenDiagnosticReportBuilder_WhenBuildingWithTag_ThenIncludesTagInMeta()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Act
        var report = DiagnosticReportBuilder.Create(_schemaProvider)
            .WithCode("24323-8", "http://loinc.org", "Comprehensive metabolic panel")
            .WithTag(tag)
            .Build();

        // Assert
        report.MutableNode["meta"]?["tag"].Should().NotBeNull();
        var tags = report.MutableNode["meta"]?["tag"]?.AsArray();
        tags.Should().HaveCount(1);

        var metaTag = tags?[0]?.AsObject();
        metaTag?["code"]?.GetValue<string>().Should().Be(tag);
        metaTag?["system"]?.GetValue<string>().Should().Be("http://ignixa.dev/test-isolation");
    }

    [Fact]
    public void GivenDiagnosticReportBuilder_WhenBuildingWithoutCode_ThenThrowsInvalidOperationException()
    {
        // Arrange & Act
        var act = () => DiagnosticReportBuilder.Create(_schemaProvider)
            .WithStatus("final")
            .Build();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*code is required*");
    }

    #endregion

    #region Status Tests

    [Fact]
    public void GivenDiagnosticReportBuilder_WhenBuildingWithoutStatus_ThenDefaultsToFinal()
    {
        // Arrange & Act
        var report = DiagnosticReportBuilder.Create(_schemaProvider)
            .WithCode("58410-2", "http://loinc.org")
            .Build();

        // Assert
        report.MutableNode["status"]?.GetValue<string>().Should().Be("final");
    }

    [Fact]
    public void GivenDiagnosticReportBuilder_WhenBuildingWithPreliminaryStatus_ThenUsesPreliminary()
    {
        // Arrange & Act
        var report = DiagnosticReportBuilder.Create(_schemaProvider)
            .WithCode("58410-2", "http://loinc.org")
            .WithStatus("preliminary")
            .Build();

        // Assert
        report.MutableNode["status"]?.GetValue<string>().Should().Be("preliminary");
    }

    [Fact]
    public void GivenDiagnosticReportBuilder_WhenBuildingWithAmendedStatus_ThenUsesAmended()
    {
        // Arrange & Act
        var report = DiagnosticReportBuilder.Create(_schemaProvider)
            .WithCode("24331-1", "http://loinc.org")
            .WithStatus("amended")
            .Build();

        // Assert
        report.MutableNode["status"]?.GetValue<string>().Should().Be("amended");
    }

    [Fact]
    public void GivenDiagnosticReportBuilder_WhenBuildingWithCorrectedStatus_ThenUsesCorrected()
    {
        // Arrange & Act
        var report = DiagnosticReportBuilder.Create(_schemaProvider)
            .WithCode("36554-4", "http://loinc.org")
            .WithStatus("corrected")
            .Build();

        // Assert
        report.MutableNode["status"]?.GetValue<string>().Should().Be("corrected");
    }

    [Fact]
    public void GivenDiagnosticReportBuilder_WhenBuildingWithCancelledStatus_ThenUsesCancelled()
    {
        // Arrange & Act
        var report = DiagnosticReportBuilder.Create(_schemaProvider)
            .WithCode("24323-8", "http://loinc.org")
            .WithStatus("cancelled")
            .Build();

        // Assert
        report.MutableNode["status"]?.GetValue<string>().Should().Be("cancelled");
    }

    #endregion

    #region Code Tests

    [Fact]
    public void GivenDiagnosticReportBuilder_WhenBuildingWithCodeWithoutDisplay_ThenIncludesCodeOnly()
    {
        // Arrange & Act
        var report = DiagnosticReportBuilder.Create(_schemaProvider)
            .WithCode("58410-2", "http://loinc.org")
            .Build();

        // Assert
        var code = report.MutableNode["code"]?.AsObject();
        var coding = code?["coding"]?.AsArray()?[0]?.AsObject();

        coding?["system"]?.GetValue<string>().Should().Be("http://loinc.org");
        coding?["code"]?.GetValue<string>().Should().Be("58410-2");
        coding?.TryGetPropertyValue("display", out _).Should().BeFalse();
    }

    [Fact]
    public void GivenDiagnosticReportBuilder_WhenBuildingWithSnomedCode_ThenUsesSnomedSystem()
    {
        // Arrange & Act
        var report = DiagnosticReportBuilder.Create(_schemaProvider)
            .WithCode("168537006", "http://snomed.info/sct", "Microscopy")
            .Build();

        // Assert
        var code = report.MutableNode["code"]?.AsObject();
        var coding = code?["coding"]?.AsArray()?[0]?.AsObject();

        coding?["system"]?.GetValue<string>().Should().Be("http://snomed.info/sct");
        coding?["code"]?.GetValue<string>().Should().Be("168537006");
        coding?["display"]?.GetValue<string>().Should().Be("Microscopy");
    }

    [Fact]
    public void GivenDiagnosticReportBuilder_WhenBuildingWithLoincCode_ThenUsesLoincSystem()
    {
        // Arrange & Act
        var report = DiagnosticReportBuilder.Create(_schemaProvider)
            .WithCode("24331-1", "http://loinc.org", "Lipid panel with direct LDL")
            .Build();

        // Assert
        var code = report.MutableNode["code"]?.AsObject();
        var coding = code?["coding"]?.AsArray()?[0]?.AsObject();

        coding?["system"]?.GetValue<string>().Should().Be("http://loinc.org");
        coding?["code"]?.GetValue<string>().Should().Be("24331-1");
        coding?["display"]?.GetValue<string>().Should().Be("Lipid panel with direct LDL");
    }

    #endregion

    #region Subject Tests

    [Fact]
    public void GivenDiagnosticReportBuilder_WhenBuildingWithSubject_ThenIncludesPatientReference()
    {
        // Arrange
        var patientId = "patient-123";

        // Act
        var report = DiagnosticReportBuilder.Create(_schemaProvider)
            .WithCode("58410-2", "http://loinc.org")
            .WithSubject(patientId)
            .Build();

        // Assert
        var subject = report.MutableNode["subject"]?.AsObject();
        subject.Should().NotBeNull();
        subject?["reference"]?.GetValue<string>().Should().Be($"Patient/{patientId}");
    }

    [Fact]
    public void GivenDiagnosticReportBuilder_WhenBuildingWithoutSubject_ThenDoesNotIncludeSubject()
    {
        // Arrange & Act
        var report = DiagnosticReportBuilder.Create(_schemaProvider)
            .WithCode("58410-2", "http://loinc.org")
            .Build();

        // Assert
        report.MutableNode.TryGetPropertyValue("subject", out _).Should().BeFalse();
    }

    #endregion

    #region Result Tests

    [Fact]
    public void GivenDiagnosticReportBuilder_WhenBuildingWithSingleResult_ThenIncludesResultArray()
    {
        // Arrange
        var observationId = "obs-123";

        // Act
        var report = DiagnosticReportBuilder.Create(_schemaProvider)
            .WithCode("58410-2", "http://loinc.org", "CBC")
            .WithResult(observationId)
            .Build();

        // Assert
        var results = report.MutableNode["result"]?.AsArray();
        results.Should().NotBeNull();
        results.Should().HaveCount(1);

        var result = results?[0]?.AsObject();
        result?["reference"]?.GetValue<string>().Should().Be($"Observation/{observationId}");
    }

    [Fact]
    public void GivenDiagnosticReportBuilder_WhenBuildingWithMultipleResultsCalled_ThenIncludesAllResults()
    {
        // Arrange
        var obs1 = "obs-001";
        var obs2 = "obs-002";
        var obs3 = "obs-003";

        // Act
        var report = DiagnosticReportBuilder.Create(_schemaProvider)
            .WithCode("24331-1", "http://loinc.org", "Lipid panel")
            .WithResult(obs1)
            .WithResult(obs2)
            .WithResult(obs3)
            .Build();

        // Assert
        var results = report.MutableNode["result"]?.AsArray();
        results.Should().HaveCount(3);

        results?[0]?["reference"]?.GetValue<string>().Should().Be($"Observation/{obs1}");
        results?[1]?["reference"]?.GetValue<string>().Should().Be($"Observation/{obs2}");
        results?[2]?["reference"]?.GetValue<string>().Should().Be($"Observation/{obs3}");
    }

    [Fact]
    public void GivenDiagnosticReportBuilder_WhenBuildingWithResultsArray_ThenIncludesAllResults()
    {
        // Arrange
        var obs1 = "obs-cholesterol";
        var obs2 = "obs-hdl";
        var obs3 = "obs-ldl";
        var obs4 = "obs-triglycerides";

        // Act
        var report = DiagnosticReportBuilder.Create(_schemaProvider)
            .WithCode("24331-1", "http://loinc.org", "Lipid panel")
            .WithResults(obs1, obs2, obs3, obs4)
            .Build();

        // Assert
        var results = report.MutableNode["result"]?.AsArray();
        results.Should().HaveCount(4);

        results?[0]?["reference"]?.GetValue<string>().Should().Be($"Observation/{obs1}");
        results?[1]?["reference"]?.GetValue<string>().Should().Be($"Observation/{obs2}");
        results?[2]?["reference"]?.GetValue<string>().Should().Be($"Observation/{obs3}");
        results?[3]?["reference"]?.GetValue<string>().Should().Be($"Observation/{obs4}");
    }

    [Fact]
    public void GivenDiagnosticReportBuilder_WhenBuildingWithMixedResultMethods_ThenIncludesAllResults()
    {
        // Arrange
        var obs1 = "obs-001";
        var obs2 = "obs-002";
        var obs3 = "obs-003";
        var obs4 = "obs-004";

        // Act - Mix WithResult and WithResults
        var report = DiagnosticReportBuilder.Create(_schemaProvider)
            .WithCode("24323-8", "http://loinc.org", "CMP")
            .WithResult(obs1)
            .WithResults(obs2, obs3)
            .WithResult(obs4)
            .Build();

        // Assert
        var results = report.MutableNode["result"]?.AsArray();
        results.Should().HaveCount(4);

        results?[0]?["reference"]?.GetValue<string>().Should().Be($"Observation/{obs1}");
        results?[1]?["reference"]?.GetValue<string>().Should().Be($"Observation/{obs2}");
        results?[2]?["reference"]?.GetValue<string>().Should().Be($"Observation/{obs3}");
        results?[3]?["reference"]?.GetValue<string>().Should().Be($"Observation/{obs4}");
    }

    [Fact]
    public void GivenDiagnosticReportBuilder_WhenBuildingWithoutResults_ThenDoesNotIncludeResultArray()
    {
        // Arrange & Act
        var report = DiagnosticReportBuilder.Create(_schemaProvider)
            .WithCode("36554-4", "http://loinc.org", "Chest X-ray")
            .Build();

        // Assert
        report.MutableNode.TryGetPropertyValue("result", out _).Should().BeFalse();
    }

    #endregion

    #region Complex Scenarios

    [Fact]
    public void GivenDiagnosticReportBuilder_WhenBuildingCompleteDiagnosticReport_ThenIncludesAllProperties()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();
        var patientId = "patient-456";
        var obs1 = "obs-glucose";
        var obs2 = "obs-sodium";
        var obs3 = "obs-potassium";

        // Act
        var report = DiagnosticReportBuilder.Create(_schemaProvider)
            .WithId("report-complete")
            .WithStatus("final")
            .WithCode("24323-8", "http://loinc.org", "Comprehensive metabolic panel")
            .WithSubject(patientId)
            .WithResults(obs1, obs2, obs3)
            .WithTag(tag)
            .Build();

        // Assert
        report.Id.Should().Be("report-complete");
        report.MutableNode["status"]?.GetValue<string>().Should().Be("final");

        var code = report.MutableNode["code"]?.AsObject();
        var coding = code?["coding"]?.AsArray()?[0]?.AsObject();
        coding?["code"]?.GetValue<string>().Should().Be("24323-8");
        coding?["display"]?.GetValue<string>().Should().Be("Comprehensive metabolic panel");

        var subject = report.MutableNode["subject"]?.AsObject();
        subject?["reference"]?.GetValue<string>().Should().Be($"Patient/{patientId}");

        var results = report.MutableNode["result"]?.AsArray();
        results.Should().HaveCount(3);

        var tags = report.MutableNode["meta"]?["tag"]?.AsArray();
        tags?[0]?["code"]?.GetValue<string>().Should().Be(tag);
    }

    [Fact]
    public void GivenDiagnosticReportBuilder_WhenBuildingLabReport_ThenCreatesValidLabReport()
    {
        // Arrange
        var patientId = "patient-lab";
        var wbcObs = "obs-wbc";
        var rbcObs = "obs-rbc";
        var hgbObs = "obs-hgb";
        var hctObs = "obs-hct";

        // Act
        var report = DiagnosticReportBuilder.Create(_schemaProvider)
            .WithCode("58410-2", "http://loinc.org", "Complete blood count (hemogram) panel - Blood by Automated count")
            .WithStatus("final")
            .WithSubject(patientId)
            .WithResults(wbcObs, rbcObs, hgbObs, hctObs)
            .Build();

        // Assert
        report.ResourceType.Should().Be("DiagnosticReport");
        report.MutableNode["status"]?.GetValue<string>().Should().Be("final");

        var subject = report.MutableNode["subject"]?.AsObject();
        subject?["reference"]?.GetValue<string>().Should().Be($"Patient/{patientId}");

        var results = report.MutableNode["result"]?.AsArray();
        results.Should().HaveCount(4);
    }

    [Fact]
    public void GivenDiagnosticReportBuilder_WhenBuildingRadiologyReport_ThenCreatesValidRadiologyReport()
    {
        // Arrange
        var patientId = "patient-radiology";
        var imagingObs = "obs-chest-xray-findings";

        // Act
        var report = DiagnosticReportBuilder.Create(_schemaProvider)
            .WithCode("36554-4", "http://loinc.org", "Chest X-ray")
            .WithStatus("final")
            .WithSubject(patientId)
            .WithResult(imagingObs)
            .Build();

        // Assert
        report.ResourceType.Should().Be("DiagnosticReport");

        var code = report.MutableNode["code"]?.AsObject();
        var coding = code?["coding"]?.AsArray()?[0]?.AsObject();
        coding?["code"]?.GetValue<string>().Should().Be("36554-4");
        coding?["display"]?.GetValue<string>().Should().Be("Chest X-ray");

        var results = report.MutableNode["result"]?.AsArray();
        results.Should().HaveCount(1);
        results?[0]?["reference"]?.GetValue<string>().Should().Be($"Observation/{imagingObs}");
    }

    [Fact]
    public void GivenDiagnosticReportBuilder_WhenBuildingPreliminaryReport_ThenCreatesValidPreliminaryReport()
    {
        // Arrange
        var patientId = "patient-prelim";
        var obs1 = "obs-partial-1";

        // Act
        var report = DiagnosticReportBuilder.Create(_schemaProvider)
            .WithCode("24331-1", "http://loinc.org", "Lipid panel")
            .WithStatus("preliminary")
            .WithSubject(patientId)
            .WithResult(obs1)
            .Build();

        // Assert
        report.MutableNode["status"]?.GetValue<string>().Should().Be("preliminary");

        var results = report.MutableNode["result"]?.AsArray();
        results.Should().HaveCount(1);
    }

    [Fact]
    public void GivenDiagnosticReportBuilder_WhenBuildingMultipleReports_ThenGeneratesDifferentIds()
    {
        // Arrange & Act
        var report1 = DiagnosticReportBuilder.Create(_schemaProvider)
            .WithCode("58410-2", "http://loinc.org")
            .Build();

        var report2 = DiagnosticReportBuilder.Create(_schemaProvider)
            .WithCode("24331-1", "http://loinc.org")
            .Build();

        // Assert
        report1.Id.Should().NotBe(report2.Id);
    }

    #endregion

    #region Meta Tests

    [Fact]
    public void GivenDiagnosticReportBuilder_WhenBuilding_ThenIncludesMetaVersionAndLastUpdated()
    {
        // Arrange & Act
        var report = DiagnosticReportBuilder.Create(_schemaProvider)
            .WithCode("58410-2", "http://loinc.org")
            .Build();

        // Assert
        report.MutableNode["meta"].Should().NotBeNull();
        var meta = report.MutableNode["meta"]?.AsObject();
        meta?["versionId"]?.GetValue<string>().Should().Be("1");
        meta?["lastUpdated"]?.GetValue<string>().Should().NotBeNullOrEmpty();
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void GivenDiagnosticReportBuilder_WhenBuildingMinimal_ThenCreatesValidDiagnosticReport()
    {
        // Arrange & Act
        var report = DiagnosticReportBuilder.Create(_schemaProvider)
            .WithCode("58410-2", "http://loinc.org")
            .Build();

        // Assert
        report.Should().NotBeNull();
        report.ResourceType.Should().Be("DiagnosticReport");
        report.Id.Should().NotBeNullOrEmpty();
        report.MutableNode["status"]?.GetValue<string>().Should().Be("final");

        var code = report.MutableNode["code"]?.AsObject();
        code.Should().NotBeNull();
    }

    [Fact]
    public void GivenDiagnosticReportBuilder_WhenBuildingWithEmptyResultsArray_ThenDoesNotIncludeResultArray()
    {
        // Arrange & Act
        var report = DiagnosticReportBuilder.Create(_schemaProvider)
            .WithCode("58410-2", "http://loinc.org")
            .Build();

        // Assert
        report.MutableNode.TryGetPropertyValue("result", out _).Should().BeFalse();
    }

    [Fact]
    public void GivenDiagnosticReportBuilder_WhenBuildingPathologyReport_ThenCreatesValidPathologyReport()
    {
        // Arrange
        var patientId = "patient-path";
        var pathObs = "obs-biopsy-findings";

        // Act
        var report = DiagnosticReportBuilder.Create(_schemaProvider)
            .WithCode("60568-3", "http://loinc.org", "Pathology Synoptic report")
            .WithStatus("final")
            .WithSubject(patientId)
            .WithResult(pathObs)
            .Build();

        // Assert
        report.ResourceType.Should().Be("DiagnosticReport");

        var code = report.MutableNode["code"]?.AsObject();
        var coding = code?["coding"]?.AsArray()?[0]?.AsObject();
        coding?["code"]?.GetValue<string>().Should().Be("60568-3");
        coding?["display"]?.GetValue<string>().Should().Be("Pathology Synoptic report");
    }

    #endregion
}
