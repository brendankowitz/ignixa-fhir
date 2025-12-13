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
        dispense.Should().NotBeNull();
        dispense.ResourceType.Should().Be("MedicationDispense");
        dispense.MutableNode["status"]?.GetValue<string>().Should().Be("completed");

        var medication = dispense.MutableNode["medicationCodeableConcept"]?.AsObject();
        medication.Should().NotBeNull();

        var coding = medication?["coding"]?.AsArray()?[0]?.AsObject();
        coding?["code"]?.GetValue<string>().Should().Be("108505002");
        coding?["system"]?.GetValue<string>().Should().Be("http://snomed.info/sct");
        coding?["display"]?.GetValue<string>().Should().Be("Aspirin");
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
        dispense.Should().NotBeNull();
        dispense.ResourceType.Should().Be("MedicationDispense");

        var medicationRef = dispense.MutableNode["medicationReference"]?.AsObject();
        medicationRef?["reference"]?.GetValue<string>().Should().Be($"Medication/{medicationId}");
    }

    [Fact]
    public void GivenMedicationDispenseBuilder_WhenBuildingWithoutMedication_ThenThrowsException()
    {
        // Arrange
        var builder = MedicationDispenseBuilder.Create(_schemaProvider);

        // Act
        var act = () => builder.Build();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Medication is required*");
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
        dispense.Id.Should().Be(expectedId);
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
        dispense.MutableNode["meta"]?["tag"].Should().NotBeNull();
        var tags = dispense.MutableNode["meta"]?["tag"]?.AsArray();
        tags.Should().HaveCount(1);

        var metaTag = tags?[0]?.AsObject();
        metaTag?["code"]?.GetValue<string>().Should().Be(tag);
        metaTag?["system"]?.GetValue<string>().Should().Be("http://ignixa.dev/test-isolation");
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
        dispense.MutableNode["status"]?.GetValue<string>().Should().Be("completed");
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
        dispense.MutableNode["status"]?.GetValue<string>().Should().Be("in-progress");
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
        dispense.MutableNode["status"]?.GetValue<string>().Should().Be(status);
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
        dispense.MutableNode.TryGetPropertyValue("medicationCodeableConcept", out _).Should().BeFalse();
        var medicationRef = dispense.MutableNode["medicationReference"]?.AsObject();
        medicationRef?["reference"]?.GetValue<string>().Should().Be($"Medication/{medicationId}");
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
        dispense.MutableNode.TryGetPropertyValue("medicationReference", out _).Should().BeFalse();
        var medication = dispense.MutableNode["medicationCodeableConcept"]?.AsObject();
        var coding = medication?["coding"]?.AsArray()?[0]?.AsObject();
        coding?["code"]?.GetValue<string>().Should().Be("108505002");
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
        coding?["code"]?.GetValue<string>().Should().Be("197361");
        coding?["system"]?.GetValue<string>().Should().Be("http://www.nlm.nih.gov/research/umls/rxnorm");
        coding?["display"]?.GetValue<string>().Should().Be("Lisinopril");
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
        subject?["reference"]?.GetValue<string>().Should().Be($"Patient/{patientId}");
    }

    [Fact]
    public void GivenMedicationDispenseBuilder_WhenBuildingWithoutSubject_ThenDoesNotIncludeSubject()
    {
        // Arrange & Act
        var dispense = MedicationDispenseBuilder.Create(_schemaProvider)
            .WithMedicationCodeableConcept("108505002", "http://snomed.info/sct")
            .Build();

        // Assert
        dispense.MutableNode.TryGetPropertyValue("subject", out _).Should().BeFalse();
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
        prescriptions.Should().HaveCount(1);

        var prescription = prescriptions?[0]?.AsObject();
        prescription?["reference"]?.GetValue<string>().Should().Be($"MedicationRequest/{prescriptionId}");
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
        prescriptions.Should().HaveCount(2);

        var ref1 = prescriptions?[0]?.AsObject()?["reference"]?.GetValue<string>();
        var ref2 = prescriptions?[1]?.AsObject()?["reference"]?.GetValue<string>();

        ref1.Should().Be($"MedicationRequest/{prescription1}");
        ref2.Should().Be($"MedicationRequest/{prescription2}");
    }

    [Fact]
    public void GivenMedicationDispenseBuilder_WhenBuildingWithoutPrescription_ThenDoesNotIncludePrescription()
    {
        // Arrange & Act
        var dispense = MedicationDispenseBuilder.Create(_schemaProvider)
            .WithMedicationCodeableConcept("108505002", "http://snomed.info/sct")
            .Build();

        // Assert
        dispense.MutableNode.TryGetPropertyValue("authorizingPrescription", out _).Should().BeFalse();
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
        performers.Should().HaveCount(1);

        var performer = performers?[0]?.AsObject();
        var actor = performer?["actor"]?.AsObject();
        actor?["reference"]?.GetValue<string>().Should().Be($"Practitioner/{practitionerId}");
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
        performers.Should().HaveCount(2);

        var actor1 = performers?[0]?.AsObject()?["actor"]?.AsObject()?["reference"]?.GetValue<string>();
        var actor2 = performers?[1]?.AsObject()?["actor"]?.AsObject()?["reference"]?.GetValue<string>();

        actor1.Should().Be($"Practitioner/{practitioner1}");
        actor2.Should().Be($"Practitioner/{practitioner2}");
    }

    [Fact]
    public void GivenMedicationDispenseBuilder_WhenBuildingWithoutPerformer_ThenDoesNotIncludePerformer()
    {
        // Arrange & Act
        var dispense = MedicationDispenseBuilder.Create(_schemaProvider)
            .WithMedicationCodeableConcept("108505002", "http://snomed.info/sct")
            .Build();

        // Assert
        dispense.MutableNode.TryGetPropertyValue("performer", out _).Should().BeFalse();
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
        dispense.MutableNode["whenHandedOver"]?.GetValue<string>().Should().Be(timestamp);
    }

    [Fact]
    public void GivenMedicationDispenseBuilder_WhenBuildingWithoutWhenHandedOver_ThenDoesNotIncludeTiming()
    {
        // Arrange & Act
        var dispense = MedicationDispenseBuilder.Create(_schemaProvider)
            .WithMedicationCodeableConcept("108505002", "http://snomed.info/sct")
            .Build();

        // Assert
        dispense.MutableNode.TryGetPropertyValue("whenHandedOver", out _).Should().BeFalse();
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
        dispense.Id.Should().Be("dispense-complete");
        dispense.MutableNode["status"]?.GetValue<string>().Should().Be("completed");

        var subject = dispense.MutableNode["subject"]?.AsObject();
        subject?["reference"]?.GetValue<string>().Should().Be($"Patient/{patientId}");

        var medication = dispense.MutableNode["medicationCodeableConcept"]?.AsObject();
        var coding = medication?["coding"]?.AsArray()?[0]?.AsObject();
        coding?["code"]?.GetValue<string>().Should().Be("197361");

        var prescriptions = dispense.MutableNode["authorizingPrescription"]?.AsArray();
        prescriptions?[0]?.AsObject()?["reference"]?.GetValue<string>().Should().Be($"MedicationRequest/{prescriptionId}");

        var performers = dispense.MutableNode["performer"]?.AsArray();
        var actor = performers?[0]?.AsObject()?["actor"]?.AsObject();
        actor?["reference"]?.GetValue<string>().Should().Be($"Practitioner/{practitionerId}");

        dispense.MutableNode["whenHandedOver"]?.GetValue<string>().Should().Be(timestamp);

        var tags = dispense.MutableNode["meta"]?["tag"]?.AsArray();
        tags?[0]?["code"]?.GetValue<string>().Should().Be(tag);
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
        dispense1.Id.Should().NotBe(dispense2.Id);
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
        dispense.Should().NotBeNull();
        dispense.ResourceType.Should().Be("MedicationDispense");
        dispense.MutableNode["status"]?.GetValue<string>().Should().Be("in-progress");

        var subject = dispense.MutableNode["subject"]?.AsObject();
        subject?["reference"]?.GetValue<string>().Should().Be($"Patient/{patientId}");

        var medication = dispense.MutableNode["medicationCodeableConcept"]?.AsObject();
        var coding = medication?["coding"]?.AsArray()?[0]?.AsObject();
        coding?["code"]?.GetValue<string>().Should().Be("108505002");
        coding?["system"]?.GetValue<string>().Should().Be("http://snomed.info/sct");

        var prescriptions = dispense.MutableNode["authorizingPrescription"]?.AsArray();
        prescriptions?[0]?.AsObject()?["reference"]?.GetValue<string>()
            .Should().Be($"MedicationRequest/{medicationRequestId}");

        var tags = dispense.MutableNode["meta"]?["tag"]?.AsArray();
        tags?[0]?["code"]?.GetValue<string>().Should().Be(tag);
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
        dispense.MutableNode["meta"].Should().NotBeNull();
        var meta = dispense.MutableNode["meta"]?.AsObject();
        meta?["versionId"]?.GetValue<string>().Should().Be("1");
        meta?["lastUpdated"]?.GetValue<string>().Should().NotBeNullOrEmpty();
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
        dispense.Should().NotBeNull();
        dispense.Id.Should().NotBeNullOrEmpty();
        dispense.ResourceType.Should().Be("MedicationDispense");
        dispense.MutableNode["status"]?.GetValue<string>().Should().Be("completed");
    }

    #endregion
}
