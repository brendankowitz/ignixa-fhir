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
        patient.MutableNode["identifier"].Should().NotBeNull();
        var identifiers = patient.MutableNode["identifier"]?.AsArray();
        identifiers.Should().HaveCount(1);

        var identifier = identifiers?[0]?.AsObject();
        identifier?["value"]?.GetValue<string>().Should().Be("12345");

        // Verify type structure
        var type = identifier?["type"]?.AsObject();
        type.Should().NotBeNull();

        var codings = type?["coding"]?.AsArray();
        codings.Should().HaveCount(1);

        var coding = codings?[0]?.AsObject();
        coding?["system"]?.GetValue<string>().Should().Be("http://terminology.hl7.org/CodeSystem/v2-0203");
        coding?["code"]?.GetValue<string>().Should().Be("MR");
        coding?["display"]?.GetValue<string>().Should().Be("Medical Record");
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
        patient.MutableNode["identifier"].Should().NotBeNull();
        var identifiers = patient.MutableNode["identifier"]?.AsArray();
        identifiers.Should().HaveCount(1);

        var identifier = identifiers?[0]?.AsObject();
        identifier?["value"]?.GetValue<string>().Should().Be("123-45-6789");

        var coding = identifier?["type"]?["coding"]?.AsArray()?[0]?.AsObject();
        coding?["system"]?.GetValue<string>().Should().Be("http://terminology.hl7.org/CodeSystem/v2-0203");
        coding?["code"]?.GetValue<string>().Should().Be("SS");
        coding?.ContainsKey("display").Should().BeFalse("display was not provided");
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
        identifiers.Should().HaveCount(1);

        var identifier = identifiers?[0]?.AsObject();
        identifier?["value"]?.GetValue<string>().Should().Be("123-45-6789");
        identifier?["system"]?.GetValue<string>().Should().Be("http://hl7.org/fhir/sid/us-ssn");

        var coding = identifier?["type"]?["coding"]?.AsArray()?[0]?.AsObject();
        coding?["code"]?.GetValue<string>().Should().Be("SS");
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
        identifiers.Should().HaveCount(3);

        // Check MR identifier
        var mrIdentifier = identifiers?.FirstOrDefault(i =>
            i?["value"]?.GetValue<string>() == "MR-12345");
        mrIdentifier.Should().NotBeNull();
        var mrCoding = mrIdentifier?["type"]?["coding"]?.AsArray()?[0]?.AsObject();
        mrCoding?["code"]?.GetValue<string>().Should().Be("MR");

        // Check SSN identifier
        var ssnIdentifier = identifiers?.FirstOrDefault(i =>
            i?["value"]?.GetValue<string>() == "123-45-6789");
        ssnIdentifier.Should().NotBeNull();
        var ssnCoding = ssnIdentifier?["type"]?["coding"]?.AsArray()?[0]?.AsObject();
        ssnCoding?["code"]?.GetValue<string>().Should().Be("SS");

        // Check DL identifier
        var dlIdentifier = identifiers?.FirstOrDefault(i =>
            i?["value"]?.GetValue<string>() == "DL-ABC123");
        dlIdentifier.Should().NotBeNull();
        var dlCoding = dlIdentifier?["type"]?["coding"]?.AsArray()?[0]?.AsObject();
        dlCoding?["code"]?.GetValue<string>().Should().Be("DL");
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
        patient.MutableNode.ContainsKey("identifier").Should().BeFalse("no identifiers were added");
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
        identifiers.Should().HaveCount(1);

        var identifier = identifiers?[0]?.AsObject();
        identifier?["value"]?.GetValue<string>().Should().Be("TEST-VALUE");

        var coding = identifier?["type"]?["coding"]?.AsArray()?[0]?.AsObject();
        coding?["system"]?.GetValue<string>().Should().Be("http://terminology.hl7.org/CodeSystem/v2-0203");
        coding?["code"]?.GetValue<string>().Should().Be(typeCode);
        coding?["display"]?.GetValue<string>().Should().Be(typeDisplay);
    }
}
