// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.FhirFakes.Builders;
using Ignixa.Abstractions;
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
        practitioner.ShouldNotBeNull();
        practitioner.ResourceType.ShouldBe("Practitioner");
        practitioner.MutableNode["active"]?.GetValue<bool>().ShouldBeTrue();

        var nameArray = practitioner.MutableNode["name"]?.AsArray();
        nameArray.ShouldNotBeNull();
        nameArray!.Count.ShouldBe(1);

        var name = nameArray?[0]?.AsObject();
        name?["use"]?.GetValue<string>().ShouldBe("official");
        name?["given"]?.AsArray()?[0]?.GetValue<string>().ShouldBe("Alice");
        name?["family"]?.GetValue<string>().ShouldBe("Anderson");
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
        practitioner.Id.ShouldBe(expectedId);
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
        practitioner.MutableNode["meta"]?["tag"].ShouldNotBeNull();
        var tags = practitioner.MutableNode["meta"]?["tag"]?.AsArray();
        tags!.Count.ShouldBe(1);

        var metaTag = tags?[0]?.AsObject();
        metaTag?["code"]?.GetValue<string>().ShouldBe(tag);
        metaTag?["system"]?.GetValue<string>().ShouldBe("http://ignixa.dev/test-isolation");
    }

    [Fact]
    public void GivenPractitionerBuilder_WhenNoParametersProvided_ThenBuildsWithDefaults()
    {
        // Arrange & Act
        var practitioner = PractitionerBuilder.Create(_schemaProvider)
            .Build();

        // Assert
        practitioner.ShouldNotBeNull();
        practitioner.ResourceType.ShouldBe("Practitioner");
        practitioner.Id.ShouldNotBeNullOrEmpty();
        practitioner.MutableNode["active"]?.GetValue<bool>().ShouldBeTrue();
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
        nameArray!.Count.ShouldBe(1);

        var name = nameArray?[0]?.AsObject();
        name?["family"]?.GetValue<string>().ShouldBe("Johnson");
        name?.TryGetPropertyValue("given", out _).ShouldBeFalse();
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
        nameArray!.Count.ShouldBe(1);

        var name = nameArray?[0]?.AsObject();
        name?["given"]?.AsArray()?[0]?.GetValue<string>().ShouldBe("Michael");
        name?.TryGetPropertyValue("family", out _).ShouldBeFalse();
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
        nameArray!.Count.ShouldBe(1);

        var name = nameArray?[0]?.AsObject();
        name?["given"]?.AsArray()?[0]?.GetValue<string>().ShouldBe("Robert");
        name?["family"]?.GetValue<string>().ShouldBe("Williams");
    }

    [Fact]
    public void GivenPractitionerBuilder_WhenBuildingWithoutName_ThenDoesNotIncludeName()
    {
        // Arrange & Act
        var practitioner = PractitionerBuilder.Create(_schemaProvider)
            .Build();

        // Assert
        practitioner.MutableNode.TryGetPropertyValue("name", out _).ShouldBeFalse();
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
        identifiers.ShouldNotBeNull();
        identifiers!.Count.ShouldBe(1);

        var identifier = identifiers?[0]?.AsObject();
        identifier?["system"]?.GetValue<string>().ShouldBe("http://hl7.org/fhir/sid/us-npi");
        identifier?["value"]?.GetValue<string>().ShouldBe(npi);
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
        identifiers!.Count.ShouldBe(1);

        var identifier = identifiers?[0]?.AsObject();
        identifier?["system"]?.GetValue<string>().ShouldBe(identifierSystem);
        identifier?["value"]?.GetValue<string>().ShouldBe(identifierValue);
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
        identifiers!.Count.ShouldBe(1);

        var identifier = identifiers?[0]?.AsObject();
        identifier?["value"]?.GetValue<string>().ShouldBe(identifierValue);
        identifier?.TryGetPropertyValue("system", out _).ShouldBeFalse();
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
        identifiers!.Count.ShouldBe(3);

        // Check NPI
        var npiIdentifier = identifiers?[0]?.AsObject();
        npiIdentifier?["system"]?.GetValue<string>().ShouldBe("http://hl7.org/fhir/sid/us-npi");
        npiIdentifier?["value"]?.GetValue<string>().ShouldBe("1234567890");

        // Check staff ID
        var staffIdentifier = identifiers?[1]?.AsObject();
        staffIdentifier?["system"]?.GetValue<string>().ShouldBe("http://hospital.example.org/staff-id");
        staffIdentifier?["value"]?.GetValue<string>().ShouldBe("EMP-999");

        // Check license
        var licenseIdentifier = identifiers?[2]?.AsObject();
        licenseIdentifier?["system"]?.GetValue<string>().ShouldBe("http://state.example.org/medical-license");
        licenseIdentifier?["value"]?.GetValue<string>().ShouldBe("LIC-ABC123");
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
        qualifications.ShouldNotBeNull();
        qualifications!.Count.ShouldBe(1);

        var qualification = qualifications?[0]?.AsObject();
        var code = qualification?["code"]?.AsObject();
        var coding = code?["coding"]?.AsArray();

        coding!.Count.ShouldBe(1);
        var codingObj = coding?[0]?.AsObject();
        codingObj?["system"]?.GetValue<string>().ShouldBe("http://snomed.info/sct");
        codingObj?["code"]?.GetValue<string>().ShouldBe(specialtyCode);
        codingObj?["display"]?.GetValue<string>().ShouldBe(specialtyDisplay);
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

        codingObj?["system"]?.GetValue<string>().ShouldBe(customSystem);
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
        qualifications!.Count.ShouldBe(2);

        var firstQual = qualifications?[0]?.AsObject();
        var firstCoding = firstQual?["code"]?.AsObject()?["coding"]?.AsArray()?[0]?.AsObject();
        firstCoding?["code"]?.GetValue<string>().ShouldBe("207Q00000X");
        firstCoding?["display"]?.GetValue<string>().ShouldBe("Family Medicine");

        var secondQual = qualifications?[1]?.AsObject();
        var secondCoding = secondQual?["code"]?.AsObject()?["coding"]?.AsArray()?[0]?.AsObject();
        secondCoding?["code"]?.GetValue<string>().ShouldBe("419192003");
        secondCoding?["display"]?.GetValue<string>().ShouldBe("Internal Medicine");
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

        coding?["code"]?.GetValue<string>().ShouldBe("207Q00000X");
        coding?.TryGetPropertyValue("display", out _).ShouldBeFalse();
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
        practitioner.Id.ShouldBe("pract-complete");

        var nameArray = practitioner.MutableNode["name"]?.AsArray();
        var name = nameArray?[0]?.AsObject();
        name?["given"]?.AsArray()?[0]?.GetValue<string>().ShouldBe("Jennifer");
        name?["family"]?.GetValue<string>().ShouldBe("Garcia");

        var identifiers = practitioner.MutableNode["identifier"]?.AsArray();
        identifiers!.Count.ShouldBe(2);

        var qualifications = practitioner.MutableNode["qualification"]?.AsArray();
        qualifications!.Count.ShouldBe(1);

        var tags = practitioner.MutableNode["meta"]?["tag"]?.AsArray();
        tags?[0]?["code"]?.GetValue<string>().ShouldBe(tag);
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
        practitioner1.Id.ShouldNotBe(practitioner2.Id);
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
        practitioner.MutableNode["meta"].ShouldNotBeNull();
        var meta = practitioner.MutableNode["meta"]?.AsObject();
        meta?["versionId"]?.GetValue<string>().ShouldBe("1");
        meta?["lastUpdated"]?.GetValue<string>().ShouldNotBeNullOrEmpty();
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
        practitioner.ShouldNotBeNull();
        practitioner.ResourceType.ShouldBe("Practitioner");
        practitioner.Id.ShouldNotBeNullOrEmpty();
        practitioner.MutableNode["active"]?.GetValue<bool>().ShouldBeTrue();
    }

    [Fact]
    public void GivenPractitionerBuilder_WhenBuildingWithoutIdentifiers_ThenDoesNotIncludeIdentifiers()
    {
        // Arrange & Act
        var practitioner = PractitionerBuilder.Create(_schemaProvider)
            .WithName("Test", "Practitioner")
            .Build();

        // Assert
        practitioner.MutableNode.TryGetPropertyValue("identifier", out _).ShouldBeFalse();
    }

    [Fact]
    public void GivenPractitionerBuilder_WhenBuildingWithoutQualifications_ThenDoesNotIncludeQualifications()
    {
        // Arrange & Act
        var practitioner = PractitionerBuilder.Create(_schemaProvider)
            .WithName("Test", "Practitioner")
            .Build();

        // Assert
        practitioner.MutableNode.TryGetPropertyValue("qualification", out _).ShouldBeFalse();
    }

    #endregion
}
