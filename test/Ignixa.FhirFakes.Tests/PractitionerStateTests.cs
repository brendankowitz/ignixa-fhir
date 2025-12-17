// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.FhirFakes.Scenarios;
using Ignixa.FhirFakes.Scenarios.Codes;
using Ignixa.FhirFakes.Scenarios.States;
using Ignixa.Specification.Generated;

namespace Ignixa.FhirFakes.Tests;

/// <summary>
/// Tests for PractitionerState. Tests healthcare provider generation with NPI and specialty codes.
/// Uses AAA pattern and BDD naming convention.
/// </summary>
public class PractitionerStateTests
{
    private readonly R4CoreSchemaProvider _schemaProvider = new();

    #region Basic Generation Tests

    [Fact]
    public void GivenPractitioner_WhenGenerated_ThenCreatesPractitioner()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddFamilyPractitioner()
            .Build();

        // Assert
        scenario.Practitioners.Count.ShouldBe(1);
        var practitioner = scenario.Practitioners[0];
        practitioner.ResourceType.ShouldBe("Practitioner");
        practitioner.Id.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void GivenPractitioner_WhenGenerated_ThenHasActiveStatus()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddFamilyPractitioner()
            .Build();

        // Assert
        var practitioner = scenario.Practitioners[0];
        var active = practitioner.MutableNode["active"]?.GetValue<bool?>();
        active!.Value.ShouldBeTrue();
    }

    [Fact]
    public void GivenPractitioner_WhenGenerated_ThenHasName()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddFamilyPractitioner()
            .Build();

        // Assert
        var practitioner = scenario.Practitioners[0];
        var familyName = practitioner.MutableNode["name"]?[0]?["family"]?.GetValue<string>();
        familyName.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void GivenPractitioner_WhenGenerated_ThenHasGender()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddFamilyPractitioner()
            .Build();

        // Assert
        var practitioner = scenario.Practitioners[0];
        var gender = practitioner.MutableNode["gender"]?.GetValue<string>();
        gender.ShouldBeOneOf("male", "female");
    }

    #endregion

    #region NPI Generation Tests

    [Fact]
    public void GivenPractitioner_WhenGenerated_ThenHasNpiIdentifier()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddFamilyPractitioner()
            .Build();

        // Assert
        var practitioner = scenario.Practitioners[0];
        var identifierSystem = practitioner.MutableNode["identifier"]?[0]?["system"]?.GetValue<string>();
        identifierSystem.ShouldBe("http://hl7.org/fhir/sid/us-npi");
    }

    [Fact]
    public void GivenPractitioner_WhenGenerated_ThenNpiIsTenDigits()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddFamilyPractitioner()
            .Build();

        // Assert
        var practitioner = scenario.Practitioners[0];
        var npi = practitioner.MutableNode["identifier"]?[0]?["value"]?.GetValue<string>();
        npi!.Length.ShouldBe(10);
        npi.ShouldMatch(@"^\d{10}$");
    }

    [Fact]
    public void GivenPractitioner_WhenGenerated_ThenNpiStartsWithOne()
    {
        // Type 1 NPI (individual practitioner) starts with 1
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddFamilyPractitioner()
            .Build();

        // Assert
        var practitioner = scenario.Practitioners[0];
        var npi = practitioner.MutableNode["identifier"]?[0]?["value"]?.GetValue<string>();
        npi.ShouldStartWith("1");
    }

    [Fact]
    public void GivenGeneratedNpi_WhenValidated_ThenLuhnCheckPasses()
    {
        // Arrange
        var npi = PractitionerState.GenerateNpi();

        // Act
        var isValid = PractitionerState.ValidateNpi(npi);

        // Assert
        isValid.ShouldBeTrue($"Generated NPI {npi} should have valid Luhn check digit");
    }

    [Fact]
    public void GivenMultipleGeneratedNpis_WhenValidated_ThenAllPass()
    {
        // Arrange & Act & Assert
        for (int i = 0; i < 100; i++)
        {
            var npi = PractitionerState.GenerateNpi();
            PractitionerState.ValidateNpi(npi).ShouldBeTrue($"NPI {npi} should be valid");
        }
    }

    [Fact]
    public void GivenOrganizationalNpi_WhenGenerated_ThenStartsWithTwo()
    {
        // Type 2 NPI (organizational) starts with 2
        // Arrange & Act
        var npi = PractitionerState.GenerateNpi(isOrganization: true);

        // Assert
        npi.ShouldStartWith("2");
        PractitionerState.ValidateNpi(npi).ShouldBeTrue();
    }

    [Fact]
    public void GivenInvalidNpi_WhenValidated_ThenFails()
    {
        // Arrange
        var invalidNpi = "1234567890"; // Random digits, likely invalid check digit

        // Act
        var isValid = PractitionerState.ValidateNpi(invalidNpi);

        // Assert
        // Most random NPIs will fail validation, but there's a 10% chance of passing
        // So we test with a known invalid NPI
        var definitelyInvalid = "1234567891"; // Manually verified as invalid
        PractitionerState.ValidateNpi(definitelyInvalid).ShouldBeFalse();
    }

    [Fact]
    public void GivenNpiWithWrongLength_WhenValidated_ThenFails()
    {
        // Arrange
        var shortNpi = "123456789";
        var longNpi = "12345678901";

        // Act & Assert
        PractitionerState.ValidateNpi(shortNpi).ShouldBeFalse();
        PractitionerState.ValidateNpi(longNpi).ShouldBeFalse();
    }

    [Fact]
    public void GivenNpiWithNonDigits_WhenValidated_ThenFails()
    {
        // Arrange
        var invalidNpi = "123456789A";

        // Act & Assert
        PractitionerState.ValidateNpi(invalidNpi).ShouldBeFalse();
    }

    [Fact]
    public void GivenCustomNpi_WhenProvided_ThenUsesCustomNpi()
    {
        // Arrange
        var customNpi = PractitionerState.GenerateNpi(); // Generate a valid NPI

        // Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddPractitioner(new PractitionerState
            {
                Specialty = Specialties.FamilyMedicine,
                NpiNumber = customNpi
            })
            .Build();

        // Assert
        var practitioner = scenario.Practitioners[0];
        var npi = practitioner.MutableNode["identifier"]?[0]?["value"]?.GetValue<string>();
        npi.ShouldBe(customNpi);
    }

    #endregion

    #region Name Component Tests

    [Fact]
    public void GivenPhysician_WhenGenerated_ThenHasDrPrefix()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddFamilyPractitioner()
            .Build();

        // Assert
        var practitioner = scenario.Practitioners[0];
        var prefix = practitioner.MutableNode["name"]?[0]?["prefix"]?[0]?.GetValue<string>();
        prefix.ShouldBe("Dr.");
    }

    [Fact]
    public void GivenNurse_WhenGenerated_ThenHasNoPrefix()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddNurse()
            .Build();

        // Assert
        var practitioner = scenario.Practitioners[0];
        var prefix = practitioner.MutableNode["name"]?[0]?["prefix"];
        prefix.ShouldBeNull();
    }

    [Fact]
    public void GivenPhysician_WhenGenerated_ThenHasMdOrDoSuffix()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddFamilyPractitioner()
            .Build();

        // Assert
        var practitioner = scenario.Practitioners[0];
        var suffix = practitioner.MutableNode["name"]?[0]?["suffix"]?[0]?.GetValue<string>();
        suffix.ShouldBeOneOf("MD", "DO");
    }

    [Fact]
    public void GivenNurse_WhenGenerated_ThenHasRnSuffix()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddNurse()
            .Build();

        // Assert
        var practitioner = scenario.Practitioners[0];
        var suffix = practitioner.MutableNode["name"]?[0]?["suffix"]?[0]?.GetValue<string>();
        suffix.ShouldBe("RN");
    }

    [Fact]
    public void GivenNursePractitioner_WhenGenerated_ThenHasNpSuffix()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddNursePractitioner()
            .Build();

        // Assert
        var practitioner = scenario.Practitioners[0];
        var suffix = practitioner.MutableNode["name"]?[0]?["suffix"]?[0]?.GetValue<string>();
        suffix.ShouldBe("NP");
    }

    [Fact]
    public void GivenCustomName_WhenProvided_ThenUsesCustomName()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddPractitioner(new PractitionerState
            {
                Specialty = Specialties.FamilyMedicine,
                GivenName = "John",
                FamilyName = "Smith"
            })
            .Build();

        // Assert
        var practitioner = scenario.Practitioners[0];
        var givenName = practitioner.MutableNode["name"]?[0]?["given"]?[0]?.GetValue<string>();
        var familyName = practitioner.MutableNode["name"]?[0]?["family"]?.GetValue<string>();
        givenName.ShouldBe("John");
        familyName.ShouldBe("Smith");
    }

    #endregion

    #region Qualification Tests

    [Fact]
    public void GivenPractitioner_WhenGenerated_ThenHasQualification()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddFamilyPractitioner()
            .Build();

        // Assert
        var practitioner = scenario.Practitioners[0];
        var qualifications = practitioner.MutableNode["qualification"];
        qualifications.ShouldNotBeNull();
    }

    [Fact]
    public void GivenFamilyPractitioner_WhenGenerated_ThenHasFamilyMedicineQualification()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddFamilyPractitioner()
            .Build();

        // Assert
        var practitioner = scenario.Practitioners[0];
        var qualCode = practitioner.MutableNode["qualification"]?[0]?["code"]?["coding"]?[0]?["code"]?.GetValue<string>();
        qualCode.ShouldBe("419772000"); // SNOMED code for Family Medicine
    }

    #endregion

    #region Contact Information Tests

    [Fact]
    public void GivenPractitioner_WhenGenerated_ThenHasPhoneContact()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddFamilyPractitioner()
            .Build();

        // Assert
        var practitioner = scenario.Practitioners[0];
        var phoneSystem = practitioner.MutableNode["telecom"]?[0]?["system"]?.GetValue<string>();
        phoneSystem.ShouldBe("phone");
    }

    [Fact]
    public void GivenPractitioner_WhenGenerated_ThenHasEmailContact()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddFamilyPractitioner()
            .Build();

        // Assert
        var practitioner = scenario.Practitioners[0];
        var emailSystem = practitioner.MutableNode["telecom"]?[1]?["system"]?.GetValue<string>();
        emailSystem.ShouldBe("email");
    }

    [Fact]
    public void GivenPractitioner_WhenGenerated_ThenHasAddress()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddFamilyPractitioner()
            .Build();

        // Assert
        var practitioner = scenario.Practitioners[0];
        var addressUse = practitioner.MutableNode["address"]?[0]?["use"]?.GetValue<string>();
        addressUse.ShouldBe("work");
    }

    #endregion

    #region Factory Method Tests

    [Fact]
    public void GivenFamilyPractitionerFactory_WhenUsed_ThenHasFamilyMedicineSpecialty()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddFamilyPractitioner()
            .Build();

        // Assert
        var practitioner = scenario.Practitioners[0];
        var specialtyCode = practitioner.MutableNode["qualification"]?[0]?["code"]?["coding"]?[0]?["code"]?.GetValue<string>();
        specialtyCode.ShouldBe("419772000");
    }

    [Fact]
    public void GivenPediatricianFactory_WhenUsed_ThenHasPediatricsSpecialty()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddPediatrician()
            .Build();

        // Assert
        var practitioner = scenario.Practitioners[0];
        var specialtyCode = practitioner.MutableNode["qualification"]?[0]?["code"]?["coding"]?[0]?["code"]?.GetValue<string>();
        specialtyCode.ShouldBe("394537008");
    }

    [Fact]
    public void GivenCardiologistFactory_WhenUsed_ThenHasCardiologySpecialty()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddCardiologist()
            .Build();

        // Assert
        var practitioner = scenario.Practitioners[0];
        var specialtyCode = practitioner.MutableNode["qualification"]?[0]?["code"]?["coding"]?[0]?["code"]?.GetValue<string>();
        specialtyCode.ShouldBe("394579002");
    }

    [Fact]
    public void GivenEmergencyPhysicianFactory_WhenUsed_ThenHasEmergencyMedicineSpecialty()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEmergencyPhysician()
            .Build();

        // Assert
        var practitioner = scenario.Practitioners[0];
        var specialtyCode = practitioner.MutableNode["qualification"]?[0]?["code"]?["coding"]?[0]?["code"]?.GetValue<string>();
        specialtyCode.ShouldBe("773568002");
    }

    [Fact]
    public void GivenSurgeonFactory_WhenUsed_ThenHasGeneralSurgerySpecialty()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddSurgeon()
            .Build();

        // Assert
        var practitioner = scenario.Practitioners[0];
        var specialtyCode = practitioner.MutableNode["qualification"]?[0]?["code"]?["coding"]?[0]?["code"]?.GetValue<string>();
        specialtyCode.ShouldBe("394609007");
    }

    [Fact]
    public void GivenNurseFactory_WhenUsed_ThenHasNursingSpecialty()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddNurse()
            .Build();

        // Assert
        var practitioner = scenario.Practitioners[0];
        var specialtyCode = practitioner.MutableNode["qualification"]?[0]?["code"]?["coding"]?[0]?["code"]?.GetValue<string>();
        specialtyCode.ShouldBe("224535009");
    }

    #endregion

    #region Context Integration Tests

    [Fact]
    public void GivenPractitioner_WhenGenerated_ThenSetAsCurrentPractitioner()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddFamilyPractitioner()
            .Build();

        // Assert
        scenario.CurrentPractitioner.ShouldNotBeNull();
        scenario.CurrentPractitioner.ShouldBe(scenario.Practitioners[0]);
    }

    [Fact]
    public void GivenMultiplePractitioners_WhenGenerated_ThenLastIsCurrentPractitioner()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddFamilyPractitioner()
            .AddCardiologist()
            .Build();

        // Assert
        scenario.Practitioners.Count.ShouldBe(2);
        scenario.CurrentPractitioner.ShouldBe(scenario.Practitioners[1]);
    }

    [Fact]
    public void GivenPractitioner_WhenGenerated_ThenAppearsInAllResources()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddFamilyPractitioner()
            .Build();

        // Assert
        scenario.AllResources.ShouldContain(scenario.Practitioners[0]);
    }

    [Fact]
    public void GivenPractitioner_WhenGenerated_ThenAppearsInTimeline()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddFamilyPractitioner()
            .Build();

        // Assert
        var practitionerEvents = scenario.Timeline.Where(e => e.EventType == "Practitioner").ToList();
        practitionerEvents.Count.ShouldBe(1);
    }

    #endregion

    #region Cross-Version Compatibility Tests

    [Fact]
    public void GivenR4Schema_WhenGeneratingPractitioner_ThenSucceeds()
    {
        // Arrange
        var schemaProvider = new R4CoreSchemaProvider();

        // Act
        var scenario = new ScenarioBuilder(schemaProvider)
            .WithPatient()
            .AddFamilyPractitioner()
            .Build();

        // Assert
        scenario.Practitioners.Count.ShouldBe(1);
    }

    [Fact]
    public void GivenR4BSchema_WhenGeneratingPractitioner_ThenSucceeds()
    {
        // Arrange
        var schemaProvider = new R4BCoreSchemaProvider();

        // Act
        var scenario = new ScenarioBuilder(schemaProvider)
            .WithPatient()
            .AddFamilyPractitioner()
            .Build();

        // Assert
        scenario.Practitioners.Count.ShouldBe(1);
    }

    [Fact]
    public void GivenR5Schema_WhenGeneratingPractitioner_ThenSucceeds()
    {
        // Arrange
        var schemaProvider = new R5CoreSchemaProvider();

        // Act
        var scenario = new ScenarioBuilder(schemaProvider)
            .WithPatient()
            .AddFamilyPractitioner()
            .Build();

        // Assert
        scenario.Practitioners.Count.ShouldBe(1);
    }

    [Fact]
    public void GivenSTU3Schema_WhenGeneratingPractitioner_ThenSucceeds()
    {
        // Arrange
        var schemaProvider = new STU3CoreSchemaProvider();

        // Act
        var scenario = new ScenarioBuilder(schemaProvider)
            .WithPatient()
            .AddFamilyPractitioner()
            .Build();

        // Assert
        scenario.Practitioners.Count.ShouldBe(1);
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public void GivenCustomGender_WhenProvided_ThenUsesCustomGender()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddPractitioner(new PractitionerState
            {
                Specialty = Specialties.FamilyMedicine,
                Gender = "female"
            })
            .Build();

        // Assert
        var practitioner = scenario.Practitioners[0];
        var gender = practitioner.MutableNode["gender"]?.GetValue<string>();
        gender.ShouldBe("female");
    }

    [Fact]
    public void GivenCustomQualifications_WhenProvided_ThenIncludesCustomQualifications()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddPractitioner(new PractitionerState
            {
                Specialty = Specialties.FamilyMedicine,
                Qualifications = ["Board Certified", "Fellowship Trained"]
            })
            .Build();

        // Assert
        var practitioner = scenario.Practitioners[0];
        var qualifications = practitioner.MutableNode["qualification"];
        // First qualification is the specialty, additional ones are custom
        qualifications?.AsArray().Count.ShouldBeGreaterThan(1);
    }

    [Fact]
    public void GivenPhysicianAssistant_WhenGenerated_ThenHasPaCsuffix()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddPractitioner(PractitionerState.PhysicianAssistant())
            .Build();

        // Assert
        var practitioner = scenario.Practitioners[0];
        var suffix = practitioner.MutableNode["name"]?[0]?["suffix"]?[0]?.GetValue<string>();
        suffix.ShouldBe("PA-C");
    }

    #endregion
}
