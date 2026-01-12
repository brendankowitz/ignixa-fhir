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
/// Unit tests for PatientBuilder typed identifier support.
/// Tests the WithTypedIdentifier method for creating identifiers with FHIR v2-0203 type codes.
/// </summary>
public class PatientBuilder_TypedIdentifierTests
{
    private readonly IFhirSchemaProvider _schemaProvider = new R4CoreSchemaProvider();

    [Fact]
    public void GivenPatientBuilder_WhenBuildingWithTypedIdentifier_ThenCreatesIdentifierWithType()
    {
        // Arrange & Act
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .WithAge(30)
            .WithGender("male")
            .WithTypedIdentifier("12345", "http://terminology.hl7.org/CodeSystem/v2-0203", "MR", "Medical Record")
            .Build();

        // Assert
        patient.MutableNode["identifier"].ShouldNotBeNull();
        var identifiers = patient.MutableNode["identifier"]?.AsArray();
        identifiers!.Count.ShouldBe(1);

        var identifier = identifiers?[0]?.AsObject();
        identifier?["value"]?.GetValue<string>().ShouldBe("12345");

        // Verify type structure
        var type = identifier?["type"]?.AsObject();
        type.ShouldNotBeNull();

        var codings = type?["coding"]?.AsArray();
        codings!.Count.ShouldBe(1);

        var coding = codings?[0]?.AsObject();
        coding?["system"]?.GetValue<string>().ShouldBe("http://terminology.hl7.org/CodeSystem/v2-0203");
        coding?["code"]?.GetValue<string>().ShouldBe("MR");
        coding?["display"]?.GetValue<string>().ShouldBe("Medical Record");
    }

    [Fact]
    public void GivenPatientBuilder_WhenBuildingWithTypedIdentifierWithoutDisplay_ThenCreatesIdentifierWithoutDisplay()
    {
        // Arrange & Act
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .WithAge(30)
            .WithGender("female")
            .WithTypedIdentifier("123-45-6789", "http://terminology.hl7.org/CodeSystem/v2-0203", "SS")
            .Build();

        // Assert
        patient.MutableNode["identifier"].ShouldNotBeNull();
        var identifiers = patient.MutableNode["identifier"]?.AsArray();
        identifiers!.Count.ShouldBe(1);

        var identifier = identifiers?[0]?.AsObject();
        identifier?["value"]?.GetValue<string>().ShouldBe("123-45-6789");

        var coding = identifier?["type"]?["coding"]?.AsArray()?[0]?.AsObject();
        coding?["system"]?.GetValue<string>().ShouldBe("http://terminology.hl7.org/CodeSystem/v2-0203");
        coding?["code"]?.GetValue<string>().ShouldBe("SS");
        coding?.ContainsKey("display").ShouldBeFalse("display was not provided");
    }

    [Fact]
    public void GivenPatientBuilder_WhenBuildingWithTypedIdentifierWithSystem_ThenCreatesIdentifierWithSystem()
    {
        // Arrange & Act
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .WithAge(35)
            .WithGender("male")
            .WithTypedIdentifier(
                "123-45-6789",
                "http://terminology.hl7.org/CodeSystem/v2-0203",
                "SS",
                "Social Security Number",
                "http://hl7.org/fhir/sid/us-ssn")
            .Build();

        // Assert
        var identifiers = patient.MutableNode["identifier"]?.AsArray();
        identifiers!.Count.ShouldBe(1);

        var identifier = identifiers?[0]?.AsObject();
        identifier?["value"]?.GetValue<string>().ShouldBe("123-45-6789");
        identifier?["system"]?.GetValue<string>().ShouldBe("http://hl7.org/fhir/sid/us-ssn");

        var coding = identifier?["type"]?["coding"]?.AsArray()?[0]?.AsObject();
        coding?["code"]?.GetValue<string>().ShouldBe("SS");
    }

    [Fact]
    public void GivenPatientBuilder_WhenBuildingWithMultipleTypedIdentifiers_ThenCreatesAllIdentifiers()
    {
        // Arrange & Act
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .WithAge(40)
            .WithGender("female")
            .WithTypedIdentifier("MR-12345", "http://terminology.hl7.org/CodeSystem/v2-0203", "MR", "Medical Record")
            .WithTypedIdentifier("123-45-6789", "http://terminology.hl7.org/CodeSystem/v2-0203", "SS", "Social Security Number")
            .WithTypedIdentifier("DL-ABC123", "http://terminology.hl7.org/CodeSystem/v2-0203", "DL", "Driver's License")
            .Build();

        // Assert
        var identifiers = patient.MutableNode["identifier"]?.AsArray();
        identifiers!.Count.ShouldBe(3);

        // Check MR identifier
        var mrIdentifier = identifiers?.FirstOrDefault(i =>
            i?["value"]?.GetValue<string>() == "MR-12345");
        mrIdentifier.ShouldNotBeNull();
        var mrCoding = mrIdentifier?["type"]?["coding"]?.AsArray()?[0]?.AsObject();
        mrCoding?["code"]?.GetValue<string>().ShouldBe("MR");

        // Check SSN identifier
        var ssnIdentifier = identifiers?.FirstOrDefault(i =>
            i?["value"]?.GetValue<string>() == "123-45-6789");
        ssnIdentifier.ShouldNotBeNull();
        var ssnCoding = ssnIdentifier?["type"]?["coding"]?.AsArray()?[0]?.AsObject();
        ssnCoding?["code"]?.GetValue<string>().ShouldBe("SS");

        // Check DL identifier
        var dlIdentifier = identifiers?.FirstOrDefault(i =>
            i?["value"]?.GetValue<string>() == "DL-ABC123");
        dlIdentifier.ShouldNotBeNull();
        var dlCoding = dlIdentifier?["type"]?["coding"]?.AsArray()?[0]?.AsObject();
        dlCoding?["code"]?.GetValue<string>().ShouldBe("DL");
    }

    [Fact]
    public void GivenPatientBuilder_WhenBuildingWithoutTypedIdentifiers_ThenNoIdentifierField()
    {
        // Arrange & Act
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .WithAge(25)
            .WithGender("male")
            .Build();

        // Assert
        patient.MutableNode.ContainsKey("identifier").ShouldBeFalse("no identifiers were added");
    }

    [Theory]
    [InlineData("MR", "Medical Record")]
    [InlineData("SS", "Social Security Number")]
    [InlineData("DL", "Driver's License")]
    [InlineData("PPN", "Passport Number")]
    [InlineData("EN", "Employer Number")]
    public void GivenPatientBuilder_WhenBuildingWithCommonIdentifierTypes_ThenCreatesCorrectIdentifierType(
        string typeCode,
        string typeDisplay)
    {
        // Arrange & Act
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .WithAge(30)
            .WithGender("male")
            .WithTypedIdentifier("TEST-VALUE", "http://terminology.hl7.org/CodeSystem/v2-0203", typeCode, typeDisplay)
            .Build();

        // Assert
        var identifiers = patient.MutableNode["identifier"]?.AsArray();
        identifiers!.Count.ShouldBe(1);

        var identifier = identifiers?[0]?.AsObject();
        identifier?["value"]?.GetValue<string>().ShouldBe("TEST-VALUE");

        var coding = identifier?["type"]?["coding"]?.AsArray()?[0]?.AsObject();
        coding?["system"]?.GetValue<string>().ShouldBe("http://terminology.hl7.org/CodeSystem/v2-0203");
        coding?["code"]?.GetValue<string>().ShouldBe(typeCode);
        coding?["display"]?.GetValue<string>().ShouldBe(typeDisplay);
    }
}
