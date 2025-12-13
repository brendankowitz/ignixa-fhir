// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.FhirFakes.Builders;
using Ignixa.Specification;
using Ignixa.Specification.Generated;
using Xunit;

namespace Ignixa.FhirFakes.Tests.Builders;

/// <summary>
/// Unit tests for PractitionerBuilder.
/// Tests basic practitioner generation with names, identifiers, and specialties.
/// </summary>
public class PractitionerBuilderTests
{
    private readonly IFhirSchemaProvider _schemaProvider = new R4CoreSchemaProvider();

    #region Basic Building Tests

    [Fact]
    public void GivenPractitionerBuilder_WhenBuildingWithName_ThenCreatesPractitioner()
    {
        // Arrange & Act
        var practitioner = PractitionerBuilder.Create(_schemaProvider)
            .WithName("Alice", "Anderson")
            .Build();

        // Assert
        practitioner.Should().NotBeNull();
        practitioner.ResourceType.Should().Be("Practitioner");
        practitioner.MutableNode["active"]?.GetValue<bool>().Should().BeTrue();

        var nameArray = practitioner.MutableNode["name"]?.AsArray();
        nameArray.Should().NotBeNull();
        nameArray.Should().HaveCount(1);

        var name = nameArray?[0]?.AsObject();
        name?["use"]?.GetValue<string>().Should().Be("official");
        name?["given"]?.AsArray()?[0]?.GetValue<string>().Should().Be("Alice");
        name?["family"]?.GetValue<string>().Should().Be("Anderson");
    }

    [Fact]
    public void GivenPractitionerBuilder_WhenBuildingWithId_ThenUsesProvidedId()
    {
        // Arrange
        var expectedId = "practitioner-123";

        // Act
        var practitioner = PractitionerBuilder.Create(_schemaProvider)
            .WithId(expectedId)
            .WithName("John", "Doe")
            .Build();

        // Assert
        practitioner.Id.Should().Be(expectedId);
    }

    [Fact]
    public void GivenPractitionerBuilder_WhenBuildingWithTag_ThenIncludesTagInMeta()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Act
        var practitioner = PractitionerBuilder.Create(_schemaProvider)
            .WithName("Jane", "Smith")
            .WithTag(tag)
            .Build();

        // Assert
        practitioner.MutableNode["meta"]?["tag"].Should().NotBeNull();
        var tags = practitioner.MutableNode["meta"]?["tag"]?.AsArray();
        tags.Should().HaveCount(1);

        var metaTag = tags?[0]?.AsObject();
        metaTag?["code"]?.GetValue<string>().Should().Be(tag);
        metaTag?["system"]?.GetValue<string>().Should().Be("http://ignixa.dev/test-isolation");
    }

    [Fact]
    public void GivenPractitionerBuilder_WhenNoParametersProvided_ThenBuildsWithDefaults()
    {
        // Arrange & Act
        var practitioner = PractitionerBuilder.Create(_schemaProvider)
            .Build();

        // Assert
        practitioner.Should().NotBeNull();
        practitioner.ResourceType.Should().Be("Practitioner");
        practitioner.Id.Should().NotBeNullOrEmpty();
        practitioner.MutableNode["active"]?.GetValue<bool>().Should().BeTrue();
    }

    #endregion

    #region Name Tests

    [Fact]
    public void GivenPractitionerBuilder_WhenBuildingWithFamilyNameOnly_ThenIncludesFamilyName()
    {
        // Arrange & Act
        var practitioner = PractitionerBuilder.Create(_schemaProvider)
            .WithFamilyName("Johnson")
            .Build();

        // Assert
        var nameArray = practitioner.MutableNode["name"]?.AsArray();
        nameArray.Should().HaveCount(1);

        var name = nameArray?[0]?.AsObject();
        name?["family"]?.GetValue<string>().Should().Be("Johnson");
        name?.TryGetPropertyValue("given", out _).Should().BeFalse();
    }

    [Fact]
    public void GivenPractitionerBuilder_WhenBuildingWithGivenNameOnly_ThenIncludesGivenName()
    {
        // Arrange & Act
        var practitioner = PractitionerBuilder.Create(_schemaProvider)
            .WithGivenName("Michael")
            .Build();

        // Assert
        var nameArray = practitioner.MutableNode["name"]?.AsArray();
        nameArray.Should().HaveCount(1);

        var name = nameArray?[0]?.AsObject();
        name?["given"]?.AsArray()?[0]?.GetValue<string>().Should().Be("Michael");
        name?.TryGetPropertyValue("family", out _).Should().BeFalse();
    }

    [Fact]
    public void GivenPractitionerBuilder_WhenBuildingWithBothNameComponents_ThenIncludesBoth()
    {
        // Arrange & Act
        var practitioner = PractitionerBuilder.Create(_schemaProvider)
            .WithGivenName("Robert")
            .WithFamilyName("Williams")
            .Build();

        // Assert
        var nameArray = practitioner.MutableNode["name"]?.AsArray();
        nameArray.Should().HaveCount(1);

        var name = nameArray?[0]?.AsObject();
        name?["given"]?.AsArray()?[0]?.GetValue<string>().Should().Be("Robert");
        name?["family"]?.GetValue<string>().Should().Be("Williams");
    }

    [Fact]
    public void GivenPractitionerBuilder_WhenBuildingWithoutName_ThenDoesNotIncludeName()
    {
        // Arrange & Act
        var practitioner = PractitionerBuilder.Create(_schemaProvider)
            .Build();

        // Assert
        practitioner.MutableNode.TryGetPropertyValue("name", out _).Should().BeFalse();
    }

    #endregion

    #region Identifier Tests

    [Fact]
    public void GivenPractitionerBuilder_WhenBuildingWithNpi_ThenIncludesNpiIdentifier()
    {
        // Arrange
        var npi = "1234567890";

        // Act
        var practitioner = PractitionerBuilder.Create(_schemaProvider)
            .WithName("Sarah", "Davis")
            .WithNpi(npi)
            .Build();

        // Assert
        var identifiers = practitioner.MutableNode["identifier"]?.AsArray();
        identifiers.Should().NotBeNull();
        identifiers.Should().HaveCount(1);

        var identifier = identifiers?[0]?.AsObject();
        identifier?["system"]?.GetValue<string>().Should().Be("http://hl7.org/fhir/sid/us-npi");
        identifier?["value"]?.GetValue<string>().Should().Be(npi);
    }

    [Fact]
    public void GivenPractitionerBuilder_WhenBuildingWithCustomIdentifier_ThenIncludesIdentifier()
    {
        // Arrange
        var identifierValue = "EMP-12345";
        var identifierSystem = "http://hospital.example.org/staff-id";

        // Act
        var practitioner = PractitionerBuilder.Create(_schemaProvider)
            .WithName("Emily", "Brown")
            .WithIdentifier(identifierValue, identifierSystem)
            .Build();

        // Assert
        var identifiers = practitioner.MutableNode["identifier"]?.AsArray();
        identifiers.Should().HaveCount(1);

        var identifier = identifiers?[0]?.AsObject();
        identifier?["system"]?.GetValue<string>().Should().Be(identifierSystem);
        identifier?["value"]?.GetValue<string>().Should().Be(identifierValue);
    }

    [Fact]
    public void GivenPractitionerBuilder_WhenBuildingWithIdentifierWithoutSystem_ThenIncludesIdentifierWithoutSystem()
    {
        // Arrange
        var identifierValue = "STAFF-789";

        // Act
        var practitioner = PractitionerBuilder.Create(_schemaProvider)
            .WithIdentifier(identifierValue)
            .Build();

        // Assert
        var identifiers = practitioner.MutableNode["identifier"]?.AsArray();
        identifiers.Should().HaveCount(1);

        var identifier = identifiers?[0]?.AsObject();
        identifier?["value"]?.GetValue<string>().Should().Be(identifierValue);
        identifier?.TryGetPropertyValue("system", out _).Should().BeFalse();
    }

    [Fact]
    public void GivenPractitionerBuilder_WhenBuildingWithMultipleIdentifiers_ThenIncludesAllIdentifiers()
    {
        // Arrange & Act
        var practitioner = PractitionerBuilder.Create(_schemaProvider)
            .WithNpi("1234567890")
            .WithIdentifier("EMP-999", "http://hospital.example.org/staff-id")
            .WithIdentifier("LIC-ABC123", "http://state.example.org/medical-license")
            .Build();

        // Assert
        var identifiers = practitioner.MutableNode["identifier"]?.AsArray();
        identifiers.Should().HaveCount(3);

        // Check NPI
        var npiIdentifier = identifiers?[0]?.AsObject();
        npiIdentifier?["system"]?.GetValue<string>().Should().Be("http://hl7.org/fhir/sid/us-npi");
        npiIdentifier?["value"]?.GetValue<string>().Should().Be("1234567890");

        // Check staff ID
        var staffIdentifier = identifiers?[1]?.AsObject();
        staffIdentifier?["system"]?.GetValue<string>().Should().Be("http://hospital.example.org/staff-id");
        staffIdentifier?["value"]?.GetValue<string>().Should().Be("EMP-999");

        // Check license
        var licenseIdentifier = identifiers?[2]?.AsObject();
        licenseIdentifier?["system"]?.GetValue<string>().Should().Be("http://state.example.org/medical-license");
        licenseIdentifier?["value"]?.GetValue<string>().Should().Be("LIC-ABC123");
    }

    #endregion

    #region Specialty Tests

    [Fact]
    public void GivenPractitionerBuilder_WhenBuildingWithSpecialty_ThenIncludesQualification()
    {
        // Arrange
        var specialtyCode = "207Q00000X";
        var specialtyDisplay = "Family Medicine";

        // Act
        var practitioner = PractitionerBuilder.Create(_schemaProvider)
            .WithName("David", "Wilson")
            .WithSpecialty(specialtyCode, display: specialtyDisplay)
            .Build();

        // Assert
        var qualifications = practitioner.MutableNode["qualification"]?.AsArray();
        qualifications.Should().NotBeNull();
        qualifications.Should().HaveCount(1);

        var qualification = qualifications?[0]?.AsObject();
        var code = qualification?["code"]?.AsObject();
        var coding = code?["coding"]?.AsArray();

        coding.Should().HaveCount(1);
        var codingObj = coding?[0]?.AsObject();
        codingObj?["system"]?.GetValue<string>().Should().Be("http://snomed.info/sct");
        codingObj?["code"]?.GetValue<string>().Should().Be(specialtyCode);
        codingObj?["display"]?.GetValue<string>().Should().Be(specialtyDisplay);
    }

    [Fact]
    public void GivenPractitionerBuilder_WhenBuildingWithSpecialtyCustomSystem_ThenUsesProvidedSystem()
    {
        // Arrange
        var specialtyCode = "207Q00000X";
        var customSystem = "http://nucc.org/provider-taxonomy";

        // Act
        var practitioner = PractitionerBuilder.Create(_schemaProvider)
            .WithSpecialty(specialtyCode, customSystem)
            .Build();

        // Assert
        var qualifications = practitioner.MutableNode["qualification"]?.AsArray();
        var qualification = qualifications?[0]?.AsObject();
        var code = qualification?["code"]?.AsObject();
        var coding = code?["coding"]?.AsArray();
        var codingObj = coding?[0]?.AsObject();

        codingObj?["system"]?.GetValue<string>().Should().Be(customSystem);
    }

    [Fact]
    public void GivenPractitionerBuilder_WhenBuildingWithMultipleSpecialties_ThenIncludesAllQualifications()
    {
        // Arrange & Act
        var practitioner = PractitionerBuilder.Create(_schemaProvider)
            .WithName("Patricia", "Martinez")
            .WithSpecialty("207Q00000X", display: "Family Medicine")
            .WithSpecialty("419192003", display: "Internal Medicine")
            .Build();

        // Assert
        var qualifications = practitioner.MutableNode["qualification"]?.AsArray();
        qualifications.Should().HaveCount(2);

        var firstQual = qualifications?[0]?.AsObject();
        var firstCoding = firstQual?["code"]?.AsObject()?["coding"]?.AsArray()?[0]?.AsObject();
        firstCoding?["code"]?.GetValue<string>().Should().Be("207Q00000X");
        firstCoding?["display"]?.GetValue<string>().Should().Be("Family Medicine");

        var secondQual = qualifications?[1]?.AsObject();
        var secondCoding = secondQual?["code"]?.AsObject()?["coding"]?.AsArray()?[0]?.AsObject();
        secondCoding?["code"]?.GetValue<string>().Should().Be("419192003");
        secondCoding?["display"]?.GetValue<string>().Should().Be("Internal Medicine");
    }

    [Fact]
    public void GivenPractitionerBuilder_WhenBuildingWithSpecialtyWithoutDisplay_ThenIncludesCodeOnly()
    {
        // Arrange & Act
        var practitioner = PractitionerBuilder.Create(_schemaProvider)
            .WithSpecialty("207Q00000X")
            .Build();

        // Assert
        var qualifications = practitioner.MutableNode["qualification"]?.AsArray();
        var qualification = qualifications?[0]?.AsObject();
        var coding = qualification?["code"]?.AsObject()?["coding"]?.AsArray()?[0]?.AsObject();

        coding?["code"]?.GetValue<string>().Should().Be("207Q00000X");
        coding?.TryGetPropertyValue("display", out _).Should().BeFalse();
    }

    #endregion

    #region Complex Scenarios

    [Fact]
    public void GivenPractitionerBuilder_WhenBuildingCompletePractitioner_ThenIncludesAllProperties()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();
        var npi = "1234567890";
        var specialtyCode = "207Q00000X";

        // Act
        var practitioner = PractitionerBuilder.Create(_schemaProvider)
            .WithId("pract-complete")
            .WithName("Jennifer", "Garcia")
            .WithNpi(npi)
            .WithIdentifier("EMP-555", "http://hospital.example.org/staff-id")
            .WithSpecialty(specialtyCode, display: "Family Medicine")
            .WithTag(tag)
            .Build();

        // Assert
        practitioner.Id.Should().Be("pract-complete");

        var nameArray = practitioner.MutableNode["name"]?.AsArray();
        var name = nameArray?[0]?.AsObject();
        name?["given"]?.AsArray()?[0]?.GetValue<string>().Should().Be("Jennifer");
        name?["family"]?.GetValue<string>().Should().Be("Garcia");

        var identifiers = practitioner.MutableNode["identifier"]?.AsArray();
        identifiers.Should().HaveCount(2);

        var qualifications = practitioner.MutableNode["qualification"]?.AsArray();
        qualifications.Should().HaveCount(1);

        var tags = practitioner.MutableNode["meta"]?["tag"]?.AsArray();
        tags?[0]?["code"]?.GetValue<string>().Should().Be(tag);
    }

    [Fact]
    public void GivenPractitionerBuilder_WhenBuildingMultiplePractitioners_ThenGeneratesDifferentIds()
    {
        // Arrange & Act
        var practitioner1 = PractitionerBuilder.Create(_schemaProvider)
            .WithName("Christopher", "Lee")
            .Build();

        var practitioner2 = PractitionerBuilder.Create(_schemaProvider)
            .WithName("Amanda", "Taylor")
            .Build();

        // Assert
        practitioner1.Id.Should().NotBe(practitioner2.Id);
    }

    #endregion

    #region Meta Tests

    [Fact]
    public void GivenPractitionerBuilder_WhenBuilding_ThenIncludesMetaVersionAndLastUpdated()
    {
        // Arrange & Act
        var practitioner = PractitionerBuilder.Create(_schemaProvider)
            .WithName("Test", "Practitioner")
            .Build();

        // Assert
        practitioner.MutableNode["meta"].Should().NotBeNull();
        var meta = practitioner.MutableNode["meta"]?.AsObject();
        meta?["versionId"]?.GetValue<string>().Should().Be("1");
        meta?["lastUpdated"]?.GetValue<string>().Should().NotBeNullOrEmpty();
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void GivenPractitionerBuilder_WhenBuildingMinimal_ThenCreatesValidPractitioner()
    {
        // Arrange & Act
        var practitioner = PractitionerBuilder.Create(_schemaProvider)
            .Build();

        // Assert
        practitioner.Should().NotBeNull();
        practitioner.ResourceType.Should().Be("Practitioner");
        practitioner.Id.Should().NotBeNullOrEmpty();
        practitioner.MutableNode["active"]?.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void GivenPractitionerBuilder_WhenBuildingWithoutIdentifiers_ThenDoesNotIncludeIdentifiers()
    {
        // Arrange & Act
        var practitioner = PractitionerBuilder.Create(_schemaProvider)
            .WithName("Test", "Practitioner")
            .Build();

        // Assert
        practitioner.MutableNode.TryGetPropertyValue("identifier", out _).Should().BeFalse();
    }

    [Fact]
    public void GivenPractitionerBuilder_WhenBuildingWithoutQualifications_ThenDoesNotIncludeQualifications()
    {
        // Arrange & Act
        var practitioner = PractitionerBuilder.Create(_schemaProvider)
            .WithName("Test", "Practitioner")
            .Build();

        // Assert
        practitioner.MutableNode.TryGetPropertyValue("qualification", out _).Should().BeFalse();
    }

    #endregion
}
