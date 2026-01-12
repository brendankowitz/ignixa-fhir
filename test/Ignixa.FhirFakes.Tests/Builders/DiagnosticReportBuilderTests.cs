// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.FhirFakes.Builders;
using Ignixa.Abstractions;
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
        report.ShouldNotBeNull();
        report.ResourceType.ShouldBe("DiagnosticReport");
        report.Id.ShouldNotBeNullOrEmpty();
        report.MutableNode["status"]?.GetValue<string>().ShouldBe("final");

        var code = report.MutableNode["code"]?.AsObject();
        code.ShouldNotBeNull();
        var coding = code?["coding"]?.AsArray()?[0]?.AsObject();
        coding?["system"]?.GetValue<string>().ShouldBe("http://loinc.org");
        coding?["code"]?.GetValue<string>().ShouldBe("58410-2");
        coding?["display"]?.GetValue<string>().ShouldBe("Complete blood count");
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
        report.Id.ShouldBe(expectedId);
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
        report.MutableNode["meta"]?["tag"].ShouldNotBeNull();
        var tags = report.MutableNode["meta"]?["tag"]?.AsArray();
        tags!.Count.ShouldBe(1);

        var metaTag = tags?[0]?.AsObject();
        metaTag?["code"]?.GetValue<string>().ShouldBe(tag);
        metaTag?["system"]?.GetValue<string>().ShouldBe("http://ignixa.dev/test-isolation");
    }

    [Fact]
    public void GivenDiagnosticReportBuilder_WhenBuildingWithoutCode_ThenThrowsInvalidOperationException()
    {
        // Arrange & Act
        var act = () => DiagnosticReportBuilder.Create(_schemaProvider)
            .WithStatus("final")
            .Build();

        // Assert
        Should.Throw<InvalidOperationException>(act).Message.ShouldContain("code is required");
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
        report.MutableNode["status"]?.GetValue<string>().ShouldBe("final");
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
        report.MutableNode["status"]?.GetValue<string>().ShouldBe("preliminary");
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
        report.MutableNode["status"]?.GetValue<string>().ShouldBe("amended");
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
        report.MutableNode["status"]?.GetValue<string>().ShouldBe("corrected");
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
        report.MutableNode["status"]?.GetValue<string>().ShouldBe("cancelled");
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

        coding?["system"]?.GetValue<string>().ShouldBe("http://loinc.org");
        coding?["code"]?.GetValue<string>().ShouldBe("58410-2");
        coding?.TryGetPropertyValue("display", out _).ShouldBeFalse();
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

        coding?["system"]?.GetValue<string>().ShouldBe("http://snomed.info/sct");
        coding?["code"]?.GetValue<string>().ShouldBe("168537006");
        coding?["display"]?.GetValue<string>().ShouldBe("Microscopy");
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

        coding?["system"]?.GetValue<string>().ShouldBe("http://loinc.org");
        coding?["code"]?.GetValue<string>().ShouldBe("24331-1");
        coding?["display"]?.GetValue<string>().ShouldBe("Lipid panel with direct LDL");
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
        subject.ShouldNotBeNull();
        subject?["reference"]?.GetValue<string>().ShouldBe($"Patient/{patientId}");
    }

    [Fact]
    public void GivenDiagnosticReportBuilder_WhenBuildingWithoutSubject_ThenDoesNotIncludeSubject()
    {
        // Arrange & Act
        var report = DiagnosticReportBuilder.Create(_schemaProvider)
            .WithCode("58410-2", "http://loinc.org")
            .Build();

        // Assert
        report.MutableNode.TryGetPropertyValue("subject", out _).ShouldBeFalse();
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
        results.ShouldNotBeNull();
        results!.Count.ShouldBe(1);

        var result = results?[0]?.AsObject();
        result?["reference"]?.GetValue<string>().ShouldBe($"Observation/{observationId}");
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
        results!.Count.ShouldBe(3);

        results?[0]?["reference"]?.GetValue<string>().ShouldBe($"Observation/{obs1}");
        results?[1]?["reference"]?.GetValue<string>().ShouldBe($"Observation/{obs2}");
        results?[2]?["reference"]?.GetValue<string>().ShouldBe($"Observation/{obs3}");
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
        results!.Count.ShouldBe(4);

        results?[0]?["reference"]?.GetValue<string>().ShouldBe($"Observation/{obs1}");
        results?[1]?["reference"]?.GetValue<string>().ShouldBe($"Observation/{obs2}");
        results?[2]?["reference"]?.GetValue<string>().ShouldBe($"Observation/{obs3}");
        results?[3]?["reference"]?.GetValue<string>().ShouldBe($"Observation/{obs4}");
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
        results!.Count.ShouldBe(4);

        results?[0]?["reference"]?.GetValue<string>().ShouldBe($"Observation/{obs1}");
        results?[1]?["reference"]?.GetValue<string>().ShouldBe($"Observation/{obs2}");
        results?[2]?["reference"]?.GetValue<string>().ShouldBe($"Observation/{obs3}");
        results?[3]?["reference"]?.GetValue<string>().ShouldBe($"Observation/{obs4}");
    }

    [Fact]
    public void GivenDiagnosticReportBuilder_WhenBuildingWithoutResults_ThenDoesNotIncludeResultArray()
    {
        // Arrange & Act
        var report = DiagnosticReportBuilder.Create(_schemaProvider)
            .WithCode("36554-4", "http://loinc.org", "Chest X-ray")
            .Build();

        // Assert
        report.MutableNode.TryGetPropertyValue("result", out _).ShouldBeFalse();
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
        report.Id.ShouldBe("report-complete");
        report.MutableNode["status"]?.GetValue<string>().ShouldBe("final");

        var code = report.MutableNode["code"]?.AsObject();
        var coding = code?["coding"]?.AsArray()?[0]?.AsObject();
        coding?["code"]?.GetValue<string>().ShouldBe("24323-8");
        coding?["display"]?.GetValue<string>().ShouldBe("Comprehensive metabolic panel");

        var subject = report.MutableNode["subject"]?.AsObject();
        subject?["reference"]?.GetValue<string>().ShouldBe($"Patient/{patientId}");

        var results = report.MutableNode["result"]?.AsArray();
        results!.Count.ShouldBe(3);

        var tags = report.MutableNode["meta"]?["tag"]?.AsArray();
        tags?[0]?["code"]?.GetValue<string>().ShouldBe(tag);
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
        report.ResourceType.ShouldBe("DiagnosticReport");
        report.MutableNode["status"]?.GetValue<string>().ShouldBe("final");

        var subject = report.MutableNode["subject"]?.AsObject();
        subject?["reference"]?.GetValue<string>().ShouldBe($"Patient/{patientId}");

        var results = report.MutableNode["result"]?.AsArray();
        results!.Count.ShouldBe(4);
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
        report.ResourceType.ShouldBe("DiagnosticReport");

        var code = report.MutableNode["code"]?.AsObject();
        var coding = code?["coding"]?.AsArray()?[0]?.AsObject();
        coding?["code"]?.GetValue<string>().ShouldBe("36554-4");
        coding?["display"]?.GetValue<string>().ShouldBe("Chest X-ray");

        var results = report.MutableNode["result"]?.AsArray();
        results!.Count.ShouldBe(1);
        results?[0]?["reference"]?.GetValue<string>().ShouldBe($"Observation/{imagingObs}");
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
        report.MutableNode["status"]?.GetValue<string>().ShouldBe("preliminary");

        var results = report.MutableNode["result"]?.AsArray();
        results!.Count.ShouldBe(1);
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
        report1.Id.ShouldNotBe(report2.Id);
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
        report.MutableNode["meta"].ShouldNotBeNull();
        var meta = report.MutableNode["meta"]?.AsObject();
        meta?["versionId"]?.GetValue<string>().ShouldBe("1");
        meta?["lastUpdated"]?.GetValue<string>().ShouldNotBeNullOrEmpty();
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
        report.ShouldNotBeNull();
        report.ResourceType.ShouldBe("DiagnosticReport");
        report.Id.ShouldNotBeNullOrEmpty();
        report.MutableNode["status"]?.GetValue<string>().ShouldBe("final");

        var code = report.MutableNode["code"]?.AsObject();
        code.ShouldNotBeNull();
    }

    [Fact]
    public void GivenDiagnosticReportBuilder_WhenBuildingWithEmptyResultsArray_ThenDoesNotIncludeResultArray()
    {
        // Arrange & Act
        var report = DiagnosticReportBuilder.Create(_schemaProvider)
            .WithCode("58410-2", "http://loinc.org")
            .Build();

        // Assert
        report.MutableNode.TryGetPropertyValue("result", out _).ShouldBeFalse();
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
        report.ResourceType.ShouldBe("DiagnosticReport");

        var code = report.MutableNode["code"]?.AsObject();
        var coding = code?["coding"]?.AsArray()?[0]?.AsObject();
        coding?["code"]?.GetValue<string>().ShouldBe("60568-3");
        coding?["display"]?.GetValue<string>().ShouldBe("Pathology Synoptic report");
    }

    #endregion
}
