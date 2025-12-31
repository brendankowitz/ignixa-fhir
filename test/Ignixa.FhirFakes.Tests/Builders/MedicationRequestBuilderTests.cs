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
        request.ShouldNotBeNull();
        request.ResourceType.ShouldBe("MedicationRequest");
        request.Id.ShouldNotBeNullOrEmpty();
        request.MutableNode["status"]?.GetValue<string>().ShouldBe("active");
        request.MutableNode["intent"]?.GetValue<string>().ShouldBe("order");
        request.MutableNode["subject"]?["reference"]?.GetValue<string>().ShouldBe($"Patient/{patientId}");
    }

    [Fact]
    public void GivenBuilder_WhenBuildingWithoutSubject_ThenThrowsInvalidOperationException()
    {
        // Arrange & Act
        var act = () => MedicationRequestBuilder.Create(_schemaProvider)
            .WithMedicationCodeableConcept("aspirin", "http://example.org")
            .Build();

        // Assert
        Should.Throw<InvalidOperationException>(act).Message.ShouldContain("subject is required");
    }

    [Fact]
    public void GivenBuilder_WhenBuildingWithoutMedication_ThenThrowsInvalidOperationException()
    {
        // Arrange & Act
        var act = () => MedicationRequestBuilder.Create(_schemaProvider)
            .WithSubject("patient-123")
            .Build();

        // Assert
        Should.Throw<InvalidOperationException>(act).Message.ShouldContain("Medication is required");
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
        request.Id.ShouldBe(expectedId);
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
        request.MutableNode["meta"]?["tag"].ShouldNotBeNull();
        var tags = request.MutableNode["meta"]?["tag"]?.AsArray();
        tags!.Count.ShouldBe(1);

        var metaTag = tags?[0]?.AsObject();
        metaTag?["code"]?.GetValue<string>().ShouldBe(tag);
        metaTag?["system"]?.GetValue<string>().ShouldBe("http://ignixa.dev/test-isolation");
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
        request.MutableNode["status"]?.GetValue<string>().ShouldBe("completed");
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
        request.MutableNode["status"]?.GetValue<string>().ShouldBe("cancelled");
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
        request.MutableNode["intent"]?.GetValue<string>().ShouldBe("plan");
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
        request.MutableNode["intent"]?.GetValue<string>().ShouldBe("proposal");
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
        request.MutableNode["medicationCodeableConcept"].ShouldNotBeNull();
        request.MutableNode["medicationReference"].ShouldBeNull();

        var medicationCC = request.MutableNode["medicationCodeableConcept"]?.AsObject();
        var coding = medicationCC?["coding"]?.AsArray()?[0]?.AsObject();
        coding?["code"]?.GetValue<string>().ShouldBe("16590-619-30");
        coding?["system"]?.GetValue<string>().ShouldBe("http://snomed.info/sct");
        coding?["display"]?.GetValue<string>().ShouldBe("Amoxicillin 500mg");
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
        coding?["code"]?.GetValue<string>().ShouldBe("aspirin");
        coding?["system"]?.GetValue<string>().ShouldBe("http://example.org");
        coding?["display"].ShouldBeNull();
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
        request.MutableNode["medicationReference"].ShouldNotBeNull();
        request.MutableNode["medicationCodeableConcept"].ShouldBeNull();

        var medicationRef = request.MutableNode["medicationReference"]?.AsObject();
        medicationRef?["reference"]?.GetValue<string>().ShouldBe($"Medication/{medicationId}");
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
        request.MutableNode["medicationReference"].ShouldNotBeNull();
        request.MutableNode["medicationCodeableConcept"].ShouldBeNull();

        var medicationRef = request.MutableNode["medicationReference"]?.AsObject();
        medicationRef?["reference"]?.GetValue<string>().ShouldBe($"Medication/{medicationId}");
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
        request.MutableNode["medicationCodeableConcept"].ShouldNotBeNull();
        request.MutableNode["medicationReference"].ShouldBeNull();
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
        request.MutableNode["requester"].ShouldNotBeNull();
        var requester = request.MutableNode["requester"]?.AsObject();
        requester?["reference"]?.GetValue<string>().ShouldBe($"Practitioner/{practitionerId}");
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
        request.MutableNode["requester"].ShouldBeNull();
    }

    [Fact]
    public void GivenBuilder_WhenSettingRequesterWithResourceType_ThenIncludesCorrectRequesterReference()
    {
        // Arrange
        var patientId = "patient-123";
        var organizationId = "org-789";

        // Act
        var request = MedicationRequestBuilder.Create(_schemaProvider)
            .WithSubject(patientId)
            .WithMedicationCodeableConcept("aspirin", "http://example.org")
            .WithRequester("Organization", organizationId)
            .Build();

        // Assert
        request.MutableNode["requester"].ShouldNotBeNull();
        var requester = request.MutableNode["requester"]?.AsObject();
        requester?["reference"]?.GetValue<string>().ShouldBe($"Organization/{organizationId}");
    }

    [Fact]
    public void GivenBuilder_WhenUsingRequesterOrganizationConvenienceMethod_ThenIncludesOrganizationRequester()
    {
        // Arrange
        var patientId = "patient-123";
        var organizationId = "org-456";

        // Act
        var request = MedicationRequestBuilder.Create(_schemaProvider)
            .WithSubject(patientId)
            .WithMedicationCodeableConcept("aspirin", "http://example.org")
            .WithRequesterOrganization(organizationId)
            .Build();

        // Assert
        request.MutableNode["requester"].ShouldNotBeNull();
        var requester = request.MutableNode["requester"]?.AsObject();
        requester?["reference"]?.GetValue<string>().ShouldBe($"Organization/{organizationId}");
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
        request.MutableNode["authoredOn"]?.GetValue<string>().ShouldBe(authoredOn);
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
        request.MutableNode["authoredOn"]?.GetValue<string>().ShouldBe(authoredOn);
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
        request.MutableNode["authoredOn"].ShouldBeNull();
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
        request.ShouldNotBeNull();
        request.ResourceType.ShouldBe("MedicationRequest");
        request.Id.ShouldBe("medreq-789");
        request.MutableNode["status"]?.GetValue<string>().ShouldBe("completed");
        request.MutableNode["intent"]?.GetValue<string>().ShouldBe("order");
        request.MutableNode["subject"]?["reference"]?.GetValue<string>().ShouldBe($"Patient/{patientId}");
        request.MutableNode["requester"]?["reference"]?.GetValue<string>().ShouldBe($"Practitioner/{practitionerId}");
        request.MutableNode["authoredOn"]?.GetValue<string>().ShouldBe(authoredOn);

        var medicationCC = request.MutableNode["medicationCodeableConcept"]?.AsObject();
        var coding = medicationCC?["coding"]?.AsArray()?[0]?.AsObject();
        coding?["code"]?.GetValue<string>().ShouldBe("16590-619-30");
        coding?["system"]?.GetValue<string>().ShouldBe("http://snomed.info/sct");
        coding?["display"]?.GetValue<string>().ShouldBe("Amoxicillin 500mg");

        var tags = request.MutableNode["meta"]?["tag"]?.AsArray();
        tags!.Count.ShouldBe(1);
        tags?[0]?["code"]?.GetValue<string>().ShouldBe(tag);
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
        request.ShouldNotBeNull();
        request.MutableNode["medicationReference"]?["reference"]?.GetValue<string>().ShouldBe($"Medication/{medicationId}");
        request.MutableNode["medicationCodeableConcept"].ShouldBeNull();
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
        request1.Id.ShouldNotBe(request2.Id);
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
        profiles!.Count.ShouldBe(1);
        profiles?[0]?.GetValue<string>().ShouldBe(profileUrl);
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
        builder.ShouldNotBeNull();
        builder.ShouldBeOfType<MedicationRequestBuilder>();
    }

    #endregion
}
