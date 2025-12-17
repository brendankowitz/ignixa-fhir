// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.FhirFakes.Builders;
using Ignixa.Specification;
using Ignixa.Specification.Generated;
using Xunit;

namespace Ignixa.FhirFakes.Tests.Builders;

/// <summary>
/// Unit tests for MedicationDispenseBuilder.
/// Tests basic dispense generation with medications, prescriptions, and performers.
/// </summary>
public class MedicationDispenseBuilderTests
{
    private readonly IFhirSchemaProvider _schemaProvider = new R4CoreSchemaProvider();

    #region Basic Building Tests

    [Fact]
    public void GivenMedicationDispenseBuilder_WhenBuildingWithCodeableConcept_ThenCreatesDispense()
    {
        // Arrange & Act
        var dispense = MedicationDispenseBuilder.Create(_schemaProvider)
            .WithMedicationCodeableConcept("108505002", "http://snomed.info/sct", "Aspirin")
            .Build();

        // Assert
        dispense.ShouldNotBeNull();
        dispense.ResourceType.ShouldBe("MedicationDispense");
        dispense.MutableNode["status"]?.GetValue<string>().ShouldBe("completed");

        var medication = dispense.MutableNode["medicationCodeableConcept"]?.AsObject();
        medication.ShouldNotBeNull();

        var coding = medication?["coding"]?.AsArray()?[0]?.AsObject();
        coding?["code"]?.GetValue<string>().ShouldBe("108505002");
        coding?["system"]?.GetValue<string>().ShouldBe("http://snomed.info/sct");
        coding?["display"]?.GetValue<string>().ShouldBe("Aspirin");
    }

    [Fact]
    public void GivenMedicationDispenseBuilder_WhenBuildingWithReference_ThenCreatesDispense()
    {
        // Arrange
        var medicationId = "medication-123";

        // Act
        var dispense = MedicationDispenseBuilder.Create(_schemaProvider)
            .WithMedicationReference(medicationId)
            .Build();

        // Assert
        dispense.ShouldNotBeNull();
        dispense.ResourceType.ShouldBe("MedicationDispense");

        var medicationRef = dispense.MutableNode["medicationReference"]?.AsObject();
        medicationRef?["reference"]?.GetValue<string>().ShouldBe($"Medication/{medicationId}");
    }

    [Fact]
    public void GivenMedicationDispenseBuilder_WhenBuildingWithoutMedication_ThenThrowsException()
    {
        // Arrange
        var builder = MedicationDispenseBuilder.Create(_schemaProvider);

        // Act
        var act = () => builder.Build();

        // Assert
        Should.Throw<InvalidOperationException>(act).Message.ShouldContain("Medication is required");
    }

    [Fact]
    public void GivenMedicationDispenseBuilder_WhenBuildingWithId_ThenUsesProvidedId()
    {
        // Arrange
        var expectedId = "dispense-123";

        // Act
        var dispense = MedicationDispenseBuilder.Create(_schemaProvider)
            .WithId(expectedId)
            .WithMedicationCodeableConcept("108505002", "http://snomed.info/sct")
            .Build();

        // Assert
        dispense.Id.ShouldBe(expectedId);
    }

    [Fact]
    public void GivenMedicationDispenseBuilder_WhenBuildingWithTag_ThenIncludesTagInMeta()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Act
        var dispense = MedicationDispenseBuilder.Create(_schemaProvider)
            .WithMedicationCodeableConcept("108505002", "http://snomed.info/sct")
            .WithTag(tag)
            .Build();

        // Assert
        dispense.MutableNode["meta"]?["tag"].ShouldNotBeNull();
        var tags = dispense.MutableNode["meta"]?["tag"]?.AsArray();
        tags!.Count.ShouldBe(1);

        var metaTag = tags?[0]?.AsObject();
        metaTag?["code"]?.GetValue<string>().ShouldBe(tag);
        metaTag?["system"]?.GetValue<string>().ShouldBe("http://ignixa.dev/test-isolation");
    }

    #endregion

    #region Status Tests

    [Fact]
    public void GivenMedicationDispenseBuilder_WhenBuildingWithDefaultStatus_ThenUsesCompleted()
    {
        // Arrange & Act
        var dispense = MedicationDispenseBuilder.Create(_schemaProvider)
            .WithMedicationCodeableConcept("108505002", "http://snomed.info/sct")
            .Build();

        // Assert
        dispense.MutableNode["status"]?.GetValue<string>().ShouldBe("completed");
    }

    [Fact]
    public void GivenMedicationDispenseBuilder_WhenBuildingWithCustomStatus_ThenUsesProvidedStatus()
    {
        // Arrange & Act
        var dispense = MedicationDispenseBuilder.Create(_schemaProvider)
            .WithStatus("in-progress")
            .WithMedicationCodeableConcept("108505002", "http://snomed.info/sct")
            .Build();

        // Assert
        dispense.MutableNode["status"]?.GetValue<string>().ShouldBe("in-progress");
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("in-progress")]
    [InlineData("stopped")]
    [InlineData("on-hold")]
    public void GivenMedicationDispenseBuilder_WhenBuildingWithVariousStatuses_ThenUsesCorrectStatus(string status)
    {
        // Arrange & Act
        var dispense = MedicationDispenseBuilder.Create(_schemaProvider)
            .WithStatus(status)
            .WithMedicationCodeableConcept("108505002", "http://snomed.info/sct")
            .Build();

        // Assert
        dispense.MutableNode["status"]?.GetValue<string>().ShouldBe(status);
    }

    #endregion

    #region Medication Tests

    [Fact]
    public void GivenMedicationDispenseBuilder_WhenSwitchingFromCodeableConceptToReference_ThenClearsCodeableConcept()
    {
        // Arrange
        var medicationId = "medication-456";

        // Act
        var dispense = MedicationDispenseBuilder.Create(_schemaProvider)
            .WithMedicationCodeableConcept("108505002", "http://snomed.info/sct", "Aspirin")
            .WithMedicationReference(medicationId) // Switch to reference
            .Build();

        // Assert
        dispense.MutableNode.TryGetPropertyValue("medicationCodeableConcept", out _).ShouldBeFalse();
        var medicationRef = dispense.MutableNode["medicationReference"]?.AsObject();
        medicationRef?["reference"]?.GetValue<string>().ShouldBe($"Medication/{medicationId}");
    }

    [Fact]
    public void GivenMedicationDispenseBuilder_WhenSwitchingFromReferenceToCodeableConcept_ThenClearsReference()
    {
        // Arrange
        var medicationId = "medication-456";

        // Act
        var dispense = MedicationDispenseBuilder.Create(_schemaProvider)
            .WithMedicationReference(medicationId)
            .WithMedicationCodeableConcept("108505002", "http://snomed.info/sct", "Aspirin") // Switch to CodeableConcept
            .Build();

        // Assert
        dispense.MutableNode.TryGetPropertyValue("medicationReference", out _).ShouldBeFalse();
        var medication = dispense.MutableNode["medicationCodeableConcept"]?.AsObject();
        var coding = medication?["coding"]?.AsArray()?[0]?.AsObject();
        coding?["code"]?.GetValue<string>().ShouldBe("108505002");
    }

    [Fact]
    public void GivenMedicationDispenseBuilder_WhenBuildingWithRxNormCode_ThenCreatesDispenseWithRxNorm()
    {
        // Arrange & Act
        var dispense = MedicationDispenseBuilder.Create(_schemaProvider)
            .WithMedicationCodeableConcept("197361", "http://www.nlm.nih.gov/research/umls/rxnorm", "Lisinopril")
            .Build();

        // Assert
        var medication = dispense.MutableNode["medicationCodeableConcept"]?.AsObject();
        var coding = medication?["coding"]?.AsArray()?[0]?.AsObject();
        coding?["code"]?.GetValue<string>().ShouldBe("197361");
        coding?["system"]?.GetValue<string>().ShouldBe("http://www.nlm.nih.gov/research/umls/rxnorm");
        coding?["display"]?.GetValue<string>().ShouldBe("Lisinopril");
    }

    #endregion

    #region Subject Tests

    [Fact]
    public void GivenMedicationDispenseBuilder_WhenBuildingWithSubject_ThenIncludesSubjectReference()
    {
        // Arrange
        var patientId = "patient-789";

        // Act
        var dispense = MedicationDispenseBuilder.Create(_schemaProvider)
            .WithSubject(patientId)
            .WithMedicationCodeableConcept("108505002", "http://snomed.info/sct")
            .Build();

        // Assert
        var subject = dispense.MutableNode["subject"]?.AsObject();
        subject?["reference"]?.GetValue<string>().ShouldBe($"Patient/{patientId}");
    }

    [Fact]
    public void GivenMedicationDispenseBuilder_WhenBuildingWithoutSubject_ThenDoesNotIncludeSubject()
    {
        // Arrange & Act
        var dispense = MedicationDispenseBuilder.Create(_schemaProvider)
            .WithMedicationCodeableConcept("108505002", "http://snomed.info/sct")
            .Build();

        // Assert
        dispense.MutableNode.TryGetPropertyValue("subject", out _).ShouldBeFalse();
    }

    #endregion

    #region Prescription Tests

    [Fact]
    public void GivenMedicationDispenseBuilder_WhenBuildingWithAuthorizingPrescription_ThenIncludesPrescriptionReference()
    {
        // Arrange
        var prescriptionId = "medreq-100";

        // Act
        var dispense = MedicationDispenseBuilder.Create(_schemaProvider)
            .WithMedicationCodeableConcept("108505002", "http://snomed.info/sct")
            .WithAuthorizingPrescription(prescriptionId)
            .Build();

        // Assert
        var prescriptions = dispense.MutableNode["authorizingPrescription"]?.AsArray();
        prescriptions!.Count.ShouldBe(1);

        var prescription = prescriptions?[0]?.AsObject();
        prescription?["reference"]?.GetValue<string>().ShouldBe($"MedicationRequest/{prescriptionId}");
    }

    [Fact]
    public void GivenMedicationDispenseBuilder_WhenBuildingWithMultiplePrescriptions_ThenIncludesAllReferences()
    {
        // Arrange
        var prescription1 = "medreq-101";
        var prescription2 = "medreq-102";

        // Act
        var dispense = MedicationDispenseBuilder.Create(_schemaProvider)
            .WithMedicationCodeableConcept("108505002", "http://snomed.info/sct")
            .WithAuthorizingPrescription(prescription1)
            .WithAuthorizingPrescription(prescription2)
            .Build();

        // Assert
        var prescriptions = dispense.MutableNode["authorizingPrescription"]?.AsArray();
        prescriptions!.Count.ShouldBe(2);

        var ref1 = prescriptions?[0]?.AsObject()?["reference"]?.GetValue<string>();
        var ref2 = prescriptions?[1]?.AsObject()?["reference"]?.GetValue<string>();

        ref1.ShouldBe($"MedicationRequest/{prescription1}");
        ref2.ShouldBe($"MedicationRequest/{prescription2}");
    }

    [Fact]
    public void GivenMedicationDispenseBuilder_WhenBuildingWithoutPrescription_ThenDoesNotIncludePrescription()
    {
        // Arrange & Act
        var dispense = MedicationDispenseBuilder.Create(_schemaProvider)
            .WithMedicationCodeableConcept("108505002", "http://snomed.info/sct")
            .Build();

        // Assert
        dispense.MutableNode.TryGetPropertyValue("authorizingPrescription", out _).ShouldBeFalse();
    }

    #endregion

    #region Performer Tests

    [Fact]
    public void GivenMedicationDispenseBuilder_WhenBuildingWithPerformer_ThenIncludesPerformerReference()
    {
        // Arrange
        var practitionerId = "practitioner-200";

        // Act
        var dispense = MedicationDispenseBuilder.Create(_schemaProvider)
            .WithMedicationCodeableConcept("108505002", "http://snomed.info/sct")
            .WithPerformer(practitionerId)
            .Build();

        // Assert
        var performers = dispense.MutableNode["performer"]?.AsArray();
        performers!.Count.ShouldBe(1);

        var performer = performers?[0]?.AsObject();
        var actor = performer?["actor"]?.AsObject();
        actor?["reference"]?.GetValue<string>().ShouldBe($"Practitioner/{practitionerId}");
    }

    [Fact]
    public void GivenMedicationDispenseBuilder_WhenBuildingWithMultiplePerformers_ThenIncludesAllPerformers()
    {
        // Arrange
        var practitioner1 = "practitioner-201";
        var practitioner2 = "practitioner-202";

        // Act
        var dispense = MedicationDispenseBuilder.Create(_schemaProvider)
            .WithMedicationCodeableConcept("108505002", "http://snomed.info/sct")
            .WithPerformer(practitioner1)
            .WithPerformer(practitioner2)
            .Build();

        // Assert
        var performers = dispense.MutableNode["performer"]?.AsArray();
        performers!.Count.ShouldBe(2);

        var actor1 = performers?[0]?.AsObject()?["actor"]?.AsObject()?["reference"]?.GetValue<string>();
        var actor2 = performers?[1]?.AsObject()?["actor"]?.AsObject()?["reference"]?.GetValue<string>();

        actor1.ShouldBe($"Practitioner/{practitioner1}");
        actor2.ShouldBe($"Practitioner/{practitioner2}");
    }

    [Fact]
    public void GivenMedicationDispenseBuilder_WhenBuildingWithoutPerformer_ThenDoesNotIncludePerformer()
    {
        // Arrange & Act
        var dispense = MedicationDispenseBuilder.Create(_schemaProvider)
            .WithMedicationCodeableConcept("108505002", "http://snomed.info/sct")
            .Build();

        // Assert
        dispense.MutableNode.TryGetPropertyValue("performer", out _).ShouldBeFalse();
    }

    #endregion

    #region Timing Tests

    [Fact]
    public void GivenMedicationDispenseBuilder_WhenBuildingWithWhenHandedOver_ThenIncludesTiming()
    {
        // Arrange
        var timestamp = "2024-01-15T10:30:00Z";

        // Act
        var dispense = MedicationDispenseBuilder.Create(_schemaProvider)
            .WithMedicationCodeableConcept("108505002", "http://snomed.info/sct")
            .WithWhenHandedOver(timestamp)
            .Build();

        // Assert
        dispense.MutableNode["whenHandedOver"]?.GetValue<string>().ShouldBe(timestamp);
    }

    [Fact]
    public void GivenMedicationDispenseBuilder_WhenBuildingWithoutWhenHandedOver_ThenDoesNotIncludeTiming()
    {
        // Arrange & Act
        var dispense = MedicationDispenseBuilder.Create(_schemaProvider)
            .WithMedicationCodeableConcept("108505002", "http://snomed.info/sct")
            .Build();

        // Assert
        dispense.MutableNode.TryGetPropertyValue("whenHandedOver", out _).ShouldBeFalse();
    }

    #endregion

    #region Complex Scenarios

    [Fact]
    public void GivenMedicationDispenseBuilder_WhenBuildingCompleteDispense_ThenIncludesAllProperties()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();
        var patientId = "patient-999";
        var prescriptionId = "medreq-999";
        var practitionerId = "practitioner-999";
        var timestamp = "2024-01-15T14:30:00Z";

        // Act
        var dispense = MedicationDispenseBuilder.Create(_schemaProvider)
            .WithId("dispense-complete")
            .WithStatus("completed")
            .WithSubject(patientId)
            .WithMedicationCodeableConcept("197361", "http://www.nlm.nih.gov/research/umls/rxnorm", "Lisinopril")
            .WithAuthorizingPrescription(prescriptionId)
            .WithPerformer(practitionerId)
            .WithWhenHandedOver(timestamp)
            .WithTag(tag)
            .Build();

        // Assert
        dispense.Id.ShouldBe("dispense-complete");
        dispense.MutableNode["status"]?.GetValue<string>().ShouldBe("completed");

        var subject = dispense.MutableNode["subject"]?.AsObject();
        subject?["reference"]?.GetValue<string>().ShouldBe($"Patient/{patientId}");

        var medication = dispense.MutableNode["medicationCodeableConcept"]?.AsObject();
        var coding = medication?["coding"]?.AsArray()?[0]?.AsObject();
        coding?["code"]?.GetValue<string>().ShouldBe("197361");

        var prescriptions = dispense.MutableNode["authorizingPrescription"]?.AsArray();
        prescriptions?[0]?.AsObject()?["reference"]?.GetValue<string>().ShouldBe($"MedicationRequest/{prescriptionId}");

        var performers = dispense.MutableNode["performer"]?.AsArray();
        var actor = performers?[0]?.AsObject()?["actor"]?.AsObject();
        actor?["reference"]?.GetValue<string>().ShouldBe($"Practitioner/{practitionerId}");

        dispense.MutableNode["whenHandedOver"]?.GetValue<string>().ShouldBe(timestamp);

        var tags = dispense.MutableNode["meta"]?["tag"]?.AsArray();
        tags?[0]?["code"]?.GetValue<string>().ShouldBe(tag);
    }

    [Fact]
    public void GivenMedicationDispenseBuilder_WhenBuildingMultipleDispenses_ThenGeneratesDifferentIds()
    {
        // Arrange & Act
        var dispense1 = MedicationDispenseBuilder.Create(_schemaProvider)
            .WithMedicationCodeableConcept("108505002", "http://snomed.info/sct")
            .Build();

        var dispense2 = MedicationDispenseBuilder.Create(_schemaProvider)
            .WithMedicationCodeableConcept("108505002", "http://snomed.info/sct")
            .Build();

        // Assert
        dispense1.Id.ShouldNotBe(dispense2.Id);
    }

    [Fact]
    public void GivenMedicationDispenseBuilder_WhenBuildingFromIncludeTestPattern_ThenMatchesExpectedStructure()
    {
        // Arrange - Based on IncludeTestBase.cs line 425
        var tag = Guid.NewGuid().ToString();
        var patientId = "patient-include-test";
        var medicationRequestId = "medreq-include-test";

        // Act
        var dispense = MedicationDispenseBuilder.Create(_schemaProvider)
            .WithTag(tag)
            .WithStatus("in-progress")
            .WithSubject(patientId)
            .WithMedicationCodeableConcept("108505002", "http://snomed.info/sct")
            .WithAuthorizingPrescription(medicationRequestId)
            .Build();

        // Assert
        dispense.ShouldNotBeNull();
        dispense.ResourceType.ShouldBe("MedicationDispense");
        dispense.MutableNode["status"]?.GetValue<string>().ShouldBe("in-progress");

        var subject = dispense.MutableNode["subject"]?.AsObject();
        subject?["reference"]?.GetValue<string>().ShouldBe($"Patient/{patientId}");

        var medication = dispense.MutableNode["medicationCodeableConcept"]?.AsObject();
        var coding = medication?["coding"]?.AsArray()?[0]?.AsObject();
        coding?["code"]?.GetValue<string>().ShouldBe("108505002");
        coding?["system"]?.GetValue<string>().ShouldBe("http://snomed.info/sct");

        var prescriptions = dispense.MutableNode["authorizingPrescription"]?.AsArray();
        prescriptions?[0]?.AsObject()?["reference"]?.GetValue<string>()
            .ShouldBe($"MedicationRequest/{medicationRequestId}");

        var tags = dispense.MutableNode["meta"]?["tag"]?.AsArray();
        tags?[0]?["code"]?.GetValue<string>().ShouldBe(tag);
    }

    #endregion

    #region Meta Tests

    [Fact]
    public void GivenMedicationDispenseBuilder_WhenBuilding_ThenIncludesMetaVersionAndLastUpdated()
    {
        // Arrange & Act
        var dispense = MedicationDispenseBuilder.Create(_schemaProvider)
            .WithMedicationCodeableConcept("108505002", "http://snomed.info/sct")
            .Build();

        // Assert
        dispense.MutableNode["meta"].ShouldNotBeNull();
        var meta = dispense.MutableNode["meta"]?.AsObject();
        meta?["versionId"]?.GetValue<string>().ShouldBe("1");
        meta?["lastUpdated"]?.GetValue<string>().ShouldNotBeNullOrEmpty();
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void GivenMedicationDispenseBuilder_WhenBuildingMinimal_ThenCreatesValidDispense()
    {
        // Arrange & Act
        var dispense = MedicationDispenseBuilder.Create(_schemaProvider)
            .WithMedicationCodeableConcept("108505002", "http://snomed.info/sct")
            .Build();

        // Assert
        dispense.ShouldNotBeNull();
        dispense.Id.ShouldNotBeNullOrEmpty();
        dispense.ResourceType.ShouldBe("MedicationDispense");
        dispense.MutableNode["status"]?.GetValue<string>().ShouldBe("completed");
    }

    #endregion
}
