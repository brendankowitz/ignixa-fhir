// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
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
        scenario.Organizations.Should().HaveCount(1);
        var organization = scenario.Organizations[0];
        organization.ResourceType.Should().Be("Organization");
        organization.Id.Should().NotBeNullOrEmpty();
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
        name.Should().Be("Test General Hospital");
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
        var active = organization.MutableNode["active"]?.GetValue<bool>();
        active.Should().BeTrue();
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
        identifiers.Should().NotBeNull();

        var npiIdentifier = identifiers!.AsArray()
            .FirstOrDefault(i => i?["system"]?.GetValue<string>() == OrganizationState.NpiSystem);

        npiIdentifier.Should().NotBeNull();
        var npi = npiIdentifier!["value"]?.GetValue<string>();
        npi.Should().NotBeNullOrEmpty();
        npi.Should().HaveLength(10);
        OrganizationState.ValidateNpi(npi!).Should().BeTrue("NPI should pass Luhn check");
    }

    [Fact]
    public void GivenType2Npi_WhenValidated_ThenPassesLuhnCheck()
    {
        // Arrange
        var npi = OrganizationState.GenerateNpi();

        // Assert
        npi.Should().HaveLength(10);
        npi.Should().StartWith("2", "Type 2 NPI should start with 2 for organizations");
        OrganizationState.ValidateNpi(npi).Should().BeTrue("Generated NPI should be valid");
    }

    [Fact]
    public void GivenType1Npi_WhenValidated_ThenPassesLuhnCheck()
    {
        // Arrange
        var npi = OrganizationState.GenerateType1Npi();

        // Assert
        npi.Should().HaveLength(10);
        npi.Should().StartWith("1", "Type 1 NPI should start with 1 for individuals");
        OrganizationState.ValidateNpi(npi).Should().BeTrue("Generated NPI should be valid");
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
            OrganizationState.ValidateNpi(npi).Should().BeFalse($"NPI '{npi}' should be invalid");
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
        npis.Should().HaveCount(100, "Generated NPIs should be unique");
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

        taxIdIdentifier.Should().NotBeNull();
        var taxId = taxIdIdentifier!["value"]?.GetValue<string>();
        taxId.Should().NotBeNullOrEmpty();
        OrganizationState.ValidateTaxIdFormat(taxId!).Should().BeTrue("Tax ID should be in XX-XXXXXXX format");
    }

    [Fact]
    public void GivenGeneratedTaxId_WhenValidated_ThenHasCorrectFormat()
    {
        // Arrange
        var taxId = OrganizationState.GenerateTaxId();

        // Assert
        taxId.Should().HaveLength(10); // XX-XXXXXXX = 10 characters
        taxId[2].Should().Be('-');
        OrganizationState.ValidateTaxIdFormat(taxId).Should().BeTrue();
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
            OrganizationState.ValidateTaxIdFormat(taxId).Should().BeFalse($"Tax ID '{taxId}' should be invalid");
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
        typeArray.Should().NotBeNull();

        var typeCode = typeArray![0]?["coding"]?[0]?["code"]?.GetValue<string>();
        typeCode.Should().Be("prov");
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
        typeCode.Should().Be("ins");
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
        typeCode.Should().Be("dept");
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
        typeCode.Should().Be("pay");
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
        scenario.Organizations.Should().HaveCount(1);
        var name = scenario.Organizations[0].MutableNode["name"]?.GetValue<string>();
        name.Should().Be("City General Hospital");
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
        scenario.Organizations.Should().HaveCount(1);
        var name = scenario.Organizations[0].MutableNode["name"]?.GetValue<string>();
        name.Should().NotBeNullOrEmpty();
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
        scenario.Organizations.Should().HaveCount(1);
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
        scenario.Organizations.Should().HaveCount(1);
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
        scenario.Organizations.Should().HaveCount(1);
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
        scenario.Organizations.Should().HaveCount(1);
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
        scenario.Organizations.Should().HaveCount(1);
        var name = scenario.Organizations[0].MutableNode["name"]?.GetValue<string>();
        name.Should().Contain("Cardiology");
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
        telecom.Should().NotBeNull();

        var phoneEntry = telecom!.AsArray()
            .FirstOrDefault(t => t?["system"]?.GetValue<string>() == "phone");

        phoneEntry.Should().NotBeNull();
        var phone = phoneEntry!["value"]?.GetValue<string>();
        phone.Should().NotBeNullOrEmpty();
        phone.Should().MatchRegex(@"^\(\d{3}\) \d{3}-\d{4}$", "Phone should be in (XXX) XXX-XXXX format");
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

        emailEntry.Should().NotBeNull();
        var email = emailEntry!["value"]?.GetValue<string>();
        email.Should().NotBeNullOrEmpty();
        email.Should().Contain("@", "Email should contain @ symbol");
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
        addresses.Should().NotBeNull();

        var address = addresses![0];
        address!["city"]?.GetValue<string>().Should().NotBeNullOrEmpty();
        address["state"]?.GetValue<string>().Should().NotBeNullOrEmpty();
        address["postalCode"]?.GetValue<string>().Should().NotBeNullOrEmpty();
        address["country"]?.GetValue<string>().Should().Be("USA");
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
        address!["city"]?.GetValue<string>().Should().Be("Boston");
        address["state"]?.GetValue<string>().Should().Be("Massachusetts");
        address["postalCode"]?.GetValue<string>().Should().Be("02101");
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
        scenario.CurrentOrganization.Should().NotBeNull();
        scenario.CurrentOrganization.Should().Be(scenario.Organizations[0]);
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
        scenario.Organizations.Should().HaveCount(2);
        scenario.CurrentOrganization.Should().Be(scenario.Organizations[1]);
        var name = scenario.CurrentOrganization!.MutableNode["name"]?.GetValue<string>();
        name.Should().Be("Second Clinic");
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
        orgEvents.Should().HaveCount(1);
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
        scenario.AllResources.Should().Contain(scenario.Organizations[0]);
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
        scenario.Organizations.Should().HaveCount(1);
        scenario.Organizations[0].ResourceType.Should().Be("Organization");
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
        scenario.Organizations.Should().HaveCount(1);
        scenario.Organizations[0].ResourceType.Should().Be("Organization");
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
        scenario.Organizations.Should().HaveCount(1);
        scenario.Organizations[0].ResourceType.Should().Be("Organization");
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

        orgEntry.Should().NotBeNull();
        orgEntry!["resource"]?["name"]?.GetValue<string>().Should().Be("Test Hospital");
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
        scenario.Organizations.Should().HaveCount(2);
        scenario.CurrentOrganization!.MutableNode["name"]?.GetValue<string>().Should().Be("Main Hospital");
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

        npiIdentifier!["value"]?.GetValue<string>().Should().Be(customNpi);
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

        taxIdIdentifier!["value"]?.GetValue<string>().Should().Be(customTaxId);
    }

    #endregion
}
