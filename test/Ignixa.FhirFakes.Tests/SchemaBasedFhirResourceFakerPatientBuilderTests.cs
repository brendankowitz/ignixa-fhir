// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
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
        patient.ShouldNotBeNull();
        patient.ResourceType.ShouldBe("Patient");
        patient.Id.ShouldNotBeNullOrEmpty();
        patient.MutableNode["gender"].ShouldNotBeNull();
        patient.MutableNode["birthDate"].ShouldNotBeNull();
        patient.MutableNode["name"].ShouldNotBeNull();
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
        patient.ShouldNotBeNull();
        patient.MutableNode["gender"]?.GetValue<string>().ShouldBe("male");

        var name = patient.MutableNode["name"]?.AsArray()?[0]?.AsObject();
        name?["family"]?.GetValue<string>().ShouldBe("Smith");
        name?["given"]?[0]?.GetValue<string>().ShouldBe("John");

        // Verify birth year is approximately 45 years ago
        var birthDate = patient.MutableNode["birthDate"]?.GetValue<string>();
        birthDate.ShouldStartWith((DateTime.UtcNow.Year - 45).ToString());
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
        patient.MutableNode["meta"]?["tag"].ShouldNotBeNull();
        var tagArray = patient.MutableNode["meta"]?["tag"]?.AsArray();
        tagArray!.Count.ShouldBe(1);

        var tag = tagArray?[0]?.AsObject();
        tag?["code"]?.GetValue<string>().ShouldBe(tagCode);
    }

    [Fact]
    public void GivenFaker_WhenCreatingSimplePatientWithAddress_ThenIncludesAddress()
    {
        // Act
        var patient = _faker.CreatePatient(p => p
            .WithAge(35)
            .WithAddress("123 Main St", "Boston", "MA", "02101"));

        // Assert
        patient.MutableNode["address"].ShouldNotBeNull();
        var address = patient.MutableNode["address"]?.AsArray()?[0]?.AsObject();
        address?["city"]?.GetValue<string>().ShouldBe("Boston");
        address?["state"]?.GetValue<string>().ShouldBe("MA");
        address?["postalCode"]?.GetValue<string>().ShouldBe("02101");
    }

    #endregion

    #region CreatePatient Tests

    [Fact]
    public void GivenFaker_WhenCreatingRealisticPatientWithNoConfiguration_ThenCreatesValidPatient()
    {
        // Act
        var patient = _faker.CreatePatient();

        // Assert
        patient.ShouldNotBeNull();
        patient.ResourceType.ShouldBe("Patient");
        patient.Id.ShouldNotBeNullOrEmpty();
        patient.MutableNode["gender"].ShouldNotBeNull();
        patient.MutableNode["birthDate"].ShouldNotBeNull();
        patient.MutableNode["name"].ShouldNotBeNull();
    }

    [Fact]
    public void GivenFaker_WhenCreatingRealisticPatientFromCity_ThenUsesRealDemographics()
    {
        // Act
        var patient = _faker.CreatePatient(p => p
            .FromCity(KnownCities.Boston));

        // Assert
        patient.ShouldNotBeNull();
        patient.ResourceType.ShouldBe("Patient");

        // Should have address with Boston ZIP code prefix
        patient.MutableNode["address"].ShouldNotBeNull();
        var address = patient.MutableNode["address"]?.AsArray()?[0]?.AsObject();
        address?["postalCode"]?.GetValue<string>().ShouldStartWith("02");
        address?["city"]?.GetValue<string>().ShouldBe("Boston");

        // Should have phone with Boston area code
        patient.MutableNode["telecom"].ShouldNotBeNull();
        var telecom = patient.MutableNode["telecom"]?.AsArray()?[0]?.AsObject();
        var phoneValue = telecom?["value"]?.GetValue<string>();
        phoneValue.ShouldNotBeNull();
        (phoneValue!.StartsWith("617-", StringComparison.Ordinal) || phoneValue.StartsWith("857-", StringComparison.Ordinal)).ShouldBeTrue();
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
        patient.MutableNode["meta"]?["tag"].ShouldNotBeNull();
        var tagArray = patient.MutableNode["meta"]?["tag"]?.AsArray();
        tagArray!.Count.ShouldBe(1);

        var tag = tagArray?[0]?.AsObject();
        tag?["code"]?.GetValue<string>().ShouldBe(tagCode);
    }

    [Fact]
    public void GivenFaker_WhenCreatingRealisticPatientWithBMI_ThenIncludesBMIExtension()
    {
        // Act
        var patient = _faker.CreatePatient(p => p
            .WithAge(40)
            .WithRealisticBMI());

        // Assert
        patient.MutableNode["extension"].ShouldNotBeNull();
        var extensions = patient.MutableNode["extension"]?.AsArray();

        // Find BMI extension
        var bmiExtension = extensions?
            .FirstOrDefault(e => e?["url"]?.GetValue<string>() == "http://ignixa.dev/StructureDefinition/patient-bmi");

        bmiExtension.ShouldNotBeNull();
        var bmi = bmiExtension?["valueDecimal"]?.GetValue<decimal>();
        bmi.ShouldNotBeNull();
        bmi.Value.ShouldBeGreaterThanOrEqualTo(19m);
        bmi.Value.ShouldBeLessThanOrEqualTo(42m);
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
        patient.MutableNode["extension"].ShouldNotBeNull();
        var extensions = patient.MutableNode["extension"]?.AsArray();

        // Find ethnicity extension (using us-core-race URL per FHIR spec)
        var ethnicityExtension = extensions?
            .FirstOrDefault(e => e?["url"]?.GetValue<string>() == "http://hl7.org/fhir/us/core/StructureDefinition/us-core-race");

        ethnicityExtension.ShouldNotBeNull();
    }

    #endregion

    #region CreateSeattlePatient Tests

    [Fact]
    public void GivenFaker_WhenCreatingSeattlePatientWithNoConfiguration_ThenCreatesSeattlePatient()
    {
        // Act
        var patient = _faker.CreateSeattlePatient();

        // Assert
        patient.ShouldNotBeNull();
        patient.ResourceType.ShouldBe("Patient");

        // Should have Seattle address
        patient.MutableNode["address"].ShouldNotBeNull();
        var address = patient.MutableNode["address"]?.AsArray()?[0]?.AsObject();
        address?["city"]?.GetValue<string>().ShouldBe("Seattle");
        address?["state"]?.GetValue<string>().ShouldBe("Washington");
    }

    [Fact]
    public void GivenFaker_WhenCreatingSeattlePatientWithAgeOverride_ThenUsesOverriddenAge()
    {
        // Act
        var patient = _faker.CreateSeattlePatient(p => p.WithAge(35));

        // Assert
        var birthDate = patient.MutableNode["birthDate"]?.GetValue<string>();
        var expectedYear = DateTime.UtcNow.Year - 35;
        birthDate.ShouldStartWith(expectedYear.ToString());
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
        patient.MutableNode["meta"]?["tag"].ShouldNotBeNull();
        var tagArray = patient.MutableNode["meta"]?["tag"]?.AsArray();
        tagArray!.Count.ShouldBe(1);

        var tag = tagArray?[0]?.AsObject();
        tag?["code"]?.GetValue<string>().ShouldBe(tagCode);
    }

    [Fact]
    public void GivenFaker_WhenCreatingSeattlePatientWithBMI_ThenIncludesBMIExtension()
    {
        // Act
        var patient = _faker.CreateSeattlePatient(p => p.WithRealisticBMI());

        // Assert
        patient.MutableNode["extension"].ShouldNotBeNull();
        var extensions = patient.MutableNode["extension"]?.AsArray();

        // Find BMI extension
        var bmiExtension = extensions?
            .FirstOrDefault(e => e?["url"]?.GetValue<string>() == "http://ignixa.dev/StructureDefinition/patient-bmi");

        bmiExtension.ShouldNotBeNull();
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
        patient1.MutableNode["meta"]?["tag"]?[0]?["code"]?.GetValue<string>().ShouldBe(tag1);
        patient2.MutableNode["meta"]?["tag"]?[0]?["code"]?.GetValue<string>().ShouldBe(tag2);
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
        patient1.MutableNode["meta"]?["tag"].ShouldNotBeNull();
        patient2.MutableNode["meta"]?["tag"].ShouldBeNull();
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
        ids.Distinct().Count().ShouldBe(3);

        // All should be valid patients
        simplePatient.ResourceType.ShouldBe("Patient");
        realisticPatient.ResourceType.ShouldBe("Patient");
        seattlePatient.ResourceType.ShouldBe("Patient");
    }

    #endregion
}
