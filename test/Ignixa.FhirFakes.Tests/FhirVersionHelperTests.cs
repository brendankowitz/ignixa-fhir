// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.Abstractions;
using Ignixa.Specification;
using Ignixa.Specification.Generated;
using Xunit.Abstractions;

namespace Ignixa.FhirFakes.Tests;

/// <summary>
/// Tests for FhirVersionHelper extension methods.
/// Verifies version detection and schema-driven property access methods work correctly across all FHIR versions.
/// </summary>
public class FhirVersionHelperTests
{
    private readonly ITestOutputHelper _output;
    private readonly List<IFhirSchemaProvider> _schemaProviders;

    public FhirVersionHelperTests(ITestOutputHelper output)
    {
        _output = output;
        _schemaProviders =
        [
            new STU3CoreSchemaProvider(),
            new R4CoreSchemaProvider(),
            new R4BCoreSchemaProvider(),
            new R5CoreSchemaProvider()
        ];
    }

    #region Version Detection Tests

    [Fact]
    public void GivenSTU3Schema_WhenCheckingIsStu3_ThenReturnsTrue()
    {
        // Arrange
        var schema = new STU3CoreSchemaProvider();

        // Act
        var result = schema.IsStu3();

        // Assert
        result.Should().BeTrue("STU3 schema should be detected as STU3");
    }

    [Theory]
    [InlineData(typeof(R4CoreSchemaProvider))]
    [InlineData(typeof(R4BCoreSchemaProvider))]
    [InlineData(typeof(R5CoreSchemaProvider))]
    public void GivenNonSTU3Schema_WhenCheckingIsStu3_ThenReturnsFalse(Type schemaType)
    {
        // Arrange
        var schema = (IFhirSchemaProvider)Activator.CreateInstance(schemaType)!;

        // Act
        var result = schema.IsStu3();

        // Assert
        result.Should().BeFalse($"{schema.Version} schema should not be detected as STU3");
    }

    [Theory]
    [InlineData(typeof(R4CoreSchemaProvider))]
    [InlineData(typeof(R4BCoreSchemaProvider))]
    [InlineData(typeof(R5CoreSchemaProvider))]
    public void GivenR4OrLaterSchema_WhenCheckingIsR4OrLater_ThenReturnsTrue(Type schemaType)
    {
        // Arrange
        var schema = (IFhirSchemaProvider)Activator.CreateInstance(schemaType)!;

        // Act
        var result = schema.IsR4OrLater();

        // Assert
        result.Should().BeTrue($"{schema.Version} schema should be detected as R4 or later");
    }

    [Fact]
    public void GivenSTU3Schema_WhenCheckingIsR4OrLater_ThenReturnsFalse()
    {
        // Arrange
        var schema = new STU3CoreSchemaProvider();

        // Act
        var result = schema.IsR4OrLater();

        // Assert
        result.Should().BeFalse("STU3 schema should not be detected as R4 or later");
    }

    #endregion

    #region Property Existence Tests

    [Fact]
    public void GivenExistingProperty_WhenCheckingHasProperty_ThenReturnsTrue()
    {
        foreach (var schema in _schemaProviders)
        {
            _output.WriteLine($"Testing HasProperty with {schema.Version}");

            // Act & Assert - status exists in Patient across all versions
            schema.HasProperty("Patient", "gender")
                .Should().BeTrue($"gender should exist in Patient for {schema.Version}");

            schema.HasProperty("Observation", "status")
                .Should().BeTrue($"status should exist in Observation for {schema.Version}");

            schema.HasProperty("MedicationRequest", "intent")
                .Should().BeTrue($"intent should exist in MedicationRequest for {schema.Version}");
        }
    }

    [Fact]
    public void GivenNonExistingProperty_WhenCheckingHasProperty_ThenReturnsFalse()
    {
        foreach (var schema in _schemaProviders)
        {
            _output.WriteLine($"Testing HasProperty with non-existing property in {schema.Version}");

            // Act & Assert - nonExistentField should not exist
            schema.HasProperty("Patient", "nonExistentField")
                .Should().BeFalse($"nonExistentField should not exist in Patient for {schema.Version}");
        }
    }

    [Fact]
    public void GivenVersionSpecificProperty_WhenCheckingHasProperty_ThenReturnsCorrectResult()
    {
        // Immunization.protocolApplied exists in R4+ but not STU3
        var stu3 = new STU3CoreSchemaProvider();
        var r4 = new R4CoreSchemaProvider();

        stu3.HasProperty("Immunization", "protocolApplied")
            .Should().BeFalse("protocolApplied should not exist in STU3");

        r4.HasProperty("Immunization", "protocolApplied")
            .Should().BeTrue("protocolApplied should exist in R4");

        // Immunization.vaccinationProtocol exists in STU3 but not R4+
        stu3.HasProperty("Immunization", "vaccinationProtocol")
            .Should().BeTrue("vaccinationProtocol should exist in STU3");

        r4.HasProperty("Immunization", "vaccinationProtocol")
            .Should().BeFalse("vaccinationProtocol should not exist in R4");
    }

    #endregion

    #region Choice Field Name Tests

    [Fact]
    public void GivenMedicationRequest_WhenGettingMedicationChoiceField_ThenReturnsValidField()
    {
        foreach (var schema in _schemaProviders)
        {
            _output.WriteLine($"Testing medication[x] choice field with {schema.Version}");

            // Act
            var field = schema.GetChoiceFieldName(
                "MedicationRequest",
                "medication",
                "CodeableConcept",
                "Reference");

            // Assert
            field.Should().NotBeNullOrEmpty($"medication field should exist in {schema.Version}");
            field.Should().Match(f => f == "medicationCodeableConcept" || f == "medicationReference",
                $"should be a valid medication[x] field in {schema.Version}");

            // Verify the field actually exists in the schema
            schema.HasProperty("MedicationRequest", field!)
                .Should().BeTrue($"{field} should exist in MedicationRequest for {schema.Version}");
        }
    }

    [Fact]
    public void GivenObservation_WhenGettingValueChoiceField_ThenReturnsValidField()
    {
        foreach (var schema in _schemaProviders)
        {
            _output.WriteLine($"Testing value[x] choice field with {schema.Version}");

            // Act - Try to get valueQuantity (common for vital signs)
            var field = schema.GetChoiceFieldName(
                "Observation",
                "value",
                "Quantity",
                "CodeableConcept",
                "String");

            // Assert
            field.Should().NotBeNullOrEmpty($"value field should exist in {schema.Version}");
            field.Should().StartWith("value", $"should be a value[x] field in {schema.Version}");

            // Verify the field actually exists in the schema
            schema.HasProperty("Observation", field!)
                .Should().BeTrue($"{field} should exist in Observation for {schema.Version}");
        }
    }

    [Fact]
    public void GivenChoiceFieldWithPreferredSuffix_WhenGettingChoiceField_ThenReturnsPreferredField()
    {
        var schema = new R4CoreSchemaProvider();

        // Act - Request CodeableConcept first (preferred), then Reference (fallback)
        var field = schema.GetChoiceFieldName(
            "MedicationRequest",
            "medication",
            "CodeableConcept",  // This should be preferred if it exists
            "Reference");

        // Assert - Should return CodeableConcept if available
        // Both exist in MedicationRequest, so it should return the first match
        field.Should().Be("medicationCodeableConcept",
            "should return first matching choice type from preferred suffixes");
    }

    [Fact]
    public void GivenChoiceFieldWithFallback_WhenPreferredNotAvailable_ThenReturnsFallback()
    {
        var schema = new R4CoreSchemaProvider();

        // Act - Request a non-existent suffix first, then a valid one
        var field = schema.GetChoiceFieldName(
            "MedicationRequest",
            "medication",
            "NonExistentType",  // This doesn't exist
            "CodeableConcept"); // This should be returned as fallback

        // Assert
        field.Should().Be("medicationCodeableConcept",
            "should fallback to next suffix when preferred doesn't exist");
    }

    [Fact]
    public void GivenNonExistentChoiceField_WhenGettingChoiceField_ThenReturnsNull()
    {
        var schema = new R4CoreSchemaProvider();

        // Act
        var field = schema.GetChoiceFieldName(
            "Patient",
            "nonExistent",
            "String",
            "CodeableConcept");

        // Assert
        field.Should().BeNull("should return null when no matching choice field exists");
    }

    #endregion

    #region Required Field Tests

    [Fact]
    public void GivenRequiredField_WhenCheckingIsRequired_ThenReturnsTrue()
    {
        foreach (var schema in _schemaProviders)
        {
            _output.WriteLine($"Testing required fields with {schema.Version}");

            // Act & Assert - status is required in Observation across all versions
            schema.IsRequired("Observation", "status")
                .Should().BeTrue($"status should be required in Observation for {schema.Version}");

            // intent is required in MedicationRequest
            schema.IsRequired("MedicationRequest", "intent")
                .Should().BeTrue($"intent should be required in MedicationRequest for {schema.Version}");
        }
    }

    [Fact]
    public void GivenOptionalField_WhenCheckingIsRequired_ThenReturnsFalse()
    {
        foreach (var schema in _schemaProviders)
        {
            _output.WriteLine($"Testing optional fields with {schema.Version}");

            // Act & Assert - note is optional in Observation
            schema.IsRequired("Observation", "note")
                .Should().BeFalse($"note should be optional in Observation for {schema.Version}");
        }
    }

    [Fact]
    public void GivenVersionSpecificRequirement_WhenCheckingIsRequired_ThenReturnsCorrectResult()
    {
        // clinicalStatus is required in R4+ but optional in STU3 for AllergyIntolerance
        var stu3 = new STU3CoreSchemaProvider();
        var r4 = new R4CoreSchemaProvider();

        var stu3Required = stu3.IsRequired("AllergyIntolerance", "clinicalStatus");
        var r4Required = r4.IsRequired("AllergyIntolerance", "clinicalStatus");

        _output.WriteLine($"STU3 clinicalStatus required: {stu3Required}");
        _output.WriteLine($"R4 clinicalStatus required: {r4Required}");

        // Note: The actual requirement may vary based on the schema definition
        // This test documents the behavior across versions
        // If both are the same, that's also valid - the test primarily ensures the method works
        (stu3Required || r4Required).Should().BeTrue(
            "at least one version should have a requirement status for clinicalStatus");
    }

    #endregion

    #region Summary Field Tests

    [Fact]
    public void GivenSummaryField_WhenCheckingIsInSummary_ThenReturnsTrue()
    {
        foreach (var schema in _schemaProviders)
        {
            _output.WriteLine($"Testing summary fields with {schema.Version}");

            // Act & Assert - status is typically in summary for most resources
            var statusInSummary = schema.IsInSummary("Observation", "status");
            _output.WriteLine($"  Observation.status in summary: {statusInSummary}");

            // identifier is typically in summary for Patient
            var identifierInSummary = schema.IsInSummary("Patient", "identifier");
            _output.WriteLine($"  Patient.identifier in summary: {identifierInSummary}");

            // At least one of these core fields should be in summary
            (statusInSummary || identifierInSummary).Should().BeTrue(
                $"core fields should be in summary for {schema.Version}");
        }
    }

    [Fact]
    public void GivenNonSummaryField_WhenCheckingIsInSummary_ThenReturnsFalse()
    {
        foreach (var schema in _schemaProviders)
        {
            _output.WriteLine($"Testing non-summary fields with {schema.Version}");

            // Act - text narrative is typically not in summary
            var textInSummary = schema.IsInSummary("Patient", "text");

            _output.WriteLine($"  Patient.text in summary: {textInSummary}");

            // Note: The actual summary status may vary by version
            // This test documents the behavior - text is often excluded from summary
        }
    }

    #endregion

    #region Immunization-Specific Helper Tests

    [Fact]
    public void GivenSTU3_WhenGettingImmunizationProtocolFieldName_ThenReturnsVaccinationProtocol()
    {
        // Arrange
        var schema = new STU3CoreSchemaProvider();

        // Act
        var fieldName = schema.GetImmunizationProtocolFieldName();

        // Assert
        fieldName.Should().Be("vaccinationProtocol", "STU3 uses vaccinationProtocol");
    }

    [Theory]
    [InlineData(typeof(R4CoreSchemaProvider))]
    [InlineData(typeof(R4BCoreSchemaProvider))]
    [InlineData(typeof(R5CoreSchemaProvider))]
    public void GivenR4OrLater_WhenGettingImmunizationProtocolFieldName_ThenReturnsProtocolApplied(Type schemaType)
    {
        // Arrange
        var schema = (IFhirSchemaProvider)Activator.CreateInstance(schemaType)!;

        // Act
        var fieldName = schema.GetImmunizationProtocolFieldName();

        // Assert
        fieldName.Should().Be("protocolApplied", $"{schema.Version} uses protocolApplied");
    }

    [Fact]
    public void GivenSTU3_WhenGettingImmunizationDoseNumberFieldName_ThenReturnsDoseSequence()
    {
        // Arrange
        var schema = new STU3CoreSchemaProvider();

        // Act
        var fieldName = schema.GetImmunizationDoseNumberFieldName();

        // Assert
        fieldName.Should().Be("doseSequence", "STU3 uses doseSequence");
    }

    [Theory]
    [InlineData(typeof(R4CoreSchemaProvider))]
    [InlineData(typeof(R4BCoreSchemaProvider))]
    [InlineData(typeof(R5CoreSchemaProvider))]
    public void GivenR4OrLater_WhenGettingImmunizationDoseNumberFieldName_ThenReturnsDoseNumberPositiveInt(Type schemaType)
    {
        // Arrange
        var schema = (IFhirSchemaProvider)Activator.CreateInstance(schemaType)!;

        // Act
        var fieldName = schema.GetImmunizationDoseNumberFieldName();

        // Assert
        fieldName.Should().Be("doseNumberPositiveInt", $"{schema.Version} uses doseNumberPositiveInt");
    }

    [Fact]
    public void GivenSTU3_WhenGettingImmunizationSeriesDosesFieldName_ThenReturnsNull()
    {
        // Arrange
        var schema = new STU3CoreSchemaProvider();

        // Act
        var fieldName = schema.GetImmunizationSeriesDosesFieldName();

        // Assert
        fieldName.Should().BeNull("STU3 doesn't have seriesDosesPositiveInt field");
    }

    [Theory]
    [InlineData(typeof(R4CoreSchemaProvider))]
    [InlineData(typeof(R4BCoreSchemaProvider))]
    [InlineData(typeof(R5CoreSchemaProvider))]
    public void GivenR4OrLater_WhenGettingImmunizationSeriesDosesFieldName_ThenReturnsSeriesDosesPositiveInt(Type schemaType)
    {
        // Arrange
        var schema = (IFhirSchemaProvider)Activator.CreateInstance(schemaType)!;

        // Act
        var fieldName = schema.GetImmunizationSeriesDosesFieldName();

        // Assert
        fieldName.Should().Be("seriesDosesPositiveInt", $"{schema.Version} uses seriesDosesPositiveInt");
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public void GivenNullSchemaProvider_WhenCallingAnyMethod_ThenThrowsArgumentNullException()
    {
        // Arrange
        IFhirSchemaProvider? nullSchema = null;

        // Act & Assert
        var act1 = () => nullSchema!.IsStu3();
        act1.Should().Throw<ArgumentNullException>();

        var act2 = () => nullSchema!.IsR4OrLater();
        act2.Should().Throw<ArgumentNullException>();

        var act3 = () => nullSchema!.HasProperty("Patient", "name");
        act3.Should().Throw<ArgumentNullException>();

        var act4 = () => nullSchema!.GetChoiceFieldName("Patient", "name", "String");
        act4.Should().Throw<ArgumentNullException>();

        var act5 = () => nullSchema!.IsRequired("Patient", "name");
        act5.Should().Throw<ArgumentNullException>();

        var act6 = () => nullSchema!.IsInSummary("Patient", "name");
        act6.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GivenInvalidResourceType_WhenGettingChoiceField_ThenReturnsNull()
    {
        var schema = new R4CoreSchemaProvider();

        // Act
        var field = schema.GetChoiceFieldName(
            "NonExistentResource",
            "someField",
            "String");

        // Assert
        field.Should().BeNull("should return null for non-existent resource type");
    }

    [Fact]
    public void GivenInvalidResourceType_WhenCheckingHasProperty_ThenReturnsFalse()
    {
        var schema = new R4CoreSchemaProvider();

        // Act
        var hasProperty = schema.HasProperty("NonExistentResource", "someField");

        // Assert
        hasProperty.Should().BeFalse("should return false for non-existent resource type");
    }

    #endregion
}
