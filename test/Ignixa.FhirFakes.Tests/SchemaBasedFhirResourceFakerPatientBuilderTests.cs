// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.FhirFakes.Builders;
using Ignixa.FhirFakes.Builders.Profiles;
using Ignixa.FhirFakes.Population;
using Ignixa.Specification.Generated;

namespace Ignixa.FhirFakes.Tests;

/// <summary>
/// Tests for PatientBuilder convenience methods in SchemaBasedFhirResourceFaker.
/// Verifies CreatePatient, CreatePatient, and CreateSeattlePatient.
/// </summary>
public class SchemaBasedFhirResourceFakerPatientBuilderTests
{
    private readonly R4CoreSchemaProvider _schemaProvider = new();
    private readonly SchemaBasedFhirResourceFaker _faker;

    public SchemaBasedFhirResourceFakerPatientBuilderTests()
    {
        _faker = new SchemaBasedFhirResourceFaker(_schemaProvider);
    }

    #region CreatePatient Tests

    [Fact]
    public void GivenFaker_WhenCreatingSimplePatientWithNoConfiguration_ThenCreatesValidPatient()
    {
        // Act
        var patient = _faker.CreatePatient();

        // Assert
        patient.Should().NotBeNull();
        patient.ResourceType.Should().Be("Patient");
        patient.Id.Should().NotBeNullOrEmpty();
        patient.MutableNode["gender"].Should().NotBeNull();
        patient.MutableNode["birthDate"].Should().NotBeNull();
        patient.MutableNode["name"].Should().NotBeNull();
    }

    [Fact]
    public void GivenFaker_WhenCreatingSimplePatientWithConfiguration_ThenAppliesConfiguration()
    {
        // Act
        var patient = _faker.CreatePatient(p => p
            .WithAge(45)
            .WithGender(g => g.Male)
            .WithGivenName("John")
            .WithFamilyName("Smith"));

        // Assert
        patient.Should().NotBeNull();
        patient.MutableNode["gender"]?.GetValue<string>().Should().Be("male");

        var name = patient.MutableNode["name"]?.AsArray()?[0]?.AsObject();
        name?["family"]?.GetValue<string>().Should().Be("Smith");
        name?["given"]?[0]?.GetValue<string>().Should().Be("John");

        // Verify birth year is approximately 45 years ago
        var birthDate = patient.MutableNode["birthDate"]?.GetValue<string>();
        birthDate.Should().StartWith((DateTime.UtcNow.Year - 45).ToString());
    }

    [Fact]
    public void GivenFakerWithTag_WhenCreatingSimplePatient_ThenTagIsApplied()
    {
        // Arrange
        var tagCode = Guid.NewGuid().ToString();
        _faker.WithTag(tagCode);

        // Act
        var patient = _faker.CreatePatient(p => p.WithAge(30));

        // Assert
        patient.MutableNode["meta"]?["tag"].Should().NotBeNull();
        var tagArray = patient.MutableNode["meta"]?["tag"]?.AsArray();
        tagArray.Should().HaveCount(1);

        var tag = tagArray?[0]?.AsObject();
        tag?["code"]?.GetValue<string>().Should().Be(tagCode);
    }

    [Fact]
    public void GivenFaker_WhenCreatingSimplePatientWithAddress_ThenIncludesAddress()
    {
        // Act
        var patient = _faker.CreatePatient(p => p
            .WithAge(35)
            .WithAddress("123 Main St", "Boston", "MA", "02101"));

        // Assert
        patient.MutableNode["address"].Should().NotBeNull();
        var address = patient.MutableNode["address"]?.AsArray()?[0]?.AsObject();
        address?["city"]?.GetValue<string>().Should().Be("Boston");
        address?["state"]?.GetValue<string>().Should().Be("MA");
        address?["postalCode"]?.GetValue<string>().Should().Be("02101");
    }

    #endregion

    #region CreatePatient Tests

    [Fact]
    public void GivenFaker_WhenCreatingRealisticPatientWithNoConfiguration_ThenCreatesValidPatient()
    {
        // Act
        var patient = _faker.CreatePatient();

        // Assert
        patient.Should().NotBeNull();
        patient.ResourceType.Should().Be("Patient");
        patient.Id.Should().NotBeNullOrEmpty();
        patient.MutableNode["gender"].Should().NotBeNull();
        patient.MutableNode["birthDate"].Should().NotBeNull();
        patient.MutableNode["name"].Should().NotBeNull();
    }

    [Fact]
    public void GivenFaker_WhenCreatingRealisticPatientFromCity_ThenUsesRealDemographics()
    {
        // Act
        var patient = _faker.CreatePatient(p => p
            .FromCity(KnownCities.Boston));

        // Assert
        patient.Should().NotBeNull();
        patient.ResourceType.Should().Be("Patient");

        // Should have address with Boston ZIP code prefix
        patient.MutableNode["address"].Should().NotBeNull();
        var address = patient.MutableNode["address"]?.AsArray()?[0]?.AsObject();
        address?["postalCode"]?.GetValue<string>().Should().StartWith("02");
        address?["city"]?.GetValue<string>().Should().Be("Boston");

        // Should have phone with Boston area code
        patient.MutableNode["telecom"].Should().NotBeNull();
        var telecom = patient.MutableNode["telecom"]?.AsArray()?[0]?.AsObject();
        var phoneValue = telecom?["value"]?.GetValue<string>();
        phoneValue.Should().Match(p => p.StartsWith("617-") || p.StartsWith("857-"));
    }

    [Fact]
    public void GivenFakerWithTag_WhenCreatingRealisticPatient_ThenTagIsApplied()
    {
        // Arrange
        var tagCode = Guid.NewGuid().ToString();
        _faker.WithTag(tagCode);

        // Act
        var patient = _faker.CreatePatient(p => p
            .FromCity(KnownCities.Chicago));

        // Assert
        patient.MutableNode["meta"]?["tag"].Should().NotBeNull();
        var tagArray = patient.MutableNode["meta"]?["tag"]?.AsArray();
        tagArray.Should().HaveCount(1);

        var tag = tagArray?[0]?.AsObject();
        tag?["code"]?.GetValue<string>().Should().Be(tagCode);
    }

    [Fact]
    public void GivenFaker_WhenCreatingRealisticPatientWithBMI_ThenIncludesBMIExtension()
    {
        // Act
        var patient = _faker.CreatePatient(p => p
            .WithAge(40)
            .WithRealisticBMI());

        // Assert
        patient.MutableNode["extension"].Should().NotBeNull();
        var extensions = patient.MutableNode["extension"]?.AsArray();

        // Find BMI extension
        var bmiExtension = extensions?
            .FirstOrDefault(e => e?["url"]?.GetValue<string>() == "http://ignixa.dev/StructureDefinition/patient-bmi");

        bmiExtension.Should().NotBeNull();
        var bmi = bmiExtension?["valueDecimal"]?.GetValue<decimal>();
        bmi.Should().BeGreaterOrEqualTo(19).And.BeLessOrEqualTo(42);
    }

    [Fact]
    public void GivenFaker_WhenCreatingRealisticPatientWithEthnicityAttribute_ThenSetsEthnicityExtension()
    {
        // Act
        var patient = _faker.CreatePatient(p => p
            .WithProfile(USCorePatientProfile.Instance)
            .WithAttribute(USCorePatientProfile.UsCoreRaceAttribute, USCorePatientProfile.Race.Hispanic)
            .WithGender(g => g.Female)
            .WithAge(30));

        // Assert
        patient.MutableNode["extension"].Should().NotBeNull();
        var extensions = patient.MutableNode["extension"]?.AsArray();

        // Find ethnicity extension (using us-core-race URL per FHIR spec)
        var ethnicityExtension = extensions?
            .FirstOrDefault(e => e?["url"]?.GetValue<string>() == "http://hl7.org/fhir/us/core/StructureDefinition/us-core-race");

        ethnicityExtension.Should().NotBeNull();
    }

    #endregion

    #region CreateSeattlePatient Tests

    [Fact]
    public void GivenFaker_WhenCreatingSeattlePatientWithNoConfiguration_ThenCreatesSeattlePatient()
    {
        // Act
        var patient = _faker.CreateSeattlePatient();

        // Assert
        patient.Should().NotBeNull();
        patient.ResourceType.Should().Be("Patient");

        // Should have Seattle address
        patient.MutableNode["address"].Should().NotBeNull();
        var address = patient.MutableNode["address"]?.AsArray()?[0]?.AsObject();
        address?["city"]?.GetValue<string>().Should().Be("Seattle");
        address?["state"]?.GetValue<string>().Should().Be("Washington");
    }

    [Fact]
    public void GivenFaker_WhenCreatingSeattlePatientWithAgeOverride_ThenUsesOverriddenAge()
    {
        // Act
        var patient = _faker.CreateSeattlePatient(p => p.WithAge(35));

        // Assert
        var birthDate = patient.MutableNode["birthDate"]?.GetValue<string>();
        var expectedYear = DateTime.UtcNow.Year - 35;
        birthDate.Should().StartWith(expectedYear.ToString());
    }

    [Fact]
    public void GivenFakerWithTag_WhenCreatingSeattlePatient_ThenTagIsApplied()
    {
        // Arrange
        var tagCode = Guid.NewGuid().ToString();
        _faker.WithTag(tagCode);

        // Act
        var patient = _faker.CreateSeattlePatient();

        // Assert
        patient.MutableNode["meta"]?["tag"].Should().NotBeNull();
        var tagArray = patient.MutableNode["meta"]?["tag"]?.AsArray();
        tagArray.Should().HaveCount(1);

        var tag = tagArray?[0]?.AsObject();
        tag?["code"]?.GetValue<string>().Should().Be(tagCode);
    }

    [Fact]
    public void GivenFaker_WhenCreatingSeattlePatientWithBMI_ThenIncludesBMIExtension()
    {
        // Act
        var patient = _faker.CreateSeattlePatient(p => p.WithRealisticBMI());

        // Assert
        patient.MutableNode["extension"].Should().NotBeNull();
        var extensions = patient.MutableNode["extension"]?.AsArray();

        // Find BMI extension
        var bmiExtension = extensions?
            .FirstOrDefault(e => e?["url"]?.GetValue<string>() == "http://ignixa.dev/StructureDefinition/patient-bmi");

        bmiExtension.Should().NotBeNull();
    }

    #endregion

    #region Tag Integration Tests

    [Fact]
    public void GivenFakerWithTag_WhenChangingTagBetweenPatientCreations_ThenNewPatientsGetNewTag()
    {
        // Arrange
        var tag1 = "test-tag-1";
        var tag2 = "test-tag-2";

        // Act
        _faker.WithTag(tag1);
        var patient1 = _faker.CreatePatient();

        _faker.WithTag(tag2);
        var patient2 = _faker.CreatePatient();

        // Assert
        patient1.MutableNode["meta"]?["tag"]?[0]?["code"]?.GetValue<string>().Should().Be(tag1);
        patient2.MutableNode["meta"]?["tag"]?[0]?["code"]?.GetValue<string>().Should().Be(tag2);
    }

    [Fact]
    public void GivenFakerWithTag_WhenClearingTag_ThenNewPatientsHaveNoTag()
    {
        // Arrange
        var tagCode = Guid.NewGuid().ToString();
        _faker.WithTag(tagCode);
        var patient1 = _faker.CreatePatient();

        // Act - Clear tag
        _faker.WithTag(null);
        var patient2 = _faker.CreatePatient();

        // Assert
        patient1.MutableNode["meta"]?["tag"].Should().NotBeNull();
        patient2.MutableNode["meta"]?["tag"].Should().BeNull();
    }

    [Fact]
    public void GivenFaker_WhenCreatingMultiplePatientTypes_ThenAllAreDistinct()
    {
        // Act
        var simplePatient = _faker.CreatePatient();
        var realisticPatient = _faker.CreatePatient();
        var seattlePatient = _faker.CreateSeattlePatient();

        // Assert - All patients should have different IDs
        var ids = new[] { simplePatient.Id, realisticPatient.Id, seattlePatient.Id };
        ids.Distinct().Should().HaveCount(3);

        // All should be valid patients
        simplePatient.ResourceType.Should().Be("Patient");
        realisticPatient.ResourceType.Should().Be("Patient");
        seattlePatient.ResourceType.Should().Be("Patient");
    }

    #endregion
}
