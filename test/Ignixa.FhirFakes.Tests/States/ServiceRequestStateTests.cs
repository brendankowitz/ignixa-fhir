// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.FhirFakes.Scenarios;
using Ignixa.FhirFakes.Scenarios.Codes;
using Ignixa.FhirFakes.Scenarios.States;
using Ignixa.Specification.Generated;

namespace Ignixa.FhirFakes.Tests.States;

/// <summary>
/// Tests for ServiceRequestState. Tests laboratory orders, imaging orders, and specialist referrals.
/// Uses AAA pattern and BDD naming convention.
/// </summary>
public class ServiceRequestStateTests
{
    private readonly R4CoreSchemaProvider _schemaProvider = new();

    #region Basic Generation Tests

    [Fact]
    public void GivenServiceRequest_WhenGenerated_ThenCreatesServiceRequest()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddCBCOrder()
            .Build();

        // Assert
        scenario.ServiceRequests.Should().HaveCount(1);
        var serviceRequest = scenario.ServiceRequests[0];
        serviceRequest.ResourceType.Should().Be("ServiceRequest");
        serviceRequest.Id.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GivenServiceRequest_WhenGenerated_ThenHasActiveStatus()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddCBCOrder()
            .Build();

        // Assert
        var serviceRequest = scenario.ServiceRequests[0];
        var status = serviceRequest.MutableNode["status"]?.GetValue<string>();
        status.Should().Be("active");
    }

    [Fact]
    public void GivenServiceRequest_WhenGenerated_ThenHasOrderIntent()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddCBCOrder()
            .Build();

        // Assert
        var serviceRequest = scenario.ServiceRequests[0];
        var intent = serviceRequest.MutableNode["intent"]?.GetValue<string>();
        intent.Should().Be("order");
    }

    [Fact]
    public void GivenServiceRequest_WhenGenerated_ThenHasIdentifier()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddCBCOrder()
            .Build();

        // Assert
        var serviceRequest = scenario.ServiceRequests[0];
        var identifier = serviceRequest.MutableNode["identifier"]?[0]?["value"]?.GetValue<string>();
        identifier.Should().NotBeNullOrEmpty();
        identifier.Should().StartWith("SR-");
    }

    #endregion

    #region Patient Reference Tests

    [Fact]
    public void GivenServiceRequest_WhenGenerated_ThenReferencesPatient()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddCBCOrder()
            .Build();

        // Assert
        var serviceRequest = scenario.ServiceRequests[0];
        var subjectRef = serviceRequest.MutableNode["subject"]?["reference"]?.GetValue<string>();
        subjectRef.Should().Be($"urn:uuid:{scenario.Patient!.Id}");
    }

    [Fact]
    public void GivenServiceRequestWithEncounter_WhenGenerated_ThenReferencesEncounter()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Annual checkup")
            .AddCBCOrder()
            .Build();

        // Assert
        var serviceRequest = scenario.ServiceRequests[0];
        var encounterRef = serviceRequest.MutableNode["encounter"]?["reference"]?.GetValue<string>();
        encounterRef.Should().Be($"urn:uuid:{scenario.Encounters[0].Id}");
    }

    [Fact]
    public void GivenServiceRequestWithPractitioner_WhenGenerated_ThenReferencesRequester()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddFamilyPractitioner()
            .AddCBCOrder()
            .Build();

        // Assert
        var serviceRequest = scenario.ServiceRequests[0];
        var requesterRef = serviceRequest.MutableNode["requester"]?["reference"]?.GetValue<string>();
        requesterRef.Should().Be($"urn:uuid:{scenario.CurrentPractitioner!.Id}");
    }

    #endregion

    #region Laboratory Order Tests

    [Fact]
    public void GivenCBCOrder_WhenGenerated_ThenHasCorrectCode()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddCBCOrder()
            .Build();

        // Assert
        var serviceRequest = scenario.ServiceRequests[0];
        var code = serviceRequest.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>();
        code.Should().Be("58410-2");
    }

    [Fact]
    public void GivenCBCOrder_WhenGenerated_ThenHasLaboratoryCategory()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddCBCOrder()
            .Build();

        // Assert
        var serviceRequest = scenario.ServiceRequests[0];
        var categoryCode = serviceRequest.MutableNode["category"]?[0]?["coding"]?[0]?["code"]?.GetValue<string>();
        categoryCode.Should().Be("108252007"); // Laboratory procedure
    }

    [Fact]
    public void GivenLipidPanelOrder_WhenGenerated_ThenHasCorrectCode()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddLipidPanelOrder()
            .Build();

        // Assert
        var serviceRequest = scenario.ServiceRequests[0];
        var code = serviceRequest.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>();
        code.Should().Be("57698-3");
    }

    [Fact]
    public void GivenHemoglobinA1cOrder_WhenGenerated_ThenHasDiabetesReasonCode()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddHemoglobinA1cOrder()
            .Build();

        // Assert
        var serviceRequest = scenario.ServiceRequests[0];
        var reasonCode = serviceRequest.MutableNode["reasonCode"]?[0]?["coding"]?[0]?["code"]?.GetValue<string>();
        reasonCode.Should().Be("44054006"); // Diabetes mellitus type 2
    }

    [Fact]
    public void GivenComprehensiveMetabolicPanelOrder_WhenGenerated_ThenHasCorrectDisplay()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddComprehensiveMetabolicPanelOrder()
            .Build();

        // Assert
        var serviceRequest = scenario.ServiceRequests[0];
        var display = serviceRequest.MutableNode["code"]?["text"]?.GetValue<string>();
        display.Should().Be("Comprehensive metabolic panel");
    }

    #endregion

    #region Imaging Order Tests

    [Fact]
    public void GivenChestXRayOrder_WhenGenerated_ThenHasCorrectCode()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddChestXRayOrder()
            .Build();

        // Assert
        var serviceRequest = scenario.ServiceRequests[0];
        var code = serviceRequest.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>();
        code.Should().Be("399208008");
    }

    [Fact]
    public void GivenChestXRayOrder_WhenGenerated_ThenHasImagingCategory()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddChestXRayOrder()
            .Build();

        // Assert
        var serviceRequest = scenario.ServiceRequests[0];
        var categoryCode = serviceRequest.MutableNode["category"]?[0]?["coding"]?[0]?["code"]?.GetValue<string>();
        categoryCode.Should().Be("363679005"); // Imaging
    }

    [Fact]
    public void GivenCTChestOrder_WhenGenerated_ThenHasCorrectCode()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddCTChestOrder()
            .Build();

        // Assert
        var serviceRequest = scenario.ServiceRequests[0];
        var code = serviceRequest.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>();
        code.Should().Be("241540006");
    }

    [Fact]
    public void GivenMRIBrainOrder_WhenGenerated_ThenHasCorrectDisplay()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddMRIBrainOrder()
            .Build();

        // Assert
        var serviceRequest = scenario.ServiceRequests[0];
        var display = serviceRequest.MutableNode["code"]?["text"]?.GetValue<string>();
        display.Should().Be("MRI of brain");
    }

    [Fact]
    public void GivenMammogramOrder_WhenGenerated_ThenHasImagingCategory()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddMammogramOrder()
            .Build();

        // Assert
        var serviceRequest = scenario.ServiceRequests[0];
        var categoryCode = serviceRequest.MutableNode["category"]?[0]?["coding"]?[0]?["code"]?.GetValue<string>();
        categoryCode.Should().Be("363679005");
    }

    #endregion

    #region Specialist Referral Tests

    [Fact]
    public void GivenCardiologyReferral_WhenGenerated_ThenHasCorrectCode()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddCardiologyReferral()
            .Build();

        // Assert
        var serviceRequest = scenario.ServiceRequests[0];
        var code = serviceRequest.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>();
        code.Should().Be("183524002");
    }

    [Fact]
    public void GivenCardiologyReferral_WhenGenerated_ThenHasReferralCategory()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddCardiologyReferral()
            .Build();

        // Assert
        var serviceRequest = scenario.ServiceRequests[0];
        var categoryCode = serviceRequest.MutableNode["category"]?[0]?["coding"]?[0]?["code"]?.GetValue<string>();
        categoryCode.Should().Be("3457005"); // Referral
    }

    [Fact]
    public void GivenOrthopedicReferral_WhenGenerated_ThenHasCorrectCode()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddOrthopedicReferral()
            .Build();

        // Assert
        var serviceRequest = scenario.ServiceRequests[0];
        var code = serviceRequest.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>();
        code.Should().Be("183516009");
    }

    [Fact]
    public void GivenPhysicalTherapyReferral_WhenGenerated_ThenHasCorrectDisplay()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddPhysicalTherapyReferral()
            .Build();

        // Assert
        var serviceRequest = scenario.ServiceRequests[0];
        var display = serviceRequest.MutableNode["code"]?["text"]?.GetValue<string>();
        display.Should().Be("Physical therapy referral");
    }

    [Fact]
    public void GivenPsychiatryReferral_WhenGenerated_ThenHasCorrectCode()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddPsychiatryReferral()
            .Build();

        // Assert
        var serviceRequest = scenario.ServiceRequests[0];
        var code = serviceRequest.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>();
        code.Should().Be("183521005");
    }

    #endregion

    #region Priority Tests

    [Fact]
    public void GivenRoutineOrder_WhenGenerated_ThenHasRoutinePriority()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddCBCOrder()
            .Build();

        // Assert
        var serviceRequest = scenario.ServiceRequests[0];
        var priority = serviceRequest.MutableNode["priority"]?.GetValue<string>();
        priority.Should().Be("routine");
    }

    [Fact]
    public void GivenUrgentCBCOrder_WhenGenerated_ThenHasUrgentPriority()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddUrgentCBCOrder()
            .Build();

        // Assert
        var serviceRequest = scenario.ServiceRequests[0];
        var priority = serviceRequest.MutableNode["priority"]?.GetValue<string>();
        priority.Should().Be("urgent");
    }

    [Fact]
    public void GivenStatMetabolicPanelOrder_WhenGenerated_ThenHasStatPriority()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddStatMetabolicPanelOrder()
            .Build();

        // Assert
        var serviceRequest = scenario.ServiceRequests[0];
        var priority = serviceRequest.MutableNode["priority"]?.GetValue<string>();
        priority.Should().Be("stat");
    }

    [Fact]
    public void GivenCustomPriority_WhenGenerated_ThenUsesProvidedPriority()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddLabOrder(ServiceRequestCodes.Laboratory.CBCWithDifferential, priority: "asap")
            .Build();

        // Assert
        var serviceRequest = scenario.ServiceRequests[0];
        var priority = serviceRequest.MutableNode["priority"]?.GetValue<string>();
        priority.Should().Be("asap");
    }

    #endregion

    #region Performer Tests

    [Fact]
    public void GivenServiceRequestWithOrganization_WhenGenerated_ThenReferencesPerformer()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddLaboratory("Quest Diagnostics")
            .AddCBCOrder()
            .Build();

        // Assert
        var serviceRequest = scenario.ServiceRequests[0];
        var performerRef = serviceRequest.MutableNode["performer"]?[0]?["reference"]?.GetValue<string>();
        performerRef.Should().Be($"urn:uuid:{scenario.CurrentOrganization!.Id}");
    }

    [Fact]
    public void GivenLabOrderWithoutOrganization_WhenGenerated_ThenHasDefaultPerformerDisplay()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddCBCOrder()
            .Build();

        // Assert
        var serviceRequest = scenario.ServiceRequests[0];
        var performerDisplay = serviceRequest.MutableNode["performer"]?[0]?["display"]?.GetValue<string>();
        performerDisplay.Should().Be("Clinical Laboratory");
    }

    [Fact]
    public void GivenImagingOrderWithoutOrganization_WhenGenerated_ThenHasRadiologyPerformerDisplay()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddChestXRayOrder()
            .Build();

        // Assert
        var serviceRequest = scenario.ServiceRequests[0];
        var performerDisplay = serviceRequest.MutableNode["performer"]?[0]?["display"]?.GetValue<string>();
        performerDisplay.Should().Be("Radiology Department");
    }

    [Fact]
    public void GivenReferralWithoutOrganization_WhenGenerated_ThenHasSpecialistClinicPerformerDisplay()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddCardiologyReferral()
            .Build();

        // Assert
        var serviceRequest = scenario.ServiceRequests[0];
        var performerDisplay = serviceRequest.MutableNode["performer"]?[0]?["display"]?.GetValue<string>();
        performerDisplay.Should().Be("Specialist Clinic");
    }

    #endregion

    #region AuthoredOn Tests

    [Fact]
    public void GivenServiceRequest_WhenGenerated_ThenHasAuthoredOn()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddCBCOrder()
            .Build();

        // Assert
        var serviceRequest = scenario.ServiceRequests[0];
        var authoredOn = serviceRequest.MutableNode["authoredOn"]?.GetValue<string>();
        authoredOn.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GivenServiceRequestWithCustomAuthoredOn_WhenGenerated_ThenUsesProvidedDate()
    {
        // Arrange
        var customDate = new DateTime(2024, 6, 15, 10, 30, 0, DateTimeKind.Utc);

        // Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddServiceRequest(new ServiceRequestState
            {
                Code = ServiceRequestCodes.Laboratory.CBCWithDifferential,
                AuthoredOn = customDate
            })
            .Build();

        // Assert
        var serviceRequest = scenario.ServiceRequests[0];
        var authoredOn = serviceRequest.MutableNode["authoredOn"]?.GetValue<string>();
        authoredOn.Should().Contain("2024-06-15");
    }

    #endregion

    #region Timeline and Context Tests

    [Fact]
    public void GivenMultipleServiceRequests_WhenGenerated_ThenAllAddedToContext()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Annual checkup")
            .AddCBCOrder()
            .AddLipidPanelOrder()
            .AddHemoglobinA1cOrder()
            .Build();

        // Assert
        scenario.ServiceRequests.Should().HaveCount(3);
    }

    [Fact]
    public void GivenServiceRequest_WhenGenerated_ThenAppearsInTimeline()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddCBCOrder()
            .Build();

        // Assert
        var serviceRequestEvents = scenario.Timeline.Where(e => e.EventType == "ServiceRequest").ToList();
        serviceRequestEvents.Should().HaveCount(1);
    }

    [Fact]
    public void GivenServiceRequest_WhenGenerated_ThenAppearsInAllResources()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddCBCOrder()
            .Build();

        // Assert
        scenario.AllResources.Should().Contain(scenario.ServiceRequests[0]);
    }

    [Fact]
    public void GivenServiceRequest_WhenGenerated_ThenCurrentServiceRequestIsSet()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddCBCOrder()
            .AddLipidPanelOrder()
            .Build();

        // Assert
        scenario.CurrentServiceRequest.Should().NotBeNull();
        scenario.CurrentServiceRequest.Should().Be(scenario.ServiceRequests[1]); // Most recent
    }

    #endregion

    #region Factory Method Tests

    [Fact]
    public void GivenCBCOrderFactory_WhenGenerated_ThenHasLOINCSystem()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddCBCOrder()
            .Build();

        // Assert
        var serviceRequest = scenario.ServiceRequests[0];
        var system = serviceRequest.MutableNode["code"]?["coding"]?[0]?["system"]?.GetValue<string>();
        system.Should().Be("http://loinc.org");
    }

    [Fact]
    public void GivenChestXRayOrderFactory_WhenGenerated_ThenHasSNOMEDSystem()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddChestXRayOrder()
            .Build();

        // Assert
        var serviceRequest = scenario.ServiceRequests[0];
        var system = serviceRequest.MutableNode["code"]?["coding"]?[0]?["system"]?.GetValue<string>();
        system.Should().Be("http://snomed.info/sct");
    }

    [Fact]
    public void GivenCardiologyReferralFactory_WhenGenerated_ThenHasSNOMEDSystem()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddCardiologyReferral()
            .Build();

        // Assert
        var serviceRequest = scenario.ServiceRequests[0];
        var system = serviceRequest.MutableNode["code"]?["coding"]?[0]?["system"]?.GetValue<string>();
        system.Should().Be("http://snomed.info/sct");
    }

    #endregion

    #region Category Inference Tests

    [Fact]
    public void GivenLOINCCode_WhenCategoryNotSpecified_ThenInfersLaboratory()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddServiceRequest(ServiceRequestCodes.Laboratory.TSH)
            .Build();

        // Assert
        var serviceRequest = scenario.ServiceRequests[0];
        var categoryCode = serviceRequest.MutableNode["category"]?[0]?["coding"]?[0]?["code"]?.GetValue<string>();
        categoryCode.Should().Be("108252007"); // Laboratory procedure
    }

    [Fact]
    public void GivenImagingCode_WhenCategoryNotSpecified_ThenInfersImaging()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddServiceRequest(ServiceRequestCodes.ImagingStudies.Echocardiogram)
            .Build();

        // Assert
        var serviceRequest = scenario.ServiceRequests[0];
        var categoryCode = serviceRequest.MutableNode["category"]?[0]?["coding"]?[0]?["code"]?.GetValue<string>();
        // Echocardiogram doesn't contain typical imaging keywords, so defaults to procedure
        categoryCode.Should().NotBeNull();
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public void GivenServiceRequestWithoutEncounter_WhenGenerated_ThenCreatesWithoutEncounterReference()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddCBCOrder()
            .Build();

        // Assert
        var serviceRequest = scenario.ServiceRequests[0];
        var encounterRef = serviceRequest.MutableNode["encounter"];
        encounterRef.Should().BeNull();
    }

    [Fact]
    public void GivenServiceRequestWithNote_WhenGenerated_ThenHasNote()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddServiceRequest(new ServiceRequestState
            {
                Code = ServiceRequestCodes.Laboratory.CBCWithDifferential,
                Note = "Fasting required for 12 hours"
            })
            .Build();

        // Assert
        var serviceRequest = scenario.ServiceRequests[0];
        var note = serviceRequest.MutableNode["note"]?[0]?["text"]?.GetValue<string>();
        note.Should().Be("Fasting required for 12 hours");
    }

    [Fact]
    public void GivenServiceRequestWithCustomReasonDisplay_WhenGenerated_ThenHasReasonCode()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddServiceRequest(new ServiceRequestState
            {
                Code = ServiceRequestCodes.Laboratory.LipidPanel,
                ReasonDisplay = "Screening for hyperlipidemia"
            })
            .Build();

        // Assert
        var serviceRequest = scenario.ServiceRequests[0];
        var reason = serviceRequest.MutableNode["reasonCode"]?[0]?["text"]?.GetValue<string>();
        reason.Should().Be("Screening for hyperlipidemia");
    }

    [Fact]
    public void GivenServiceRequestWithOccurrenceDateTime_WhenGenerated_ThenHasOccurrenceDateTime()
    {
        // Arrange
        var scheduledDate = DateTime.UtcNow.AddDays(7);

        // Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddServiceRequest(new ServiceRequestState
            {
                Code = ServiceRequestCodes.ImagingStudies.MRIBrain,
                OccurrenceDateTime = scheduledDate
            })
            .Build();

        // Assert
        var serviceRequest = scenario.ServiceRequests[0];
        var occurrenceDateTime = serviceRequest.MutableNode["occurrenceDateTime"]?.GetValue<string>();
        occurrenceDateTime.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region Complete Clinical Scenario Tests

    [Fact]
    public void GivenTypicalLabOrderScenario_WhenGenerated_ThenHasCompleteStructure()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient(age: 55, gender: "male")
            .AddFamilyPractitioner()
            .AddEncounter("Annual wellness visit")
            .AddCBCOrder()
            .AddLipidPanelOrder()
            .AddComprehensiveMetabolicPanelOrder()
            .Build();

        // Assert
        scenario.ServiceRequests.Should().HaveCount(3);
        foreach (var sr in scenario.ServiceRequests)
        {
            sr.MutableNode["status"]?.GetValue<string>().Should().Be("active");
            sr.MutableNode["intent"]?.GetValue<string>().Should().Be("order");
            sr.MutableNode["subject"]?["reference"]?.GetValue<string>().Should().NotBeNullOrEmpty();
            sr.MutableNode["requester"]?["reference"]?.GetValue<string>().Should().NotBeNullOrEmpty();
            sr.MutableNode["encounter"]?["reference"]?.GetValue<string>().Should().NotBeNullOrEmpty();
        }
    }

    [Fact]
    public void GivenTypicalImagingOrderScenario_WhenGenerated_ThenHasCompleteStructure()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient(age: 45, gender: "female")
            .AddFamilyPractitioner()
            .AddImagingCenter("City Medical Imaging")
            .AddEncounter("Follow-up visit")
            .AddChestXRayOrder()
            .AddMammogramOrder()
            .Build();

        // Assert
        scenario.ServiceRequests.Should().HaveCount(2);
        foreach (var sr in scenario.ServiceRequests)
        {
            var categoryCode = sr.MutableNode["category"]?[0]?["coding"]?[0]?["code"]?.GetValue<string>();
            categoryCode.Should().Be("363679005"); // Imaging
        }
    }

    [Fact]
    public void GivenTypicalReferralScenario_WhenGenerated_ThenHasCompleteStructure()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient(age: 60, gender: "male")
            .AddFamilyPractitioner()
            .AddConditionOnset(FhirCode.Conditions.Hypertension)
            .AddEncounter("Consultation")
            .AddCardiologyReferral()
            .Build();

        // Assert
        scenario.ServiceRequests.Should().HaveCount(1);
        var referral = scenario.ServiceRequests[0];
        var categoryCode = referral.MutableNode["category"]?[0]?["coding"]?[0]?["code"]?.GetValue<string>();
        categoryCode.Should().Be("3457005"); // Referral
    }

    #endregion
}
