// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.FhirFakes.Builders;
using Ignixa.FhirFakes.Builders.Profiles;
using Ignixa.FhirFakes.Population;
using Ignixa.Specification;
using Ignixa.Specification.Generated;
using Xunit;

namespace Ignixa.FhirFakes.Tests.Builders;

/// <summary>
/// Unit tests for PatientBuilder.
/// Tests both simple and realistic patient generation modes.
/// </summary>
public class PatientBuilderTests
{
    private readonly IFhirSchemaProvider _schemaProvider = new R4CoreSchemaProvider();

    #region Simple Mode Tests

    [Fact]
    public void GivenSimpleBuilder_WhenBuildingWithBasicDemographics_ThenCreatesPatient()
    {
        // Arrange & Act
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .WithAge(45)
            .WithGender(g => g.Male)  // Using selector pattern for discoverability
            .WithGivenName("John")
            .WithFamilyName("Smith")
            .Build();

        // Assert
        patient.Should().NotBeNull();
        patient.ResourceType.Should().Be("Patient");
        patient.MutableNode["gender"]?.GetValue<string>().Should().Be("male");
        patient.MutableNode["birthDate"]?.GetValue<string>().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GivenSimpleBuilder_WhenUsingSelectorPattern_ThenCreatesPatient()
    {
        // Arrange & Act
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .WithAge(32)
            .WithGender(g => g.Female)  // Selector makes options discoverable
            .WithProfile(USCorePatientProfile.Instance)
            .WithAttribute(USCorePatientProfile.UsCoreRaceAttribute, USCorePatientProfile.Race.Hispanic)
            .Build();

        // Assert
        patient.Should().NotBeNull();
        patient.MutableNode["gender"]?.GetValue<string>().Should().Be("female");

        // Should have ethnicity extension
        patient.MutableNode["extension"].Should().NotBeNull();
    }

    [Fact]
    public void GivenSimpleBuilder_WhenBuildingWithAddress_ThenIncludesAddressInResource()
    {
        // Arrange & Act
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .WithAge(32)
            .WithGender("female")
            .WithAddress("123 Main St", "Seattle", "WA", "98101")
            .Build();

        // Assert
        patient.MutableNode["address"].Should().NotBeNull();
        var addresses = patient.MutableNode["address"]?.AsArray();
        addresses.Should().HaveCount(1);

        var address = addresses?[0]?.AsObject();
        address?["city"]?.GetValue<string>().Should().Be("Seattle");
        address?["state"]?.GetValue<string>().Should().Be("WA");
        address?["postalCode"]?.GetValue<string>().Should().Be("98101");
    }

    [Fact]
    public void GivenSimpleBuilder_WhenBuildingWithZipCodeOnly_ThenGeneratesAddress()
    {
        // Arrange & Act
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .WithAge(28)
            .WithGender("male")
            .WithZipCode("02101")
            .Build();

        // Assert
        patient.MutableNode["address"].Should().NotBeNull();
        var addresses = patient.MutableNode["address"]?.AsArray();
        addresses.Should().HaveCount(1);

        var address = addresses?[0]?.AsObject();
        address?["postalCode"]?.GetValue<string>().Should().Be("02101");
        address?["line"].Should().NotBeNull(); // Street should be auto-generated
    }

    [Fact]
    public void GivenSimpleBuilder_WhenBuildingWithAreaCode_ThenGeneratesPhoneNumber()
    {
        // Arrange & Act
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .WithAge(40)
            .WithGender("female")
            .WithAreaCode("617")
            .Build();

        // Assert
        patient.MutableNode["telecom"].Should().NotBeNull();
        var telecoms = patient.MutableNode["telecom"]?.AsArray();
        telecoms.Should().HaveCount(1);

        var telecom = telecoms?[0]?.AsObject();
        telecom?["system"]?.GetValue<string>().Should().Be("phone");
        telecom?["value"]?.GetValue<string>().Should().StartWith("617-");
    }

    [Fact]
    public void GivenSimpleBuilder_WhenBuildingWithTag_ThenIncludesTagInMeta()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Act
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .WithAge(35)
            .WithGender("male")
            .WithTag(tag)
            .Build();

        // Assert
        patient.MutableNode["meta"]?["tag"].Should().NotBeNull();
        var tags = patient.MutableNode["meta"]?["tag"]?.AsArray();
        tags.Should().HaveCount(1);

        var metaTag = tags?[0]?.AsObject();
        metaTag?["code"]?.GetValue<string>().Should().Be(tag);
    }

    [Fact]
    public void GivenSimpleBuilder_WhenBuildingWithId_ThenUsesProvidedId()
    {
        // Arrange
        var expectedId = "patient-123";

        // Act
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .WithAge(50)
            .WithGender("female")
            .WithId(expectedId)
            .Build();

        // Assert
        patient.Id.Should().Be(expectedId);
    }

    [Fact]
    public void GivenSimpleBuilder_WhenBuildingWithBirthYear_ThenUsesBirthYear()
    {
        // Arrange & Act
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .WithBirthYear(1980)
            .WithGender("male")
            .Build();

        // Assert
        patient.MutableNode["birthDate"]?.GetValue<string>().Should().StartWith("1980");
    }

    [Fact]
    public void GivenSimpleBuilder_WhenBuildingWithActive_ThenSetsActiveStatus()
    {
        // Arrange & Act
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .WithAge(60)
            .WithGender("female")
            .WithActive(false)
            .Build();

        // Assert
        patient.MutableNode["active"]?.GetValue<bool>().Should().BeFalse();
    }

    #endregion

    #region Realistic Mode Tests

    [Fact]
    public void GivenRealisticBuilder_WhenBuildingFromCity_ThenGeneratesRealisticDemographics()
    {
        // Arrange & Act
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .FromCity(KnownCities.Boston)  // Using selector for best discoverability
            .Build();

        // Assert
        patient.Should().NotBeNull();
        patient.ResourceType.Should().Be("Patient");
        patient.MutableNode["name"].Should().NotBeNull();
        patient.MutableNode["gender"].Should().NotBeNull();

        // Should have address with ZIP code from Boston demographics
        patient.MutableNode["address"].Should().NotBeNull();
        var address = patient.MutableNode["address"]?.AsArray()?[0]?.AsObject();
        address?["postalCode"]?.GetValue<string>().Should().StartWith("02"); // Boston ZIP prefix

        // Should have phone with area code from Boston demographics
        patient.MutableNode["telecom"].Should().NotBeNull();
        var telecom = patient.MutableNode["telecom"]?.AsArray()?[0]?.AsObject();
        var phoneValue = telecom?["value"]?.GetValue<string>();
        phoneValue.Should().Match(p => p.StartsWith("617-") || p.StartsWith("857-")); // Boston area codes
    }

    [Fact]
    public void GivenRealisticBuilder_WhenUsingFromCityAndOverridingAge_ThenUsesOverriddenAge()
    {
        // Arrange & Act
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .FromCity(KnownCities.NewYork)  // Using KnownCities
            .WithAge(45)  // Override auto-generated age
            .Build();

        // Assert
        var birthDate = patient.MutableNode["birthDate"]?.GetValue<string>();
        var expectedYear = DateTime.UtcNow.Year - 45;
        birthDate.Should().StartWith(expectedYear.ToString());
    }

    [Fact]
    public void GivenRealisticBuilder_WhenUsingCityStatePair_ThenGeneratesRealisticDemographics()
    {
        // Arrange & Act
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .FromCity(KnownCities.Chicago)
            .Build();

        // Assert
        patient.Should().NotBeNull();
        patient.ResourceType.Should().Be("Patient");

        // Should have address with ZIP code from Chicago demographics
        patient.MutableNode["address"].Should().NotBeNull();
        var address = patient.MutableNode["address"]?.AsArray()?[0]?.AsObject();
        address?["postalCode"]?.GetValue<string>().Should().StartWith("606"); // Chicago ZIP prefix

        // Should have phone with area code from Chicago demographics
        patient.MutableNode["telecom"].Should().NotBeNull();
        var telecom = patient.MutableNode["telecom"]?.AsArray()?[0]?.AsObject();
        var phoneValue = telecom?["value"]?.GetValue<string>();
        phoneValue.Should().Match(p => p.StartsWith("312-") || p.StartsWith("773-") || p.StartsWith("872-")); // Chicago area codes
    }

    [Fact]
    public void GivenRealisticBuilder_WhenUsingWithEthnicName_ThenGeneratesEthnicName()
    {
        // Arrange & Act
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .WithProfile(USCorePatientProfile.Instance)
            .WithAttribute(USCorePatientProfile.UsCoreRaceAttribute, USCorePatientProfile.Race.Hispanic)
            .WithGender(g => g.Female)
            .WithName()
            .WithAge(30)
            .Build();

        // Assert
        patient.MutableNode["name"].Should().NotBeNull();
        var names = patient.MutableNode["name"]?.AsArray();
        names.Should().HaveCount(1);

        var name = names?[0]?.AsObject();
        name?["family"].Should().NotBeNull();
        name?["given"].Should().NotBeNull();

        // Should have US Core ethnicity extension (using us-core-race URL per FHIR spec)
        patient.MutableNode["extension"].Should().NotBeNull();
    }

    [Fact]
    public void GivenRealisticBuilder_WhenUsingWithRealisticBMI_ThenGeneratesBMIInRange()
    {
        // Arrange & Act
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .WithAge(40)
            .WithGender("male")
            .WithRealisticBMI()
            .Build();

        // Assert
        patient.MutableNode["extension"].Should().NotBeNull();
        var extensions = patient.MutableNode["extension"]?.AsArray();

        // Find BMI extension
        var bmiExtension = extensions?
            .FirstOrDefault(e => e?["url"]?.GetValue<string>() == "http://ignixa.dev/StructureDefinition/patient-bmi");

        bmiExtension.Should().NotBeNull();
        var bmi = bmiExtension?["valueDecimal"]?.GetValue<decimal>();
        bmi.Should().BeGreaterOrEqualTo(19).And.BeLessOrEqualTo(42); // NHANES range
    }

    [Fact]
    public void GivenRealisticBuilder_WhenUsingCustomCity_ThenUsesProvidedDemographics()
    {
        // Arrange & Act - Create a custom city with specific demographics
        var customCity = new CityDemographics(
            Name: "TestCity",
            State: "TestState",
            Country: "US",
            Population: 100000,
            AgeGroupDistribution: new Dictionary<string, double> { { "18-44", 1.0 } },
            MaleRatio: 0.5,
            ZipCodePrefix: "123",
            AreaCodes: ["555"],
            Attributes: new Dictionary<string, object>
            {
                [USCorePatientProfile.EthnicityDistributionKey] = new Dictionary<string, double> { { "White", 1.0 } }
            });

        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .FromCity(customCity)
            .Build();

        // Assert
        patient.Should().NotBeNull();
        var address = patient.MutableNode["address"]?.AsArray()?[0]?.AsObject();
        address?["city"]?.GetValue<string>().Should().Be("TestCity");
        address?["state"]?.GetValue<string>().Should().Be("TestState");
        address?["postalCode"]?.GetValue<string>().Should().StartWith("123");
    }

    [Fact]
    public void GivenRealisticBuilder_WhenUsingFromSeattle_ThenGeneratesSeattleDemographics()
    {
        // Arrange & Act
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .FromSeattle()
            .Build();

        // Assert
        patient.Should().NotBeNull();
        patient.ResourceType.Should().Be("Patient");

        // Should have address with Seattle details
        patient.MutableNode["address"].Should().NotBeNull();
        var address = patient.MutableNode["address"]?.AsArray()?[0]?.AsObject();
        address?["city"]?.GetValue<string>().Should().Be("Seattle");
        address?["state"]?.GetValue<string>().Should().Be("Washington");

        // Should have name and demographics
        patient.MutableNode["name"].Should().NotBeNull();
        patient.MutableNode["gender"].Should().NotBeNull();
        patient.MutableNode["birthDate"].Should().NotBeNull();
    }

    [Fact]
    public void GivenRealisticBuilder_WhenBuildingFromInternationalCity_ThenDoesNotIncludeUSCoreExtensions()
    {
        // Arrange & Act - Create patient from Melbourne, Australia
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .FromCity(KnownCities.Melbourne)
            .Build();

        // Assert
        patient.Should().NotBeNull();
        patient.ResourceType.Should().Be("Patient");

        // Should have Australian address details
        patient.MutableNode["address"].Should().NotBeNull();
        var address = patient.MutableNode["address"]?.AsArray()?[0]?.AsObject();
        address?["city"]?.GetValue<string>().Should().Be("Melbourne");
        address?["state"]?.GetValue<string>().Should().Be("Victoria");
        address?["country"]?.GetValue<string>().Should().Be("AU");

        // Should NOT have USCore ethnicity extensions
        var extensions = patient.MutableNode["extension"]?.AsArray();
        if (extensions != null)
        {
            // If extensions exist, they should NOT be USCore race or ethnicity
            var usCoreRaceUrl = "http://hl7.org/fhir/us/core/StructureDefinition/us-core-race";
            var usCoreHispanicOriginUrl = "http://hl7.org/fhir/us/core/StructureDefinition/us-core-ethnicity";

            foreach (var extension in extensions)
            {
                var url = extension?["url"]?.GetValue<string>();
                url.Should().NotBe(usCoreRaceUrl);
                url.Should().NotBe(usCoreHispanicOriginUrl);
            }
        }
    }

    [Fact]
    public void GivenRealisticBuilder_WhenBuildingUSPatientWithEthnicity_ThenIncludesUSCoreExtensions()
    {
        // Arrange & Act - Create patient from US city with ethnicity
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .FromCity(KnownCities.Boston)
            .Build();

        // Assert
        patient.Should().NotBeNull();
        patient.ResourceType.Should().Be("Patient");

        // Should have US address
        var address = patient.MutableNode["address"]?.AsArray()?[0]?.AsObject();
        address?["country"]?.GetValue<string>().Should().Be("US");

        // Should have USCore race extension (ethnicity is sampled from Boston demographics, uses us-core-race URL per FHIR spec)
        var extensions = patient.MutableNode["extension"]?.AsArray();
        extensions.Should().NotBeNull();

        var usCoreRaceUrl = "http://hl7.org/fhir/us/core/StructureDefinition/us-core-race";
        var hasUsCoreRace = false;
        foreach (var extension in extensions!)
        {
            if (extension?["url"]?.GetValue<string>() == usCoreRaceUrl)
            {
                hasUsCoreRace = true;
                break;
            }
        }
        hasUsCoreRace.Should().BeTrue("US patients should have USCore race extension (for ethnicity)");
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void GivenSimpleBuilder_WhenNoParametersProvided_ThenBuildsWithDefaults()
    {
        // Arrange & Act
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .Build();

        // Assert
        patient.Should().NotBeNull();
        patient.ResourceType.Should().Be("Patient");
        patient.Id.Should().NotBeNullOrEmpty();
        patient.MutableNode["gender"].Should().NotBeNull();
        patient.MutableNode["birthDate"].Should().NotBeNull();
        patient.MutableNode["name"].Should().NotBeNull();
        patient.MutableNode["active"]?.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void GivenSimpleBuilder_WhenCalledFromCityWithoutDemographics_ThenThrowsInvalidOperationException()
    {
        // Arrange & Act - Use direct constructor to create simple builder WITHOUT dependencies
        var act = () => new PatientBuilder(_schemaProvider)
            .FromCity(KnownCities.Boston)
            .Build();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*DemographicsDataProvider required*");
    }

    [Fact]
    public void GivenSimpleBuilder_WhenCalledWithEthnicNameWithoutGenerator_ThenThrowsInvalidOperationException()
    {
        // Arrange & Act - Use direct constructor to create simple builder WITHOUT dependencies
        var act = () => new PatientBuilder(_schemaProvider)
            .WithName()
            .Build();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*LocalBasedNameGenerator required*");
    }

    [Fact]
    public void GivenRealisticBuilder_WhenBuildingMultiplePatients_ThenGeneratesDifferentPatients()
    {
        // Arrange & Act
        var patient1 = PatientBuilderFactory.Create(_schemaProvider)
            .FromCity(KnownCities.Chicago)
            .Build();

        var patient2 = PatientBuilderFactory.Create(_schemaProvider)
            .FromCity(KnownCities.Chicago)
            .Build();

        // Assert
        patient1.Id.Should().NotBe(patient2.Id);
        // Names may differ due to random sampling
    }

    #endregion

    #region Profile-Aware Tests

    [Fact]
    public void GivenRealisticBuilder_WhenBuildingFromUSCity_ThenUsesUSCoreProfile()
    {
        // Arrange & Act
        var builder = PatientBuilderFactory.Create(_schemaProvider)
            .FromCity(KnownCities.Boston);

        // Assert - Check profile is US Core
        builder.Profile.Should().Be(USCorePatientProfile.Instance);
        builder.Profile.ProfileUrl.Should().Be("http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient");
        builder.Profile.CountryCode.Should().Be("US");
    }

    [Fact]
    public void GivenRealisticBuilder_WhenBuildingFromAUCity_ThenUsesAUBaseProfile()
    {
        // Arrange & Act
        var builder = PatientBuilderFactory.Create(_schemaProvider)
            .FromCity(KnownCities.Melbourne);

        // Assert - Check profile is AU Base
        builder.Profile.Should().Be(AUBasePatientProfile.Instance);
        builder.Profile.ProfileUrl.Should().Be("http://hl7.org.au/fhir/StructureDefinition/au-patient");
        builder.Profile.CountryCode.Should().Be("AU");
    }

    [Fact]
    public void GivenRealisticBuilder_WhenBuildingFromAUCity_ThenIncludesIndigenousStatusExtension()
    {
        // Arrange & Act
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .FromCity(KnownCities.Melbourne)
            .Build();

        // Assert
        patient.Should().NotBeNull();
        patient.ResourceType.Should().Be("Patient");

        // Should have AU Base indigenous status extension
        var extensions = patient.MutableNode["extension"]?.AsArray();
        extensions.Should().NotBeNull();

        var indigenousStatusUrl = "http://hl7.org.au/fhir/StructureDefinition/indigenous-status";
        var hasIndigenousStatus = false;
        foreach (var extension in extensions!)
        {
            if (extension?["url"]?.GetValue<string>() == indigenousStatusUrl)
            {
                hasIndigenousStatus = true;

                // Verify the coding structure
                var valueCoding = extension["valueCoding"]?.AsObject();
                valueCoding.Should().NotBeNull();
                valueCoding?["system"]?.GetValue<string>().Should().Be("https://healthterminologies.gov.au/fhir/CodeSystem/australian-indigenous-status-1");
                valueCoding?["code"]?.GetValue<string>().Should().BeOneOf("1", "2", "3", "4", "9");
                valueCoding?["display"]?.GetValue<string>().Should().NotBeNullOrEmpty();
                break;
            }
        }
        hasIndigenousStatus.Should().BeTrue("AU patients should have indigenous status extension");
    }

    [Fact]
    public void GivenRealisticBuilder_WhenBuildingFromSydney_ThenIncludesIndigenousStatusExtension()
    {
        // Arrange & Act
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .FromCity(KnownCities.Sydney)
            .Build();

        // Assert - Check address is Sydney
        var address = patient.MutableNode["address"]?.AsArray()?[0]?.AsObject();
        address?["city"]?.GetValue<string>().Should().Be("Sydney");
        address?["state"]?.GetValue<string>().Should().Be("New South Wales");
        address?["country"]?.GetValue<string>().Should().Be("AU");

        // Should have indigenous status extension
        var extensions = patient.MutableNode["extension"]?.AsArray();
        extensions.Should().NotBeNull();
        extensions!.Any(e => e?["url"]?.GetValue<string>() == "http://hl7.org.au/fhir/StructureDefinition/indigenous-status")
            .Should().BeTrue();
    }

    [Fact]
    public void GivenRealisticBuilder_WhenBuildingFromAmsterdam_ThenUsesDefaultProfile()
    {
        // Arrange & Act
        var builder = PatientBuilderFactory.Create(_schemaProvider)
            .FromCity(KnownCities.Amsterdam);

        // Assert - Check profile is Default (no NL profile implemented)
        builder.Profile.Should().Be(DefaultPatientProfile.Instance);
        builder.Profile.ProfileUrl.Should().Be("http://hl7.org/fhir/StructureDefinition/Patient");
    }

    [Fact]
    public void GivenRealisticBuilder_WhenBuildingFromAmsterdam_ThenNoCountrySpecificExtensions()
    {
        // Arrange & Act
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .FromCity(KnownCities.Amsterdam)
            .Build();

        // Assert
        patient.Should().NotBeNull();

        // Should have NL address
        var address = patient.MutableNode["address"]?.AsArray()?[0]?.AsObject();
        address?["city"]?.GetValue<string>().Should().Be("Amsterdam");
        address?["country"]?.GetValue<string>().Should().Be("NL");

        // Extensions should be null or empty (no profile-specific extensions)
        var extensions = patient.MutableNode["extension"]?.AsArray();
        if (extensions != null)
        {
            // Should NOT have US Core or AU Base extensions
            foreach (var extension in extensions)
            {
                var url = extension?["url"]?.GetValue<string>();
                url.Should().NotContain("us-core");
                url.Should().NotContain("hl7.org.au");
            }
        }
    }

    [Fact]
    public void GivenSimpleBuilder_WhenUsingWithProfile_ThenUsesSpecifiedProfile()
    {
        // Arrange & Act
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .WithProfile(AUBasePatientProfile.Instance)
            .WithAttribute(AUBasePatientProfile.IndigenousStatusAttribute, "4")
            .WithAge(35)
            .WithGender("male")
            .Build();

        // Assert
        patient.Should().NotBeNull();

        // Should have AU Base indigenous status extension
        var extensions = patient.MutableNode["extension"]?.AsArray();
        extensions.Should().NotBeNull();

        var indigenousStatusUrl = "http://hl7.org.au/fhir/StructureDefinition/indigenous-status";
        extensions!.Any(e => e?["url"]?.GetValue<string>() == indigenousStatusUrl)
            .Should().BeTrue();
    }

    [Fact]
    public void GivenSimpleBuilder_WhenUsingWithAttribute_ThenStoresAttribute()
    {
        // Arrange & Act
        var builder = PatientBuilderFactory.Create(_schemaProvider)
            .WithAttribute("customKey", "customValue")
            .WithAge(30);

        // Assert
        builder.ProfileAttributes.Should().ContainKey("customKey");
        builder.ProfileAttributes["customKey"].Should().Be("customValue");
    }

    [Fact]
    public void GivenSimpleBuilder_WhenSettingEthnicityViaAttribute_ThenUsesUSCoreProfile()
    {
        // Arrange & Act
        var builder = PatientBuilderFactory.Create(_schemaProvider)
            .WithProfile(USCorePatientProfile.Instance)
            .WithAttribute(USCorePatientProfile.UsCoreRaceAttribute, USCorePatientProfile.Race.Hispanic)
            .WithAge(40);

        // Assert - Profile should be US Core when explicitly set
        builder.Profile.Should().Be(USCorePatientProfile.Instance);
        builder.ProfileAttributes.Should().ContainKey(USCorePatientProfile.UsCoreRaceAttribute)
            .WhoseValue.Should().Be(USCorePatientProfile.Race.Hispanic);
    }

    [Fact]
    public void GivenPatientBuilder_WhenEthnicityAccessedViaProperty_ThenReturnsFromAttributes()
    {
        // Arrange & Act
        var builder = PatientBuilderFactory.Create(_schemaProvider)
            .FromCity(KnownCities.Boston);

        // Assert - Ethnicity should be accessible via ProfileAttributes
        builder.ProfileAttributes.Should().ContainKey(USCorePatientProfile.UsCoreRaceAttribute);
        builder.ProfileAttributes[USCorePatientProfile.UsCoreRaceAttribute].Should().NotBeNull();
    }

    [Fact]
    public void GivenCityDemographics_WhenAccessingProfileUrl_ThenReturnsCorrectUrl()
    {
        // Assert
        KnownCities.Boston.ProfileUrl.Should().Be("http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient");
        KnownCities.Melbourne.ProfileUrl.Should().Be("http://hl7.org.au/fhir/StructureDefinition/au-patient");
        KnownCities.Amsterdam.ProfileUrl.Should().Be("http://hl7.org/fhir/StructureDefinition/Patient");
    }

    [Fact]
    public void GivenPatientProfileFactory_WhenGettingProfileByCountry_ThenReturnsCorrectProfile()
    {
        // Assert
        PatientProfileFactory.GetProfile("US").Should().Be(USCorePatientProfile.Instance);
        PatientProfileFactory.GetProfile("AU").Should().Be(AUBasePatientProfile.Instance);
        PatientProfileFactory.GetProfile("NL").Should().Be(DefaultPatientProfile.Instance);
        PatientProfileFactory.GetProfile(null).Should().Be(DefaultPatientProfile.Instance);
        PatientProfileFactory.GetProfile("").Should().Be(DefaultPatientProfile.Instance);
    }

    [Fact]
    public void GivenUSCoreProfile_WhenBuildingExtensions_ThenIncludesEthnicityAndHispanicOrigin()
    {
        // Arrange
        var attributes = new Dictionary<string, object>
        {
            [USCorePatientProfile.UsCoreRaceAttribute] = "White",
            [USCorePatientProfile.UsCoreEthnicityAttribute] = "Not Hispanic or Latino"
        };

        // Act
        var extensions = USCorePatientProfile.Instance.BuildExtensions(attributes, bmi: null).ToList();

        // Assert
        extensions.Should().HaveCount(2);

        // Ethnicity uses us-core-race extension URL per FHIR spec
        var ethnicityExtension = extensions.FirstOrDefault(e => e["url"]?.GetValue<string>() == "http://hl7.org/fhir/us/core/StructureDefinition/us-core-race");
        ethnicityExtension.Should().NotBeNull();

        // Hispanic origin uses us-core-ethnicity extension URL per FHIR spec
        var hispanicOriginExtension = extensions.FirstOrDefault(e => e["url"]?.GetValue<string>() == "http://hl7.org/fhir/us/core/StructureDefinition/us-core-ethnicity");
        hispanicOriginExtension.Should().NotBeNull();
    }

    [Fact]
    public void GivenAUBaseProfile_WhenBuildingExtensions_ThenIncludesIndigenousStatus()
    {
        // Arrange
        var attributes = new Dictionary<string, object>
        {
            [AUBasePatientProfile.IndigenousStatusAttribute] = "4"
        };

        // Act
        var extensions = AUBasePatientProfile.Instance.BuildExtensions(attributes, bmi: null).ToList();

        // Assert
        extensions.Should().HaveCount(1);

        var extension = extensions[0];
        extension["url"]?.GetValue<string>().Should().Be("http://hl7.org.au/fhir/StructureDefinition/indigenous-status");

        var valueCoding = extension["valueCoding"]?.AsObject();
        valueCoding.Should().NotBeNull();
        valueCoding?["code"]?.GetValue<string>().Should().Be("4");
        valueCoding?["display"]?.GetValue<string>().Should().Be("Neither Aboriginal nor Torres Strait Islander origin");
    }

    [Fact]
    public void GivenDefaultProfile_WhenBuildingExtensions_ThenOnlyIncludesBMI()
    {
        // Arrange
        var attributes = new Dictionary<string, object>();

        // Act - No BMI
        var extensionsWithoutBMI = DefaultPatientProfile.Instance.BuildExtensions(attributes, bmi: null).ToList();

        // Act - With BMI
        var extensionsWithBMI = DefaultPatientProfile.Instance.BuildExtensions(attributes, bmi: 25.5m).ToList();

        // Assert
        extensionsWithoutBMI.Should().BeEmpty();
        extensionsWithBMI.Should().HaveCount(1);
        extensionsWithBMI[0]["url"]?.GetValue<string>().Should().Be("http://ignixa.dev/StructureDefinition/patient-bmi");
        extensionsWithBMI[0]["valueDecimal"]?.GetValue<decimal>().Should().Be(25.5m);
    }

    #endregion

    #region State Abbreviation Tests

    [Fact]
    public void GivenSimpleBuilder_WhenBuildingWithFullStateName_ThenUsesFullStateName()
    {
        // Arrange & Act
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .WithAge(35)
            .WithCity("Boston")
            .WithState("Massachusetts")
            .WithZipCode("02101")
            .Build();

        // Assert
        var address = patient.MutableNode["address"]?.AsArray()?[0]?.AsObject();
        address?["state"]?.GetValue<string>().Should().Be("Massachusetts");
    }

    [Fact]
    public void GivenSimpleBuilder_WhenBuildingWithStateAbbreviation_ThenUsesAbbreviation()
    {
        // Arrange & Act
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .WithAge(35)
            .WithCity("Seattle")
            .WithState("WA")
            .WithZipCode("98101")
            .Build();

        // Assert
        var address = patient.MutableNode["address"]?.AsArray()?[0]?.AsObject();
        address?["state"]?.GetValue<string>().Should().Be("WA");
    }

    #endregion
}
