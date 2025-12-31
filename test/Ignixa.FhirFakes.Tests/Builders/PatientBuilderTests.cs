// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
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
        patient.ShouldNotBeNull();
        patient.ResourceType.ShouldBe("Patient");
        patient.MutableNode["gender"]?.GetValue<string>().ShouldBe("male");
        patient.MutableNode["birthDate"]?.GetValue<string>().ShouldNotBeNullOrEmpty();
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
        patient.ShouldNotBeNull();
        patient.MutableNode["gender"]?.GetValue<string>().ShouldBe("female");

        // Should have ethnicity extension
        patient.MutableNode["extension"].ShouldNotBeNull();
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
        patient.MutableNode["address"].ShouldNotBeNull();
        var addresses = patient.MutableNode["address"]?.AsArray();
        addresses!.Count.ShouldBe(1);

        var address = addresses?[0]?.AsObject();
        address?["city"]?.GetValue<string>().ShouldBe("Seattle");
        address?["state"]?.GetValue<string>().ShouldBe("WA");
        address?["postalCode"]?.GetValue<string>().ShouldBe("98101");
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
        patient.MutableNode["address"].ShouldNotBeNull();
        var addresses = patient.MutableNode["address"]?.AsArray();
        addresses!.Count.ShouldBe(1);

        var address = addresses?[0]?.AsObject();
        address?["postalCode"]?.GetValue<string>().ShouldBe("02101");
        address?["line"].ShouldNotBeNull(); // Street should be auto-generated
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
        patient.MutableNode["telecom"].ShouldNotBeNull();
        var telecoms = patient.MutableNode["telecom"]?.AsArray();
        telecoms!.Count.ShouldBe(1);

        var telecom = telecoms?[0]?.AsObject();
        telecom?["system"]?.GetValue<string>().ShouldBe("phone");
        telecom?["value"]?.GetValue<string>().ShouldStartWith("617-");
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
        patient.MutableNode["meta"]?["tag"].ShouldNotBeNull();
        var tags = patient.MutableNode["meta"]?["tag"]?.AsArray();
        tags!.Count.ShouldBe(1);

        var metaTag = tags?[0]?.AsObject();
        metaTag?["code"]?.GetValue<string>().ShouldBe(tag);
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
        patient.Id.ShouldBe(expectedId);
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
        patient.MutableNode["birthDate"]?.GetValue<string>().ShouldStartWith("1980");
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
        patient.MutableNode["active"]?.GetValue<bool>().ShouldBeFalse();
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
        patient.ShouldNotBeNull();
        patient.ResourceType.ShouldBe("Patient");
        patient.MutableNode["name"].ShouldNotBeNull();
        patient.MutableNode["gender"].ShouldNotBeNull();

        // Should have address with ZIP code from Boston demographics
        patient.MutableNode["address"].ShouldNotBeNull();
        var address = patient.MutableNode["address"]?.AsArray()?[0]?.AsObject();
        address?["postalCode"]?.GetValue<string>().ShouldStartWith("02"); // Boston ZIP prefix

        // Should have phone with area code from Boston demographics
        patient.MutableNode["telecom"].ShouldNotBeNull();
        var telecom = patient.MutableNode["telecom"]?.AsArray()?[0]?.AsObject();
        var phoneValue = telecom?["value"]?.GetValue<string>();
        (phoneValue!.StartsWith("617-", StringComparison.Ordinal) || phoneValue.StartsWith("857-", StringComparison.Ordinal)).ShouldBeTrue(); // Boston area codes
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
        birthDate.ShouldStartWith(expectedYear.ToString());
    }

    [Fact]
    public void GivenRealisticBuilder_WhenUsingCityStatePair_ThenGeneratesRealisticDemographics()
    {
        // Arrange & Act
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .FromCity(KnownCities.Chicago)
            .Build();

        // Assert
        patient.ShouldNotBeNull();
        patient.ResourceType.ShouldBe("Patient");

        // Should have address with ZIP code from Chicago demographics
        patient.MutableNode["address"].ShouldNotBeNull();
        var address = patient.MutableNode["address"]?.AsArray()?[0]?.AsObject();
        address?["postalCode"]?.GetValue<string>().ShouldStartWith("606"); // Chicago ZIP prefix

        // Should have phone with area code from Chicago demographics
        patient.MutableNode["telecom"].ShouldNotBeNull();
        var telecom = patient.MutableNode["telecom"]?.AsArray()?[0]?.AsObject();
        var phoneValue = telecom?["value"]?.GetValue<string>();
        (phoneValue!.StartsWith("312-", StringComparison.Ordinal) || phoneValue.StartsWith("773-", StringComparison.Ordinal) || phoneValue.StartsWith("872-", StringComparison.Ordinal)).ShouldBeTrue(); // Chicago area codes
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
        patient.MutableNode["name"].ShouldNotBeNull();
        var names = patient.MutableNode["name"]?.AsArray();
        names!.Count.ShouldBe(1);

        var name = names?[0]?.AsObject();
        name?["family"].ShouldNotBeNull();
        name?["given"].ShouldNotBeNull();

        // Should have US Core ethnicity extension (using us-core-race URL per FHIR spec)
        patient.MutableNode["extension"].ShouldNotBeNull();
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
        patient.MutableNode["extension"].ShouldNotBeNull();
        var extensions = patient.MutableNode["extension"]?.AsArray();

        // Find BMI extension
        var bmiExtension = extensions?
            .FirstOrDefault(e => e?["url"]?.GetValue<string>() == "http://ignixa.dev/StructureDefinition/patient-bmi");

        bmiExtension.ShouldNotBeNull();
        var bmi = bmiExtension?["valueDecimal"]?.GetValue<decimal>();
        bmi.ShouldNotBeNull();
        bmi.Value.ShouldBeGreaterThanOrEqualTo(19m); // NHANES range
        bmi.Value.ShouldBeLessThanOrEqualTo(42m);
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
        patient.ShouldNotBeNull();
        var address = patient.MutableNode["address"]?.AsArray()?[0]?.AsObject();
        address?["city"]?.GetValue<string>().ShouldBe("TestCity");
        address?["state"]?.GetValue<string>().ShouldBe("TestState");
        address?["postalCode"]?.GetValue<string>().ShouldStartWith("123");
    }

    [Fact]
    public void GivenRealisticBuilder_WhenUsingFromSeattle_ThenGeneratesSeattleDemographics()
    {
        // Arrange & Act
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .FromSeattle()
            .Build();

        // Assert
        patient.ShouldNotBeNull();
        patient.ResourceType.ShouldBe("Patient");

        // Should have address with Seattle details
        patient.MutableNode["address"].ShouldNotBeNull();
        var address = patient.MutableNode["address"]?.AsArray()?[0]?.AsObject();
        address?["city"]?.GetValue<string>().ShouldBe("Seattle");
        address?["state"]?.GetValue<string>().ShouldBe("Washington");

        // Should have name and demographics
        patient.MutableNode["name"].ShouldNotBeNull();
        patient.MutableNode["gender"].ShouldNotBeNull();
        patient.MutableNode["birthDate"].ShouldNotBeNull();
    }

    [Fact]
    public void GivenRealisticBuilder_WhenBuildingFromInternationalCity_ThenDoesNotIncludeUSCoreExtensions()
    {
        // Arrange & Act - Create patient from Melbourne, Australia
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .FromCity(KnownCities.Melbourne)
            .Build();

        // Assert
        patient.ShouldNotBeNull();
        patient.ResourceType.ShouldBe("Patient");

        // Should have Australian address details
        patient.MutableNode["address"].ShouldNotBeNull();
        var address = patient.MutableNode["address"]?.AsArray()?[0]?.AsObject();
        address?["city"]?.GetValue<string>().ShouldBe("Melbourne");
        address?["state"]?.GetValue<string>().ShouldBe("Victoria");
        address?["country"]?.GetValue<string>().ShouldBe("AU");

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
                url.ShouldNotBe(usCoreRaceUrl);
                url.ShouldNotBe(usCoreHispanicOriginUrl);
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
        patient.ShouldNotBeNull();
        patient.ResourceType.ShouldBe("Patient");

        // Should have US address
        var address = patient.MutableNode["address"]?.AsArray()?[0]?.AsObject();
        address?["country"]?.GetValue<string>().ShouldBe("US");

        // Should have USCore race extension (ethnicity is sampled from Boston demographics, uses us-core-race URL per FHIR spec)
        var extensions = patient.MutableNode["extension"]?.AsArray();
        extensions.ShouldNotBeNull();

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
        hasUsCoreRace.ShouldBeTrue("US patients should have USCore race extension (for ethnicity)");
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
        patient.ShouldNotBeNull();
        patient.ResourceType.ShouldBe("Patient");
        patient.Id.ShouldNotBeNullOrEmpty();
        patient.MutableNode["gender"].ShouldNotBeNull();
        patient.MutableNode["birthDate"].ShouldNotBeNull();
        patient.MutableNode["name"].ShouldNotBeNull();
        patient.MutableNode["active"]?.GetValue<bool>().ShouldBeTrue();
    }

    [Fact]
    public void GivenSimpleBuilder_WhenCalledFromCityWithoutDemographics_ThenThrowsInvalidOperationException()
    {
        // Arrange & Act - Use direct constructor to create simple builder WITHOUT dependencies
        var act = () => new PatientBuilder(_schemaProvider)
            .FromCity(KnownCities.Boston)
            .Build();

        // Assert
        Should.Throw<InvalidOperationException>(act).Message.ShouldContain("DemographicsDataProvider required");
    }

    [Fact]
    public void GivenSimpleBuilder_WhenCalledWithEthnicNameWithoutGenerator_ThenThrowsInvalidOperationException()
    {
        // Arrange & Act - Use direct constructor to create simple builder WITHOUT dependencies
        var act = () => new PatientBuilder(_schemaProvider)
            .WithName()
            .Build();

        // Assert
        Should.Throw<InvalidOperationException>(act).Message.ShouldContain("LocalBasedNameGenerator required");
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
        patient1.Id.ShouldNotBe(patient2.Id);
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
        builder.Profile.ShouldBe(USCorePatientProfile.Instance);
        builder.Profile.ProfileUrl.ShouldBe("http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient");
        builder.Profile.CountryCode.ShouldBe("US");
    }

    [Fact]
    public void GivenRealisticBuilder_WhenBuildingFromAUCity_ThenUsesAUBaseProfile()
    {
        // Arrange & Act
        var builder = PatientBuilderFactory.Create(_schemaProvider)
            .FromCity(KnownCities.Melbourne);

        // Assert - Check profile is AU Base
        builder.Profile.ShouldBe(AUBasePatientProfile.Instance);
        builder.Profile.ProfileUrl.ShouldBe("http://hl7.org.au/fhir/StructureDefinition/au-patient");
        builder.Profile.CountryCode.ShouldBe("AU");
    }

    [Fact]
    public void GivenRealisticBuilder_WhenBuildingFromAUCity_ThenIncludesIndigenousStatusExtension()
    {
        // Arrange & Act
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .FromCity(KnownCities.Melbourne)
            .Build();

        // Assert
        patient.ShouldNotBeNull();
        patient.ResourceType.ShouldBe("Patient");

        // Should have AU Base indigenous status extension
        var extensions = patient.MutableNode["extension"]?.AsArray();
        extensions.ShouldNotBeNull();

        var indigenousStatusUrl = "http://hl7.org.au/fhir/StructureDefinition/indigenous-status";
        var hasIndigenousStatus = false;
        foreach (var extension in extensions!)
        {
            if (extension?["url"]?.GetValue<string>() == indigenousStatusUrl)
            {
                hasIndigenousStatus = true;

                // Verify the coding structure
                var valueCoding = extension["valueCoding"]?.AsObject();
                valueCoding.ShouldNotBeNull();
                valueCoding?["system"]?.GetValue<string>().ShouldBe("https://healthterminologies.gov.au/fhir/CodeSystem/australian-indigenous-status-1");
                valueCoding?["code"]?.GetValue<string>().ShouldBeOneOf("1", "2", "3", "4", "9");
                valueCoding?["display"]?.GetValue<string>().ShouldNotBeNullOrEmpty();
                break;
            }
        }
        hasIndigenousStatus.ShouldBeTrue("AU patients should have indigenous status extension");
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
        address?["city"]?.GetValue<string>().ShouldBe("Sydney");
        address?["state"]?.GetValue<string>().ShouldBe("New South Wales");
        address?["country"]?.GetValue<string>().ShouldBe("AU");

        // Should have indigenous status extension
        var extensions = patient.MutableNode["extension"]?.AsArray();
        extensions.ShouldNotBeNull();
        extensions!.Any(e => e?["url"]?.GetValue<string>() == "http://hl7.org.au/fhir/StructureDefinition/indigenous-status")
            .ShouldBeTrue();
    }

    [Fact]
    public void GivenRealisticBuilder_WhenBuildingFromAmsterdam_ThenUsesDefaultProfile()
    {
        // Arrange & Act
        var builder = PatientBuilderFactory.Create(_schemaProvider)
            .FromCity(KnownCities.Amsterdam);

        // Assert - Check profile is Default (no NL profile implemented)
        builder.Profile.ShouldBe(DefaultPatientProfile.Instance);
        builder.Profile.ProfileUrl.ShouldBe("http://hl7.org/fhir/StructureDefinition/Patient");
    }

    [Fact]
    public void GivenRealisticBuilder_WhenBuildingFromAmsterdam_ThenNoCountrySpecificExtensions()
    {
        // Arrange & Act
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .FromCity(KnownCities.Amsterdam)
            .Build();

        // Assert
        patient.ShouldNotBeNull();

        // Should have NL address
        var address = patient.MutableNode["address"]?.AsArray()?[0]?.AsObject();
        address?["city"]?.GetValue<string>().ShouldBe("Amsterdam");
        address?["country"]?.GetValue<string>().ShouldBe("NL");

        // Extensions should be null or empty (no profile-specific extensions)
        var extensions = patient.MutableNode["extension"]?.AsArray();
        if (extensions != null)
        {
            // Should NOT have US Core or AU Base extensions
            foreach (var extension in extensions)
            {
                var url = extension?["url"]?.GetValue<string>();
                url!.ShouldNotContain("us-core");
                url!.ShouldNotContain("hl7.org.au");
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
        patient.ShouldNotBeNull();

        // Should have AU Base indigenous status extension
        var extensions = patient.MutableNode["extension"]?.AsArray();
        extensions.ShouldNotBeNull();

        var indigenousStatusUrl = "http://hl7.org.au/fhir/StructureDefinition/indigenous-status";
        extensions!.Any(e => e?["url"]?.GetValue<string>() == indigenousStatusUrl)
            .ShouldBeTrue();
    }

    [Fact]
    public void GivenSimpleBuilder_WhenUsingWithAttribute_ThenStoresAttribute()
    {
        // Arrange & Act
        var builder = PatientBuilderFactory.Create(_schemaProvider)
            .WithAttribute("customKey", "customValue")
            .WithAge(30);

        // Assert
        builder.ProfileAttributes.ShouldContainKey("customKey");
        builder.ProfileAttributes["customKey"].ShouldBe("customValue");
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
        builder.Profile.ShouldBe(USCorePatientProfile.Instance);
        builder.ProfileAttributes.ShouldContainKey(USCorePatientProfile.UsCoreRaceAttribute);
        builder.ProfileAttributes[USCorePatientProfile.UsCoreRaceAttribute].ShouldBe(USCorePatientProfile.Race.Hispanic);
    }

    [Fact]
    public void GivenPatientBuilder_WhenEthnicityAccessedViaProperty_ThenReturnsFromAttributes()
    {
        // Arrange & Act
        var builder = PatientBuilderFactory.Create(_schemaProvider)
            .FromCity(KnownCities.Boston);

        // Assert - Ethnicity should be accessible via ProfileAttributes
        builder.ProfileAttributes.ShouldContainKey(USCorePatientProfile.UsCoreRaceAttribute);
        builder.ProfileAttributes.ShouldContainKey(USCorePatientProfile.UsCoreRaceAttribute);
    }

    [Fact]
    public void GivenCityDemographics_WhenAccessingProfileUrl_ThenReturnsCorrectUrl()
    {
        // Assert
        KnownCities.Boston.ProfileUrl.ShouldBe("http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient");
        KnownCities.Melbourne.ProfileUrl.ShouldBe("http://hl7.org.au/fhir/StructureDefinition/au-patient");
        KnownCities.Amsterdam.ProfileUrl.ShouldBe("http://hl7.org/fhir/StructureDefinition/Patient");
    }

    [Fact]
    public void GivenPatientProfileFactory_WhenGettingProfileByCountry_ThenReturnsCorrectProfile()
    {
        // Assert
        PatientProfileFactory.GetProfile("US").ShouldBe(USCorePatientProfile.Instance);
        PatientProfileFactory.GetProfile("AU").ShouldBe(AUBasePatientProfile.Instance);
        PatientProfileFactory.GetProfile("NL").ShouldBe(DefaultPatientProfile.Instance);
        PatientProfileFactory.GetProfile(null).ShouldBe(DefaultPatientProfile.Instance);
        PatientProfileFactory.GetProfile("").ShouldBe(DefaultPatientProfile.Instance);
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
        extensions!.Count.ShouldBe(2);

        // Ethnicity uses us-core-race extension URL per FHIR spec
        var ethnicityExtension = extensions.FirstOrDefault(e => e["url"]?.GetValue<string>() == "http://hl7.org/fhir/us/core/StructureDefinition/us-core-race");
        ethnicityExtension.ShouldNotBeNull();

        // Hispanic origin uses us-core-ethnicity extension URL per FHIR spec
        var hispanicOriginExtension = extensions.FirstOrDefault(e => e["url"]?.GetValue<string>() == "http://hl7.org/fhir/us/core/StructureDefinition/us-core-ethnicity");
        hispanicOriginExtension.ShouldNotBeNull();
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
        extensions!.Count.ShouldBe(1);

        var extension = extensions[0];
        extension["url"]?.GetValue<string>().ShouldBe("http://hl7.org.au/fhir/StructureDefinition/indigenous-status");

        var valueCoding = extension["valueCoding"]?.AsObject();
        valueCoding.ShouldNotBeNull();
        valueCoding?["code"]?.GetValue<string>().ShouldBe("4");
        valueCoding?["display"]?.GetValue<string>().ShouldBe("Neither Aboriginal nor Torres Strait Islander origin");
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
        extensionsWithoutBMI.ShouldBeEmpty();
        extensionsWithBMI.Count.ShouldBe(1);
        extensionsWithBMI[0]["url"]?.GetValue<string>().ShouldBe("http://ignixa.dev/StructureDefinition/patient-bmi");
        extensionsWithBMI[0]["valueDecimal"]?.GetValue<decimal>().ShouldBe(25.5m);
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
        address?["state"]?.GetValue<string>().ShouldBe("Massachusetts");
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
        address?["state"]?.GetValue<string>().ShouldBe("WA");
    }

    #endregion

    #region Birthdate Precision Tests

    [Fact]
    public void GivenSimpleBuilder_WhenBuildingWithYearOnlyPrecision_ThenStoresYearOnlyString()
    {
        // Arrange & Act
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .WithBirthDate(1982)
            .WithGender("male")
            .Build();

        // Assert
        patient.MutableNode["birthDate"]?.GetValue<string>().ShouldBe("1982");
    }

    [Fact]
    public void GivenSimpleBuilder_WhenBuildingWithMonthOnlyPrecision_ThenStoresMonthOnlyString()
    {
        // Arrange & Act
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .WithBirthDate(1982, 1)
            .WithGender("female")
            .Build();

        // Assert
        patient.MutableNode["birthDate"]?.GetValue<string>().ShouldBe("1982-01");
    }

    [Fact]
    public void GivenSimpleBuilder_WhenBuildingWithFullDatePrecision_ThenStoresFullDateString()
    {
        // Arrange & Act
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .WithBirthDate(1982, 1, 15)
            .WithGender("male")
            .Build();

        // Assert
        patient.MutableNode["birthDate"]?.GetValue<string>().ShouldBe("1982-01-15");
    }

    [Fact]
    public void GivenSimpleBuilder_WhenBuildingWithVariousBirthdatePrecisions_ThenEachStoresCorrectPrecision()
    {
        // Test year-only
        var yearOnly = PatientBuilderFactory.Create(_schemaProvider)
            .WithBirthDate(1980)
            .Build();
        yearOnly.MutableNode["birthDate"]?.GetValue<string>().ShouldBe("1980");

        // Test month-only
        var monthOnly = PatientBuilderFactory.Create(_schemaProvider)
            .WithBirthDate(1985, 3)
            .Build();
        monthOnly.MutableNode["birthDate"]?.GetValue<string>().ShouldBe("1985-03");

        // Test full date
        var fullDate = PatientBuilderFactory.Create(_schemaProvider)
            .WithBirthDate(1990, 12, 25)
            .Build();
        fullDate.MutableNode["birthDate"]?.GetValue<string>().ShouldBe("1990-12-25");
    }

    [Theory]
    [InlineData(1899)] // Below minimum
    [InlineData(2101)] // Above maximum
    public void GivenSimpleBuilder_WhenBuildingWithInvalidYear_ThenThrowsArgumentOutOfRangeException(int year)
    {
        // Arrange & Act
        var act = () => PatientBuilderFactory.Create(_schemaProvider)
            .WithBirthDate(year)
            .Build();

        // Assert
        Should.Throw<ArgumentOutOfRangeException>(act).Message.ShouldContain("Year must be between 1900 and 2100");
    }

    [Theory]
    [InlineData(0)]  // Below minimum
    [InlineData(13)] // Above maximum
    public void GivenSimpleBuilder_WhenBuildingWithInvalidMonth_ThenThrowsArgumentOutOfRangeException(int month)
    {
        // Arrange & Act
        var act = () => PatientBuilderFactory.Create(_schemaProvider)
            .WithBirthDate(1982, month)
            .Build();

        // Assert
        Should.Throw<ArgumentOutOfRangeException>(act).Message.ShouldContain("Month must be between 1 and 12");
    }

    [Theory]
    [InlineData(1982, 2, 30)] // Feb 30 doesn't exist
    [InlineData(1982, 4, 31)] // April only has 30 days
    [InlineData(2023, 2, 29)] // 2023 is not a leap year
    public void GivenSimpleBuilder_WhenBuildingWithInvalidDay_ThenThrowsArgumentOutOfRangeException(int year, int month, int day)
    {
        // Arrange & Act
        var act = () => PatientBuilderFactory.Create(_schemaProvider)
            .WithBirthDate(year, month, day)
            .Build();

        // Assert
        var exception = Should.Throw<ArgumentOutOfRangeException>(act);
        exception.Message.ShouldContain($"Invalid day for {year}-{month:D2}");
    }

    [Fact]
    public void GivenSimpleBuilder_WhenBuildingWithLeapYearDate_ThenAcceptsFebruary29()
    {
        // Arrange & Act
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .WithBirthDate(2000, 2, 29) // 2000 is a leap year
            .WithGender("female")
            .Build();

        // Assert
        patient.MutableNode["birthDate"]?.GetValue<string>().ShouldBe("2000-02-29");
    }

    [Fact]
    public void GivenSimpleBuilder_WhenBuildingWithYearBoundaryDates_ThenStoresCorrectly()
    {
        // Test year boundaries
        var jan1 = PatientBuilderFactory.Create(_schemaProvider)
            .WithBirthDate(1982, 1, 1)
            .Build();
        jan1.MutableNode["birthDate"]?.GetValue<string>().ShouldBe("1982-01-01");

        var dec31 = PatientBuilderFactory.Create(_schemaProvider)
            .WithBirthDate(1982, 12, 31)
            .Build();
        dec31.MutableNode["birthDate"]?.GetValue<string>().ShouldBe("1982-12-31");
    }

    [Fact]
    public void GivenSimpleBuilder_WhenBuildingWithMonthBoundaryDates_ThenStoresCorrectly()
    {
        // Test month boundaries
        var monthStart = PatientBuilderFactory.Create(_schemaProvider)
            .WithBirthDate(1982, 5, 1)
            .Build();
        monthStart.MutableNode["birthDate"]?.GetValue<string>().ShouldBe("1982-05-01");

        var monthEnd = PatientBuilderFactory.Create(_schemaProvider)
            .WithBirthDate(1982, 5, 31)
            .Build();
        monthEnd.MutableNode["birthDate"]?.GetValue<string>().ShouldBe("1982-05-31");
    }

    #endregion

    // TODO: Field Omission Tests require implementation of WithoutActive, WithoutGender, WithoutAddress, WithoutTelecom methods
    // These tests were part of the branch but the methods are not implemented yet
    #region Field Omission Tests (:missing modifier support) - COMMENTED OUT - REQUIRES IMPLEMENTATION

    /*

    [Fact]
    public void GivenPatientWithoutActive_WhenBuilt_ThenActiveFieldOmitted()
    {
        // Arrange & Act
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .WithAge(30)
            .WithGender("male")
            .WithoutActive()
            .Build();

        // Assert
        patient.ShouldNotBeNull();
        patient.MutableNode.ContainsKey("active").ShouldBeFalse("active field should be omitted");
    }

    [Fact]
    public void GivenPatientWithActiveThenWithoutActive_WhenBuilt_ThenActiveFieldOmitted()
    {
        // Arrange & Act - WithoutActive should override WithActive
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .WithAge(30)
            .WithGender("male")
            .WithActive(true)
            .WithoutActive()  // Should override
            .Build();

        // Assert
        patient.MutableNode.ContainsKey("active").ShouldBeFalse("WithoutActive should override WithActive");
    }

    [Fact]
    public void GivenPatientWithoutActiveThenWithActive_WhenBuilt_ThenActiveFieldIncluded()
    {
        // Arrange & Act - WithActive should re-enable after WithoutActive
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .WithAge(30)
            .WithGender("male")
            .WithoutActive()
            .WithActive(false)  // Should re-enable
            .Build();

        // Assert
        patient.MutableNode.ContainsKey("active").ShouldBeTrue("WithActive should re-enable the field");
        patient.MutableNode["active"]?.GetValue<bool>().ShouldBeFalse();
    }

    [Fact]
    public void GivenPatientWithoutGender_WhenBuilt_ThenGenderFieldOmitted()
    {
        // Arrange & Act
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .WithAge(30)
            .WithoutGender()
            .Build();

        // Assert
        patient.ShouldNotBeNull();
        patient.MutableNode.ContainsKey("gender").ShouldBeFalse("gender field should be omitted");
    }

    [Fact]
    public void GivenPatientWithGenderThenWithoutGender_WhenBuilt_ThenGenderFieldOmitted()
    {
        // Arrange & Act
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .WithAge(30)
            .WithGender("female")
            .WithoutGender()  // Should override
            .Build();

        // Assert
        patient.MutableNode.ContainsKey("gender").ShouldBeFalse("WithoutGender should override WithGender");
    }

    [Fact]
    public void GivenPatientWithoutGenderThenWithGender_WhenBuilt_ThenGenderFieldIncluded()
    {
        // Arrange & Act
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .WithAge(30)
            .WithoutGender()
            .WithGender("male")  // Should re-enable
            .Build();

        // Assert
        patient.MutableNode.ContainsKey("gender").ShouldBeTrue("WithGender should re-enable the field");
        patient.MutableNode["gender"]?.GetValue<string>().ShouldBe("male");
    }

    [Fact]
    public void GivenPatientWithoutTelecom_WhenBuilt_ThenTelecomFieldOmitted()
    {
        // Arrange & Act
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .WithAge(30)
            .WithGender("male")
            .WithAreaCode("617")  // Would normally generate telecom
            .WithoutTelecom()     // But explicitly omit it
            .Build();

        // Assert
        patient.ShouldNotBeNull();
        patient.MutableNode.ContainsKey("telecom").ShouldBeFalse("telecom field should be omitted");
    }

    [Fact]
    public void GivenPatientWithoutTelecomThenWithAreaCode_WhenBuilt_ThenTelecomFieldIncluded()
    {
        // Arrange & Act
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .WithAge(30)
            .WithGender("male")
            .WithoutTelecom()
            .WithAreaCode("617")  // Should re-enable telecom
            .Build();

        // Assert
        patient.MutableNode.ContainsKey("telecom").ShouldBeTrue("WithAreaCode should re-enable telecom field");
        var telecoms = patient.MutableNode["telecom"]?.AsArray();
        telecoms.ShouldNotBeNull();
        telecoms!.Count.ShouldBe(1);
    }

    [Fact]
    public void GivenPatientWithoutAddress_WhenBuilt_ThenAddressFieldOmitted()
    {
        // Arrange & Act
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .WithAge(30)
            .WithGender("male")
            .WithZipCode("02101")  // Would normally generate address
            .WithoutAddress()      // But explicitly omit it
            .Build();

        // Assert
        patient.ShouldNotBeNull();
        patient.MutableNode.ContainsKey("address").ShouldBeFalse("address field should be omitted");
    }

    [Fact]
    public void GivenPatientWithoutAddressThenWithZipCode_WhenBuilt_ThenAddressFieldIncluded()
    {
        // Arrange & Act
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .WithAge(30)
            .WithGender("male")
            .WithoutAddress()
            .WithZipCode("02101")  // Should re-enable address
            .Build();

        // Assert
        patient.MutableNode.ContainsKey("address").ShouldBeTrue("WithZipCode should re-enable address field");
        var addresses = patient.MutableNode["address"]?.AsArray();
        addresses.ShouldNotBeNull();
        addresses!.Count.ShouldBe(1);
    }

    [Fact]
    public void GivenPatientWithoutAddressThenWithCity_WhenBuilt_ThenAddressFieldIncluded()
    {
        // Arrange & Act
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .WithAge(30)
            .WithGender("male")
            .WithoutAddress()
            .WithCity("Boston")  // Should re-enable address
            .Build();

        // Assert
        patient.MutableNode.ContainsKey("address").ShouldBeTrue("WithCity should re-enable address field");
        var addresses = patient.MutableNode["address"]?.AsArray();
        addresses.ShouldNotBeNull();
        addresses!.Count.ShouldBe(1);
        var address = addresses?[0]?.AsObject();
        address?["city"]?.GetValue<string>().ShouldBe("Boston");
    }

    [Fact]
    public void GivenPatientWithoutAddressThenWithState_WhenBuilt_ThenAddressFieldIncluded()
    {
        // Arrange & Act
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .WithAge(30)
            .WithGender("male")
            .WithoutAddress()
            .WithState("MA")  // Should re-enable address
            .Build();

        // Assert
        patient.MutableNode.ContainsKey("address").ShouldBeTrue("WithState should re-enable address field");
    }

    [Fact]
    public void GivenPatientWithoutAddressThenWithFullAddress_WhenBuilt_ThenAddressFieldIncluded()
    {
        // Arrange & Act
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .WithAge(30)
            .WithGender("male")
            .WithoutAddress()
            .WithAddress("123 Main St", "Boston", "MA", "02101")  // Should re-enable address
            .Build();

        // Assert
        patient.MutableNode.ContainsKey("address").ShouldBeTrue("WithAddress should re-enable address field");
        var addresses = patient.MutableNode["address"]?.AsArray();
        addresses.ShouldNotBeNull();
        addresses!.Count.ShouldBe(1);
        var address = addresses?[0]?.AsObject();
        address?["city"]?.GetValue<string>().ShouldBe("Boston");
        address?["postalCode"]?.GetValue<string>().ShouldBe("02101");
    }

    [Fact]
    public void GivenPatientWithMultipleFieldsOmitted_WhenBuilt_ThenAllOmittedFieldsAbsent()
    {
        // Arrange & Act
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .WithAge(30)
            .WithoutGender()
            .WithoutActive()
            .WithoutTelecom()
            .WithoutAddress()
            .Build();

        // Assert
        patient.ShouldNotBeNull();
        patient.MutableNode.ContainsKey("gender").ShouldBeFalse("gender should be omitted");
        patient.MutableNode.ContainsKey("active").ShouldBeFalse("active should be omitted");
        patient.MutableNode.ContainsKey("telecom").ShouldBeFalse("telecom should be omitted");
        patient.MutableNode.ContainsKey("address").ShouldBeFalse("address should be omitted");

        // Other fields should still be present
        patient.MutableNode.ContainsKey("id").ShouldBeTrue();
        patient.MutableNode.ContainsKey("birthDate").ShouldBeTrue();
        patient.MutableNode.ContainsKey("name").ShouldBeTrue();
    }

    [Fact]
    public void GivenPatientFromCityWithoutAddress_WhenBuilt_ThenAddressFieldOmitted()
    {
        // Arrange & Act - FromCity sets address fields, but WithoutAddress should override
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .FromCity(KnownCities.Boston)
            .WithoutAddress()
            .Build();

        // Assert
        patient.ShouldNotBeNull();
        patient.MutableNode.ContainsKey("address").ShouldBeFalse("address should be omitted even after FromCity");
    }

    [Fact]
    public void GivenPatientFromCityWithoutTelecom_WhenBuilt_ThenTelecomFieldOmitted()
    {
        // Arrange & Act - FromCity sets area code, but WithoutTelecom should override
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .FromCity(KnownCities.Boston)
            .WithoutTelecom()
            .Build();

        // Assert
        patient.ShouldNotBeNull();
        patient.MutableNode.ContainsKey("telecom").ShouldBeFalse("telecom should be omitted even after FromCity");
    }
    */

    [Fact]
    public void GivenPatientWithPractitionerGP_WhenBuilt_ThenIncludesPractitionerReference()
    {
        var practitionerId = Guid.NewGuid().ToString();
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .WithGivenName("John")
            .WithFamilyName("Doe")
            .WithGeneralPractitioner(practitionerId)
            .Build();

        patient.ShouldNotBeNull();
        var gpArray = patient.MutableNode["generalPractitioner"]?.AsArray();
        gpArray.ShouldNotBeNull();
        gpArray!.Count.ShouldBe(1);
        gpArray[0]?["reference"]?.GetValue<string>().ShouldBe($"Practitioner/{practitionerId}");
    }

    [Fact]
    public void GivenPatientWithOrganizationGP_WhenBuilt_ThenIncludesOrganizationReference()
    {
        var organizationId = Guid.NewGuid().ToString();
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .WithGivenName("Jane")
            .WithFamilyName("Smith")
            .WithGeneralPractitioner("Organization", organizationId)
            .Build();

        patient.ShouldNotBeNull();
        var gpArray = patient.MutableNode["generalPractitioner"]?.AsArray();
        gpArray.ShouldNotBeNull();
        gpArray!.Count.ShouldBe(1);
        gpArray[0]?["reference"]?.GetValue<string>().ShouldBe($"Organization/{organizationId}");
    }

    [Fact]
    public void GivenPatientWithMultipleGPs_WhenBuilt_ThenIncludesAllReferences()
    {
        var practitionerId = Guid.NewGuid().ToString();
        var organizationId = Guid.NewGuid().ToString();
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .WithGivenName("Alice")
            .WithFamilyName("Johnson")
            .WithGeneralPractitioner(practitionerId)
            .WithGeneralPractitioner("Organization", organizationId)
            .Build();

        patient.ShouldNotBeNull();
        var gpArray = patient.MutableNode["generalPractitioner"]?.AsArray();
        gpArray.ShouldNotBeNull();
        gpArray!.Count.ShouldBe(2);
        gpArray[0]?["reference"]?.GetValue<string>().ShouldBe($"Practitioner/{practitionerId}");
        gpArray[1]?["reference"]?.GetValue<string>().ShouldBe($"Organization/{organizationId}");
    }

    #endregion
}

