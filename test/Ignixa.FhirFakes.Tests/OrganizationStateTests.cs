// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.FhirFakes.Scenarios;
using Ignixa.FhirFakes.Scenarios.States;
using Ignixa.Specification.Generated;

namespace Ignixa.FhirFakes.Tests;

/// <summary>
/// Tests for OrganizationState. Tests organization generation including NPI and Tax ID.
/// Uses AAA pattern and BDD naming convention.
/// </summary>
public class OrganizationStateTests
{
    private readonly R4CoreSchemaProvider _schemaProvider = new();

    #region Basic Generation Tests

    [Fact]
    public void GivenOrganization_WhenGenerated_ThenCreatesOrganization()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddHospital()
            .Build();

        // Assert
        scenario.Organizations.Count.ShouldBe(1);
        var organization = scenario.Organizations[0];
        organization.ResourceType.ShouldBe("Organization");
        organization.Id.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void GivenOrganization_WhenGenerated_ThenHasName()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddHospital("Test General Hospital")
            .Build();

        // Assert
        var organization = scenario.Organizations[0];
        var name = organization.MutableNode["name"]?.GetValue<string>();
        name.ShouldBe("Test General Hospital");
    }

    [Fact]
    public void GivenOrganization_WhenGenerated_ThenIsActive()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddHospital()
            .Build();

        // Assert
        var organization = scenario.Organizations[0];
        var active = organization.MutableNode["active"]?.GetValue<bool?>();
        active!.Value.ShouldBeTrue();
    }

    #endregion

    #region NPI Validation Tests

    [Fact]
    public void GivenOrganization_WhenGenerated_ThenHasValidNpi()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddHospital()
            .Build();

        // Assert
        var organization = scenario.Organizations[0];
        var identifiers = organization.MutableNode["identifier"];
        identifiers.ShouldNotBeNull();

        var npiIdentifier = identifiers!.AsArray()
            .FirstOrDefault(i => i?["system"]?.GetValue<string>() == OrganizationState.NpiSystem);

        npiIdentifier.ShouldNotBeNull();
        var npi = npiIdentifier!["value"]?.GetValue<string>();
        npi.ShouldNotBeNullOrEmpty();
        npi.Length.ShouldBe(10);
        OrganizationState.ValidateNpi(npi!).ShouldBeTrue("NPI should pass Luhn check");
    }

    [Fact]
    public void GivenType2Npi_WhenValidated_ThenPassesLuhnCheck()
    {
        // Arrange
        var npi = OrganizationState.GenerateNpi();

        // Assert
        npi.Length.ShouldBe(10);
        npi.ShouldStartWith("2", Case.Sensitive);
        OrganizationState.ValidateNpi(npi).ShouldBeTrue("Generated NPI should be valid");
    }

    [Fact]
    public void GivenType1Npi_WhenValidated_ThenPassesLuhnCheck()
    {
        // Arrange
        var npi = OrganizationState.GenerateType1Npi();

        // Assert
        npi.Length.ShouldBe(10);
        npi.ShouldStartWith("1", Case.Sensitive);
        OrganizationState.ValidateNpi(npi).ShouldBeTrue("Generated NPI should be valid");
    }

    [Fact]
    public void GivenInvalidNpi_WhenValidated_ThenReturnsFalse()
    {
        // Arrange - known invalid NPIs
        var invalidNpis = new[]
        {
            "1234567890", // Invalid check digit
            "2000000000", // Invalid check digit
            "12345",      // Too short
            "12345678901", // Too long
            "abcdefghij", // Non-numeric
            "",           // Empty
            "3123456789"  // Invalid prefix (not 1 or 2)
        };

        // Assert
        foreach (var npi in invalidNpis)
        {
            OrganizationState.ValidateNpi(npi).ShouldBeFalse($"NPI '{npi}' should be invalid");
        }
    }

    [Fact]
    public void GivenMultipleGeneratedNpis_WhenValidated_ThenAllAreUnique()
    {
        // Arrange
        var npis = new HashSet<string>();

        // Act
        for (int i = 0; i < 100; i++)
        {
            npis.Add(OrganizationState.GenerateNpi());
        }

        // Assert - All 100 NPIs should be unique
        npis.Count.ShouldBe(100, "Generated NPIs should be unique");
    }

    #endregion

    #region Tax ID Validation Tests

    [Fact]
    public void GivenOrganization_WhenGenerated_ThenHasTaxId()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddHospital()
            .Build();

        // Assert
        var organization = scenario.Organizations[0];
        var identifiers = organization.MutableNode["identifier"];

        var taxIdIdentifier = identifiers!.AsArray()
            .FirstOrDefault(i => i?["system"]?.GetValue<string>() == OrganizationState.TaxIdSystem);

        taxIdIdentifier.ShouldNotBeNull();
        var taxId = taxIdIdentifier!["value"]?.GetValue<string>();
        taxId.ShouldNotBeNullOrEmpty();
        OrganizationState.ValidateTaxIdFormat(taxId!).ShouldBeTrue("Tax ID should be in XX-XXXXXXX format");
    }

    [Fact]
    public void GivenGeneratedTaxId_WhenValidated_ThenHasCorrectFormat()
    {
        // Arrange
        var taxId = OrganizationState.GenerateTaxId();

        // Assert
        taxId.Length.ShouldBe(10); // XX-XXXXXXX = 10 characters
        taxId[2].ShouldBe('-');
        OrganizationState.ValidateTaxIdFormat(taxId).ShouldBeTrue();
    }

    [Fact]
    public void GivenInvalidTaxId_WhenValidated_ThenReturnsFalse()
    {
        // Arrange
        var invalidTaxIds = new[]
        {
            "123456789",   // Missing hyphen
            "12-3456",     // Too short
            "12-34567890", // Too long
            "AB-CDEFGHI",  // Non-numeric
            ""             // Empty
        };

        // Assert
        foreach (var taxId in invalidTaxIds)
        {
            OrganizationState.ValidateTaxIdFormat(taxId).ShouldBeFalse($"Tax ID '{taxId}' should be invalid");
        }
    }

    #endregion

    #region Organization Type Tests

    [Fact]
    public void GivenHospital_WhenGenerated_ThenHasProviderType()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddHospital()
            .Build();

        // Assert
        var organization = scenario.Organizations[0];
        var typeArray = organization.MutableNode["type"];
        typeArray.ShouldNotBeNull();

        var typeCode = typeArray![0]?["coding"]?[0]?["code"]?.GetValue<string>();
        typeCode.ShouldBe("prov");
    }

    [Fact]
    public void GivenInsuranceCompany_WhenGenerated_ThenHasInsuranceType()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddInsuranceCompany()
            .Build();

        // Assert
        var organization = scenario.Organizations[0];
        var typeArray = organization.MutableNode["type"];

        var typeCode = typeArray![0]?["coding"]?[0]?["code"]?.GetValue<string>();
        typeCode.ShouldBe("ins");
    }

    [Fact]
    public void GivenEmergencyDepartment_WhenGenerated_ThenHasDepartmentType()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEmergencyDepartment()
            .Build();

        // Assert
        var organization = scenario.Organizations[0];
        var typeArray = organization.MutableNode["type"];

        var typeCode = typeArray![0]?["coding"]?[0]?["code"]?.GetValue<string>();
        typeCode.ShouldBe("dept");
    }

    [Fact]
    public void GivenPayer_WhenGenerated_ThenHasPayerType()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddPayerOrganization()
            .Build();

        // Assert
        var organization = scenario.Organizations[0];
        var typeArray = organization.MutableNode["type"];

        var typeCode = typeArray![0]?["coding"]?[0]?["code"]?.GetValue<string>();
        typeCode.ShouldBe("pay");
    }

    #endregion

    #region Factory Method Tests

    [Fact]
    public void GivenHospitalFactory_WhenGenerated_ThenCreatesHospital()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddHospital("City General Hospital")
            .Build();

        // Assert
        scenario.Organizations.Count.ShouldBe(1);
        var name = scenario.Organizations[0].MutableNode["name"]?.GetValue<string>();
        name.ShouldBe("City General Hospital");
    }

    [Fact]
    public void GivenClinicFamilyPracticeFactory_WhenGenerated_ThenCreatesClinic()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddClinicFamilyPractice()
            .Build();

        // Assert
        scenario.Organizations.Count.ShouldBe(1);
        var name = scenario.Organizations[0].MutableNode["name"]?.GetValue<string>();
        name.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void GivenLaboratoryFactory_WhenGenerated_ThenCreatesLab()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddLaboratory()
            .Build();

        // Assert
        scenario.Organizations.Count.ShouldBe(1);
    }

    [Fact]
    public void GivenPharmacyFactory_WhenGenerated_ThenCreatesPharmacy()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddPharmacy()
            .Build();

        // Assert
        scenario.Organizations.Count.ShouldBe(1);
    }

    [Fact]
    public void GivenImagingCenterFactory_WhenGenerated_ThenCreatesImagingCenter()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddImagingCenter()
            .Build();

        // Assert
        scenario.Organizations.Count.ShouldBe(1);
    }

    [Fact]
    public void GivenUrgentCareFactory_WhenGenerated_ThenCreatesUrgentCare()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddUrgentCare()
            .Build();

        // Assert
        scenario.Organizations.Count.ShouldBe(1);
    }

    [Fact]
    public void GivenSpecialtyClinicFactory_WhenGenerated_ThenCreatesSpecialtyClinic()
    {
        // Arrange
        var state = OrganizationState.SpecialtyClinic("Cardiology");

        // Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddOrganization(state)
            .Build();

        // Assert
        scenario.Organizations.Count.ShouldBe(1);
        var name = scenario.Organizations[0].MutableNode["name"]?.GetValue<string>();
        name!.ShouldContain("Cardiology");
    }

    #endregion

    #region Telecom Tests

    [Fact]
    public void GivenOrganization_WhenGenerated_ThenHasPhone()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddHospital()
            .Build();

        // Assert
        var organization = scenario.Organizations[0];
        var telecom = organization.MutableNode["telecom"];
        telecom.ShouldNotBeNull();

        var phoneEntry = telecom!.AsArray()
            .FirstOrDefault(t => t?["system"]?.GetValue<string>() == "phone");

        phoneEntry.ShouldNotBeNull();
        var phone = phoneEntry!["value"]?.GetValue<string>();
        phone.ShouldNotBeNullOrEmpty();
        // Phone format varies by country: US uses (XXX) XXX-XXXX, international may use different formats
        phone!.ShouldContain("-");
        phone.ShouldMatch(@"[\d\(\)\s\-]+");
    }

    [Fact]
    public void GivenOrganization_WhenGenerated_ThenHasEmail()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddHospital()
            .Build();

        // Assert
        var organization = scenario.Organizations[0];
        var telecom = organization.MutableNode["telecom"];

        var emailEntry = telecom!.AsArray()
            .FirstOrDefault(t => t?["system"]?.GetValue<string>() == "email");

        emailEntry.ShouldNotBeNull();
        var email = emailEntry!["value"]?.GetValue<string>();
        email.ShouldNotBeNullOrEmpty();
        email!.ShouldContain("@");
    }

    #endregion

    #region Address Tests

    [Fact]
    public void GivenOrganization_WhenGenerated_ThenHasAddress()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddHospital()
            .Build();

        // Assert
        var organization = scenario.Organizations[0];
        var addresses = organization.MutableNode["address"];
        addresses.ShouldNotBeNull();

        var address = addresses![0];
        address!["city"]?.GetValue<string>().ShouldNotBeNullOrEmpty();
        address["state"]?.GetValue<string>().ShouldNotBeNullOrEmpty();
        address["postalCode"]?.GetValue<string>().ShouldNotBeNullOrEmpty();
        address["country"]?.GetValue<string>().ShouldBe("USA");
    }

    [Fact]
    public void GivenOrganizationWithCustomAddress_WhenGenerated_ThenUsesCustomAddress()
    {
        // Arrange
        var customAddress = new OrganizationAddress(
            Line: "123 Healthcare Way",
            City: "Boston",
            State: "Massachusetts",
            PostalCode: "02101",
            Country: "USA"
        );

        var state = new OrganizationState
        {
            OrganizationName = "Custom Hospital",
            Address = customAddress
        };

        // Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddOrganization(state)
            .Build();

        // Assert
        var organization = scenario.Organizations[0];
        var address = organization.MutableNode["address"]![0];
        address!["city"]?.GetValue<string>().ShouldBe("Boston");
        address["state"]?.GetValue<string>().ShouldBe("Massachusetts");
        address["postalCode"]?.GetValue<string>().ShouldBe("02101");
    }

    #endregion

    #region Context Integration Tests

    [Fact]
    public void GivenOrganization_WhenGenerated_ThenSetAsCurrentOrganization()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddHospital()
            .Build();

        // Assert
        scenario.CurrentOrganization.ShouldNotBeNull();
        scenario.CurrentOrganization.ShouldBe(scenario.Organizations[0]);
    }

    [Fact]
    public void GivenMultipleOrganizations_WhenGenerated_ThenLastIsCurrentOrganization()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddHospital("First Hospital")
            .AddClinicFamilyPractice("Second Clinic")
            .Build();

        // Assert
        scenario.Organizations.Count.ShouldBe(2);
        scenario.CurrentOrganization.ShouldBe(scenario.Organizations[1]);
        var name = scenario.CurrentOrganization!.MutableNode["name"]?.GetValue<string>();
        name.ShouldBe("Second Clinic");
    }

    [Fact]
    public void GivenOrganization_WhenGenerated_ThenAppearsInTimeline()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddHospital()
            .Build();

        // Assert
        var orgEvents = scenario.Timeline.Where(e => e.EventType == "Organization").ToList();
        orgEvents.Count.ShouldBe(1);
    }

    [Fact]
    public void GivenOrganization_WhenGenerated_ThenAppearsInAllResources()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddHospital()
            .Build();

        // Assert
        scenario.AllResources.ShouldContain(scenario.Organizations[0]);
    }

    #endregion

    #region Cross-Version Compatibility Tests

    [Fact]
    public void GivenOrganization_WhenGeneratedWithSTU3_ThenCreatesValidResource()
    {
        // Arrange
        var stu3Provider = new STU3CoreSchemaProvider();

        // Act
        var scenario = new ScenarioBuilder(stu3Provider)
            .WithPatient()
            .AddHospital()
            .Build();

        // Assert
        scenario.Organizations.Count.ShouldBe(1);
        scenario.Organizations[0].ResourceType.ShouldBe("Organization");
    }

    [Fact]
    public void GivenOrganization_WhenGeneratedWithR4B_ThenCreatesValidResource()
    {
        // Arrange
        var r4bProvider = new R4BCoreSchemaProvider();

        // Act
        var scenario = new ScenarioBuilder(r4bProvider)
            .WithPatient()
            .AddHospital()
            .Build();

        // Assert
        scenario.Organizations.Count.ShouldBe(1);
        scenario.Organizations[0].ResourceType.ShouldBe("Organization");
    }

    [Fact]
    public void GivenOrganization_WhenGeneratedWithR5_ThenCreatesValidResource()
    {
        // Arrange
        var r5Provider = new R5CoreSchemaProvider();

        // Act
        var scenario = new ScenarioBuilder(r5Provider)
            .WithPatient()
            .AddHospital()
            .Build();

        // Assert
        scenario.Organizations.Count.ShouldBe(1);
        scenario.Organizations[0].ResourceType.ShouldBe("Organization");
    }

    #endregion

    #region Bundle Integration Tests

    [Fact]
    public void GivenScenarioWithOrganization_WhenConvertedToBundle_ThenIncludesOrganization()
    {
        // Arrange
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddHospital("Test Hospital")
            .Build();

        // Act
        var bundle = scenario.ToBundle();

        // Assert
        var entries = bundle.MutableNode["entry"]!.AsArray();
        var orgEntry = entries.FirstOrDefault(e =>
            e?["resource"]?["resourceType"]?.GetValue<string>() == "Organization");

        orgEntry.ShouldNotBeNull();
        orgEntry!["resource"]?["name"]?.GetValue<string>().ShouldBe("Test Hospital");
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public void GivenOrganizationWithSetAsCurrentFalse_WhenGenerated_ThenDoesNotSetAsCurrent()
    {
        // Arrange
        var state = new OrganizationState
        {
            OrganizationName = "Background Hospital",
            SetAsCurrent = false
        };

        // Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddHospital("Main Hospital")
            .AddOrganization(state)
            .Build();

        // Assert
        scenario.Organizations.Count.ShouldBe(2);
        scenario.CurrentOrganization!.MutableNode["name"]?.GetValue<string>().ShouldBe("Main Hospital");
    }

    [Fact]
    public void GivenOrganizationWithCustomNpi_WhenGenerated_ThenUsesCustomNpi()
    {
        // Arrange
        var customNpi = "2345678903"; // Valid Type 2 NPI
        var state = new OrganizationState
        {
            OrganizationName = "Custom NPI Hospital",
            NpiNumber = customNpi
        };

        // Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddOrganization(state)
            .Build();

        // Assert
        var organization = scenario.Organizations[0];
        var identifiers = organization.MutableNode["identifier"];
        var npiIdentifier = identifiers!.AsArray()
            .FirstOrDefault(i => i?["system"]?.GetValue<string>() == OrganizationState.NpiSystem);

        npiIdentifier!["value"]?.GetValue<string>().ShouldBe(customNpi);
    }

    [Fact]
    public void GivenOrganizationWithCustomTaxId_WhenGenerated_ThenUsesCustomTaxId()
    {
        // Arrange
        var customTaxId = "12-3456789";
        var state = new OrganizationState
        {
            OrganizationName = "Custom Tax ID Hospital",
            TaxId = customTaxId
        };

        // Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddOrganization(state)
            .Build();

        // Assert
        var organization = scenario.Organizations[0];
        var identifiers = organization.MutableNode["identifier"];
        var taxIdIdentifier = identifiers!.AsArray()
            .FirstOrDefault(i => i?["system"]?.GetValue<string>() == OrganizationState.TaxIdSystem);

        taxIdIdentifier!["value"]?.GetValue<string>().ShouldBe(customTaxId);
    }

    #endregion
}
