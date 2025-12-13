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
/// Unit tests for MedicationRequestBuilder.
/// Tests medication request creation with various configurations including status, intent, and medication types.
/// </summary>
public class MedicationRequestBuilderTests
{
    private readonly IFhirSchemaProvider _schemaProvider = new R4CoreSchemaProvider();

    #region Basic Building Tests

    [Fact]
    public void GivenBuilder_WhenBuildingWithMinimalFields_ThenCreatesMedicationRequest()
    {
        // Arrange
        var patientId = "patient-123";

        // Act
        var request = MedicationRequestBuilder.Create(_schemaProvider)
            .WithSubject(patientId)
            .WithMedicationCodeableConcept("aspirin", "http://example.org")
            .Build();

        // Assert
        request.Should().NotBeNull();
        request.ResourceType.Should().Be("MedicationRequest");
        request.Id.Should().NotBeNullOrEmpty();
        request.MutableNode["status"]?.GetValue<string>().Should().Be("active");
        request.MutableNode["intent"]?.GetValue<string>().Should().Be("order");
        request.MutableNode["subject"]?["reference"]?.GetValue<string>().Should().Be($"Patient/{patientId}");
    }

    [Fact]
    public void GivenBuilder_WhenBuildingWithoutSubject_ThenThrowsInvalidOperationException()
    {
        // Arrange & Act
        var act = () => MedicationRequestBuilder.Create(_schemaProvider)
            .WithMedicationCodeableConcept("aspirin", "http://example.org")
            .Build();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*subject is required*");
    }

    [Fact]
    public void GivenBuilder_WhenBuildingWithoutMedication_ThenThrowsInvalidOperationException()
    {
        // Arrange & Act
        var act = () => MedicationRequestBuilder.Create(_schemaProvider)
            .WithSubject("patient-123")
            .Build();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Medication is required*");
    }

    [Fact]
    public void GivenBuilder_WhenBuildingWithId_ThenUsesProvidedId()
    {
        // Arrange
        var expectedId = "medreq-456";
        var patientId = "patient-123";

        // Act
        var request = MedicationRequestBuilder.Create(_schemaProvider)
            .WithId(expectedId)
            .WithSubject(patientId)
            .WithMedicationCodeableConcept("aspirin", "http://example.org")
            .Build();

        // Assert
        request.Id.Should().Be(expectedId);
    }

    [Fact]
    public void GivenBuilder_WhenBuildingWithTag_ThenIncludesTagInMeta()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();
        var patientId = "patient-123";

        // Act
        var request = MedicationRequestBuilder.Create(_schemaProvider)
            .WithTag(tag)
            .WithSubject(patientId)
            .WithMedicationCodeableConcept("aspirin", "http://example.org")
            .Build();

        // Assert
        request.MutableNode["meta"]?["tag"].Should().NotBeNull();
        var tags = request.MutableNode["meta"]?["tag"]?.AsArray();
        tags.Should().HaveCount(1);

        var metaTag = tags?[0]?.AsObject();
        metaTag?["code"]?.GetValue<string>().Should().Be(tag);
        metaTag?["system"]?.GetValue<string>().Should().Be("http://ignixa.dev/test-isolation");
    }

    #endregion

    #region Status and Intent Tests

    [Fact]
    public void GivenBuilder_WhenSettingStatus_ThenUsesProvidedStatus()
    {
        // Arrange
        var patientId = "patient-123";

        // Act
        var request = MedicationRequestBuilder.Create(_schemaProvider)
            .WithStatus("completed")
            .WithSubject(patientId)
            .WithMedicationCodeableConcept("aspirin", "http://example.org")
            .Build();

        // Assert
        request.MutableNode["status"]?.GetValue<string>().Should().Be("completed");
    }

    [Fact]
    public void GivenBuilder_WhenSettingStatusToCancelled_ThenUsesProvidedStatus()
    {
        // Arrange
        var patientId = "patient-123";

        // Act
        var request = MedicationRequestBuilder.Create(_schemaProvider)
            .WithStatus("cancelled")
            .WithSubject(patientId)
            .WithMedicationCodeableConcept("aspirin", "http://example.org")
            .Build();

        // Assert
        request.MutableNode["status"]?.GetValue<string>().Should().Be("cancelled");
    }

    [Fact]
    public void GivenBuilder_WhenSettingIntent_ThenUsesProvidedIntent()
    {
        // Arrange
        var patientId = "patient-123";

        // Act
        var request = MedicationRequestBuilder.Create(_schemaProvider)
            .WithIntent("plan")
            .WithSubject(patientId)
            .WithMedicationCodeableConcept("aspirin", "http://example.org")
            .Build();

        // Assert
        request.MutableNode["intent"]?.GetValue<string>().Should().Be("plan");
    }

    [Fact]
    public void GivenBuilder_WhenSettingIntentToProposal_ThenUsesProvidedIntent()
    {
        // Arrange
        var patientId = "patient-123";

        // Act
        var request = MedicationRequestBuilder.Create(_schemaProvider)
            .WithIntent("proposal")
            .WithSubject(patientId)
            .WithMedicationCodeableConcept("aspirin", "http://example.org")
            .Build();

        // Assert
        request.MutableNode["intent"]?.GetValue<string>().Should().Be("proposal");
    }

    #endregion

    #region Medication CodeableConcept Tests

    [Fact]
    public void GivenBuilder_WhenUsingMedicationCodeableConcept_ThenIncludesMedicationCodeableConcept()
    {
        // Arrange
        var patientId = "patient-123";

        // Act
        var request = MedicationRequestBuilder.Create(_schemaProvider)
            .WithSubject(patientId)
            .WithMedicationCodeableConcept("16590-619-30", "http://snomed.info/sct", "Amoxicillin 500mg")
            .Build();

        // Assert
        request.MutableNode["medicationCodeableConcept"].Should().NotBeNull();
        request.MutableNode["medicationReference"].Should().BeNull();

        var medicationCC = request.MutableNode["medicationCodeableConcept"]?.AsObject();
        var coding = medicationCC?["coding"]?.AsArray()?[0]?.AsObject();
        coding?["code"]?.GetValue<string>().Should().Be("16590-619-30");
        coding?["system"]?.GetValue<string>().Should().Be("http://snomed.info/sct");
        coding?["display"]?.GetValue<string>().Should().Be("Amoxicillin 500mg");
    }

    [Fact]
    public void GivenBuilder_WhenUsingMedicationCodeableConceptWithoutDisplay_ThenIncludesCodeAndSystem()
    {
        // Arrange
        var patientId = "patient-123";

        // Act
        var request = MedicationRequestBuilder.Create(_schemaProvider)
            .WithSubject(patientId)
            .WithMedicationCodeableConcept("aspirin", "http://example.org")
            .Build();

        // Assert
        var medicationCC = request.MutableNode["medicationCodeableConcept"]?.AsObject();
        var coding = medicationCC?["coding"]?.AsArray()?[0]?.AsObject();
        coding?["code"]?.GetValue<string>().Should().Be("aspirin");
        coding?["system"]?.GetValue<string>().Should().Be("http://example.org");
        coding?["display"].Should().BeNull();
    }

    #endregion

    #region Medication Reference Tests

    [Fact]
    public void GivenBuilder_WhenUsingMedicationReference_ThenIncludesMedicationReference()
    {
        // Arrange
        var patientId = "patient-123";
        var medicationId = "medication-789";

        // Act
        var request = MedicationRequestBuilder.Create(_schemaProvider)
            .WithSubject(patientId)
            .WithMedicationReference(medicationId)
            .Build();

        // Assert
        request.MutableNode["medicationReference"].Should().NotBeNull();
        request.MutableNode["medicationCodeableConcept"].Should().BeNull();

        var medicationRef = request.MutableNode["medicationReference"]?.AsObject();
        medicationRef?["reference"]?.GetValue<string>().Should().Be($"Medication/{medicationId}");
    }

    [Fact]
    public void GivenBuilder_WhenSwitchingFromCodeableConceptToReference_ThenUsesMedicationReference()
    {
        // Arrange
        var patientId = "patient-123";
        var medicationId = "medication-789";

        // Act
        var request = MedicationRequestBuilder.Create(_schemaProvider)
            .WithSubject(patientId)
            .WithMedicationCodeableConcept("aspirin", "http://example.org")
            .WithMedicationReference(medicationId)  // Override
            .Build();

        // Assert
        request.MutableNode["medicationReference"].Should().NotBeNull();
        request.MutableNode["medicationCodeableConcept"].Should().BeNull();

        var medicationRef = request.MutableNode["medicationReference"]?.AsObject();
        medicationRef?["reference"]?.GetValue<string>().Should().Be($"Medication/{medicationId}");
    }

    [Fact]
    public void GivenBuilder_WhenSwitchingFromReferenceToCodeableConcept_ThenUsesMedicationCodeableConcept()
    {
        // Arrange
        var patientId = "patient-123";

        // Act
        var request = MedicationRequestBuilder.Create(_schemaProvider)
            .WithSubject(patientId)
            .WithMedicationReference("medication-789")
            .WithMedicationCodeableConcept("aspirin", "http://example.org")  // Override
            .Build();

        // Assert
        request.MutableNode["medicationCodeableConcept"].Should().NotBeNull();
        request.MutableNode["medicationReference"].Should().BeNull();
    }

    #endregion

    #region Requester Tests

    [Fact]
    public void GivenBuilder_WhenSettingRequester_ThenIncludesRequesterReference()
    {
        // Arrange
        var patientId = "patient-123";
        var practitionerId = "practitioner-456";

        // Act
        var request = MedicationRequestBuilder.Create(_schemaProvider)
            .WithSubject(patientId)
            .WithMedicationCodeableConcept("aspirin", "http://example.org")
            .WithRequester(practitionerId)
            .Build();

        // Assert
        request.MutableNode["requester"].Should().NotBeNull();
        var requester = request.MutableNode["requester"]?.AsObject();
        requester?["reference"]?.GetValue<string>().Should().Be($"Practitioner/{practitionerId}");
    }

    [Fact]
    public void GivenBuilder_WhenNotSettingRequester_ThenOmitsRequesterField()
    {
        // Arrange
        var patientId = "patient-123";

        // Act
        var request = MedicationRequestBuilder.Create(_schemaProvider)
            .WithSubject(patientId)
            .WithMedicationCodeableConcept("aspirin", "http://example.org")
            .Build();

        // Assert
        request.MutableNode["requester"].Should().BeNull();
    }

    #endregion

    #region AuthoredOn Tests

    [Fact]
    public void GivenBuilder_WhenSettingAuthoredOn_ThenIncludesAuthoredOnTimestamp()
    {
        // Arrange
        var patientId = "patient-123";
        var authoredOn = "2023-01-15T10:30:00Z";

        // Act
        var request = MedicationRequestBuilder.Create(_schemaProvider)
            .WithSubject(patientId)
            .WithMedicationCodeableConcept("aspirin", "http://example.org")
            .WithAuthoredOn(authoredOn)
            .Build();

        // Assert
        request.MutableNode["authoredOn"]?.GetValue<string>().Should().Be(authoredOn);
    }

    [Fact]
    public void GivenBuilder_WhenSettingAuthoredOnWithDateOnly_ThenIncludesDate()
    {
        // Arrange
        var patientId = "patient-123";
        var authoredOn = "2023-01-15";

        // Act
        var request = MedicationRequestBuilder.Create(_schemaProvider)
            .WithSubject(patientId)
            .WithMedicationCodeableConcept("aspirin", "http://example.org")
            .WithAuthoredOn(authoredOn)
            .Build();

        // Assert
        request.MutableNode["authoredOn"]?.GetValue<string>().Should().Be(authoredOn);
    }

    [Fact]
    public void GivenBuilder_WhenNotSettingAuthoredOn_ThenOmitsAuthoredOnField()
    {
        // Arrange
        var patientId = "patient-123";

        // Act
        var request = MedicationRequestBuilder.Create(_schemaProvider)
            .WithSubject(patientId)
            .WithMedicationCodeableConcept("aspirin", "http://example.org")
            .Build();

        // Assert
        request.MutableNode["authoredOn"].Should().BeNull();
    }

    #endregion

    #region Complete Request Tests

    [Fact]
    public void GivenBuilder_WhenBuildingCompleteRequest_ThenIncludesAllFields()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();
        var patientId = "patient-123";
        var practitionerId = "practitioner-456";
        var authoredOn = "2023-01-15T10:30:00Z";

        // Act
        var request = MedicationRequestBuilder.Create(_schemaProvider)
            .WithId("medreq-789")
            .WithTag(tag)
            .WithStatus("completed")
            .WithIntent("order")
            .WithSubject(patientId)
            .WithMedicationCodeableConcept("16590-619-30", "http://snomed.info/sct", "Amoxicillin 500mg")
            .WithRequester(practitionerId)
            .WithAuthoredOn(authoredOn)
            .Build();

        // Assert
        request.Should().NotBeNull();
        request.ResourceType.Should().Be("MedicationRequest");
        request.Id.Should().Be("medreq-789");
        request.MutableNode["status"]?.GetValue<string>().Should().Be("completed");
        request.MutableNode["intent"]?.GetValue<string>().Should().Be("order");
        request.MutableNode["subject"]?["reference"]?.GetValue<string>().Should().Be($"Patient/{patientId}");
        request.MutableNode["requester"]?["reference"]?.GetValue<string>().Should().Be($"Practitioner/{practitionerId}");
        request.MutableNode["authoredOn"]?.GetValue<string>().Should().Be(authoredOn);

        var medicationCC = request.MutableNode["medicationCodeableConcept"]?.AsObject();
        var coding = medicationCC?["coding"]?.AsArray()?[0]?.AsObject();
        coding?["code"]?.GetValue<string>().Should().Be("16590-619-30");
        coding?["system"]?.GetValue<string>().Should().Be("http://snomed.info/sct");
        coding?["display"]?.GetValue<string>().Should().Be("Amoxicillin 500mg");

        var tags = request.MutableNode["meta"]?["tag"]?.AsArray();
        tags.Should().HaveCount(1);
        tags?[0]?["code"]?.GetValue<string>().Should().Be(tag);
    }

    [Fact]
    public void GivenBuilder_WhenBuildingCompleteRequestWithReference_ThenIncludesAllFields()
    {
        // Arrange
        var patientId = "patient-123";
        var medicationId = "medication-999";
        var practitionerId = "practitioner-456";

        // Act
        var request = MedicationRequestBuilder.Create(_schemaProvider)
            .WithStatus("active")
            .WithIntent("plan")
            .WithSubject(patientId)
            .WithMedicationReference(medicationId)
            .WithRequester(practitionerId)
            .WithAuthoredOn("2023-01-15")
            .Build();

        // Assert
        request.Should().NotBeNull();
        request.MutableNode["medicationReference"]?["reference"]?.GetValue<string>().Should().Be($"Medication/{medicationId}");
        request.MutableNode["medicationCodeableConcept"].Should().BeNull();
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void GivenBuilder_WhenBuildingMultipleRequests_ThenGeneratesDifferentIds()
    {
        // Arrange
        var patientId = "patient-123";

        // Act
        var request1 = MedicationRequestBuilder.Create(_schemaProvider)
            .WithSubject(patientId)
            .WithMedicationCodeableConcept("aspirin", "http://example.org")
            .Build();

        var request2 = MedicationRequestBuilder.Create(_schemaProvider)
            .WithSubject(patientId)
            .WithMedicationCodeableConcept("aspirin", "http://example.org")
            .Build();

        // Assert
        request1.Id.Should().NotBe(request2.Id);
    }

    [Fact]
    public void GivenBuilder_WhenBuildingWithProfile_ThenIncludesProfileInMeta()
    {
        // Arrange
        var patientId = "patient-123";
        var profileUrl = "http://example.org/fhir/StructureDefinition/custom-medication-request";

        // Act
        var request = MedicationRequestBuilder.Create(_schemaProvider)
            .WithProfile(profileUrl)
            .WithSubject(patientId)
            .WithMedicationCodeableConcept("aspirin", "http://example.org")
            .Build();

        // Assert
        var profiles = request.MutableNode["meta"]?["profile"]?.AsArray();
        profiles.Should().HaveCount(1);
        profiles?[0]?.GetValue<string>().Should().Be(profileUrl);
    }

    #endregion

    #region Fluent API Tests

    [Fact]
    public void GivenBuilder_WhenChainingAllMethods_ThenReturnsBuilder()
    {
        // Arrange & Act
        var builder = MedicationRequestBuilder.Create(_schemaProvider)
            .WithId("test")
            .WithTag("tag")
            .WithProfile("http://example.org")
            .WithStatus("active")
            .WithIntent("order")
            .WithSubject("patient-123")
            .WithMedicationCodeableConcept("code", "system")
            .WithRequester("practitioner-123")
            .WithAuthoredOn("2023-01-15");

        // Assert
        builder.Should().NotBeNull();
        builder.Should().BeOfType<MedicationRequestBuilder>();
    }

    #endregion
}
