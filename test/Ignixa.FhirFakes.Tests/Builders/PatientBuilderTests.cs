// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.FhirFakes.Builders;
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
        var patient = PatientBuilderFactory.CreateSimple(_schemaProvider)
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
        var patient = PatientBuilderFactory.CreateSimple(_schemaProvider)
            .WithAge(32)
            .WithGender(g => g.Female)  // Selector makes options discoverable
            .WithRace(r => r.Hispanic)
            .Build();

        // Assert
        patient.Should().NotBeNull();
        patient.MutableNode["gender"]?.GetValue<string>().Should().Be("female");

        // Should have race extension
        patient.MutableNode["extension"].Should().NotBeNull();
    }

    [Fact]
    public void GivenSimpleBuilder_WhenBuildingWithAddress_ThenIncludesAddressInResource()
    {
        // Arrange & Act
        var patient = PatientBuilderFactory.CreateSimple(_schemaProvider)
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
        var patient = PatientBuilderFactory.CreateSimple(_schemaProvider)
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
        var patient = PatientBuilderFactory.CreateSimple(_schemaProvider)
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
        var patient = PatientBuilderFactory.CreateSimple(_schemaProvider)
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
        var patient = PatientBuilderFactory.CreateSimple(_schemaProvider)
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
        var patient = PatientBuilderFactory.CreateSimple(_schemaProvider)
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
        var patient = PatientBuilderFactory.CreateSimple(_schemaProvider)
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
        var patient = PatientBuilderFactory.CreateRealistic(_schemaProvider)
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
        var patient = PatientBuilderFactory.CreateRealistic(_schemaProvider)
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
        var patient = PatientBuilderFactory.CreateRealistic(_schemaProvider)
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
        var patient = PatientBuilderFactory.CreateRealistic(_schemaProvider)
            .WithRace(r => r.Hispanic)  // Selector shows all race options
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

        // Should have US Core race extension
        patient.MutableNode["extension"].Should().NotBeNull();
    }

    [Fact]
    public void GivenRealisticBuilder_WhenUsingWithRealisticBMI_ThenGeneratesBMIInRange()
    {
        // Arrange & Act
        var patient = PatientBuilderFactory.CreateRealistic(_schemaProvider)
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
            RaceDistribution: new Dictionary<string, double> { { "White", 1.0 } },
            AgeGroupDistribution: new Dictionary<string, double> { { "18-44", 1.0 } },
            MaleRatio: 0.5,
            ZipCodePrefix: "123",
            AreaCodes: ["555"]);

        var patient = PatientBuilderFactory.CreateRealistic(_schemaProvider)
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
        var patient = PatientBuilderFactory.CreateRealistic(_schemaProvider)
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

    #endregion

    #region Edge Cases

    [Fact]
    public void GivenSimpleBuilder_WhenNoParametersProvided_ThenBuildsWithDefaults()
    {
        // Arrange & Act
        var patient = PatientBuilderFactory.CreateSimple(_schemaProvider)
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
        // Arrange & Act
        var act = () => PatientBuilderFactory.CreateSimple(_schemaProvider)
            .FromCity(KnownCities.Boston)
            .Build();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*DemographicsDataProvider required*");
    }

    [Fact]
    public void GivenSimpleBuilder_WhenCalledWithEthnicNameWithoutGenerator_ThenThrowsInvalidOperationException()
    {
        // Arrange & Act
        var act = () => PatientBuilderFactory.CreateSimple(_schemaProvider)
            .WithName()
            .Build();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*EthnicNameGenerator required*");
    }

    [Fact]
    public void GivenRealisticBuilder_WhenBuildingMultiplePatients_ThenGeneratesDifferentPatients()
    {
        // Arrange & Act
        var patient1 = PatientBuilderFactory.CreateRealistic(_schemaProvider)
            .FromCity(KnownCities.Chicago)
            .Build();

        var patient2 = PatientBuilderFactory.CreateRealistic(_schemaProvider)
            .FromCity(KnownCities.Chicago)
            .Build();

        // Assert
        patient1.Id.Should().NotBe(patient2.Id);
        // Names may differ due to random sampling
    }

    #endregion

    #region State Abbreviation Tests

    [Fact]
    public void GivenSimpleBuilder_WhenBuildingWithFullStateName_ThenUsesFullStateName()
    {
        // Arrange & Act
        var patient = PatientBuilderFactory.CreateSimple(_schemaProvider)
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
        var patient = PatientBuilderFactory.CreateSimple(_schemaProvider)
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
