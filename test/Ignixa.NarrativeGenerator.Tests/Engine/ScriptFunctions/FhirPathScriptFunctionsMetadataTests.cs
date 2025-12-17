// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using Shouldly;
using Ignixa.Abstractions;
using Ignixa.NarrativeGenerator.Engine.ScriptFunctions;
using Ignixa.Specification.Generated;

namespace Ignixa.NarrativeGenerator.Tests.Engine.ScriptFunctions;

/// <summary>
/// Tests for metadata helper functions in <see cref="FhirPathScriptFunctions"/>.
/// </summary>
public class FhirPathScriptFunctionsMetadataTests
{
    private readonly FhirPathScriptFunctions _functions;

    public FhirPathScriptFunctionsMetadataTests()
    {
        var schema = new R4CoreSchemaProvider();
        _functions = new FhirPathScriptFunctions(schema);
    }

    #region GetStructureElements Tests

    [Fact]
    public void GivenPatientResourceType_WhenGettingStructureElements_ThenReturnsExpectedElements()
    {
        // Arrange
        var resourceType = "Patient";

        // Act
        var elements = _functions.GetStructureElements(resourceType, FhirVersion.R4).ToList();

        // Assert
        elements.ShouldNotBeEmpty();

        // Should contain name element
        var nameElement = elements.FirstOrDefault(e => e.Name == "name");
        nameElement.ShouldNotBeNull();
        nameElement!.Path.ShouldBe("Patient.name");
        nameElement.Type.ShouldBe("HumanName");
        nameElement.IsPrimitive.ShouldBeFalse();
        nameElement.IsArray.ShouldBeTrue();

        // Should contain birthDate element
        var birthDateElement = elements.FirstOrDefault(e => e.Name == "birthDate");
        birthDateElement.ShouldNotBeNull();
        birthDateElement!.Path.ShouldBe("Patient.birthDate");
        birthDateElement.Type.ShouldBe("date");
        birthDateElement.IsPrimitive.ShouldBeTrue();
        birthDateElement.IsArray.ShouldBeFalse();

        // Should NOT contain excluded elements
        elements.ShouldNotContain(e => e.Name == "id");
        elements.ShouldNotContain(e => e.Name == "meta");
        elements.ShouldNotContain(e => e.Name == "text");
        elements.ShouldNotContain(e => e.Name == "contained");
        elements.ShouldNotContain(e => e.Name == "extension");
        elements.ShouldNotContain(e => e.Name == "modifierExtension");
        elements.ShouldNotContain(e => e.Name == "implicitRules");
        elements.ShouldNotContain(e => e.Name == "language");
    }

    [Fact]
    public void GivenObservationResourceType_WhenGettingStructureElements_ThenReturnsCodeableConcept()
    {
        // Arrange
        var resourceType = "Observation";

        // Act
        var elements = _functions.GetStructureElements(resourceType, FhirVersion.R4).ToList();

        // Assert
        elements.ShouldNotBeEmpty();

        // Should contain code element (CodeableConcept)
        var codeElement = elements.FirstOrDefault(e => e.Name == "code");
        codeElement.ShouldNotBeNull();
        codeElement!.IsCodeableConcept.ShouldBeTrue();
    }

    [Fact]
    public void GivenObservationResourceType_WhenGettingStructureElements_ThenReturnsReferenceType()
    {
        // Arrange
        var resourceType = "Observation";

        // Act
        var elements = _functions.GetStructureElements(resourceType, FhirVersion.R4).ToList();

        // Assert
        elements.ShouldNotBeEmpty();

        // Should contain subject element (Reference)
        var subjectElement = elements.FirstOrDefault(e => e.Name == "subject");
        subjectElement.ShouldNotBeNull();
        subjectElement!.IsReference.ShouldBeTrue();
    }

    [Fact]
    public void GivenInvalidResourceType_WhenGettingStructureElements_ThenReturnsEmpty()
    {
        // Arrange
        var resourceType = "InvalidResourceType";

        // Act
        var elements = _functions.GetStructureElements(resourceType, FhirVersion.R4).ToList();

        // Assert
        elements.ShouldBeEmpty();
    }

    [Fact]
    public void GivenNullResourceType_WhenGettingStructureElements_ThenReturnsEmpty()
    {
        // Arrange
        string? resourceType = null;

        // Act
        var elements = _functions.GetStructureElements(resourceType!, FhirVersion.R4).ToList();

        // Assert
        elements.ShouldBeEmpty();
    }

    [Fact]
    public void GivenEmptyResourceType_WhenGettingStructureElements_ThenReturnsEmpty()
    {
        // Arrange
        var resourceType = string.Empty;

        // Act
        var elements = _functions.GetStructureElements(resourceType, FhirVersion.R4).ToList();

        // Assert
        elements.ShouldBeEmpty();
    }

    #endregion

    #region FormatByType Tests

    [Fact]
    public void GivenDateValue_WhenFormattingByType_ThenReturnsFormattedDate()
    {
        // Arrange
        var value = "1980-01-15";
        var type = "date";
        var culture = new CultureInfo("en-US");

        // Act
        var result = _functions.FormatByType(value, type, culture);

        // Assert
        result.ShouldBe("January 15, 1980");
    }

    [Fact]
    public void GivenDateTimeValue_WhenFormattingByType_ThenReturnsFormattedDateTime()
    {
        // Arrange
        var value = "2023-12-25T10:30:00Z";
        var type = "dateTime";
        var culture = new CultureInfo("en-US");

        // Act
        var result = _functions.FormatByType(value, type, culture);

        // Assert
        result.ShouldContain("December 25, 2023");
        result.ShouldContain("at");
    }

    [Fact]
    public void GivenInstantValue_WhenFormattingByType_ThenReturnsFormattedDateTime()
    {
        // Arrange
        var value = "2023-12-25T10:30:00Z";
        var type = "instant";
        var culture = new CultureInfo("en-US");

        // Act
        var result = _functions.FormatByType(value, type, culture);

        // Assert
        result.ShouldContain("December 25, 2023");
    }

    [Fact]
    public void GivenBooleanTrueValue_WhenFormattingByType_ThenReturnsYes()
    {
        // Arrange
        var value = "true";
        var type = "boolean";

        // Act
        var result = _functions.FormatByType(value, type);

        // Assert
        result.ShouldBe("Yes");
    }

    [Fact]
    public void GivenBooleanFalseValue_WhenFormattingByType_ThenReturnsNo()
    {
        // Arrange
        var value = "false";
        var type = "boolean";

        // Act
        var result = _functions.FormatByType(value, type);

        // Assert
        result.ShouldBe("No");
    }

    [Fact]
    public void GivenStringValue_WhenFormattingByType_ThenReturnsOriginalValue()
    {
        // Arrange
        var value = "Test String";
        var type = "string";

        // Act
        var result = _functions.FormatByType(value, type);

        // Assert
        result.ShouldBe("Test String");
    }

    [Fact]
    public void GivenNullValue_WhenFormattingByType_ThenReturnsEmpty()
    {
        // Arrange
        string? value = null;
        var type = "string";

        // Act
        var result = _functions.FormatByType(value, type);

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public void GivenEmptyValue_WhenFormattingByType_ThenReturnsEmpty()
    {
        // Arrange
        var value = string.Empty;
        var type = "string";

        // Act
        var result = _functions.FormatByType(value, type);

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public void GivenInvalidDateValue_WhenFormattingByType_ThenReturnsOriginalValue()
    {
        // Arrange
        var value = "invalid-date";
        var type = "date";

        // Act
        var result = _functions.FormatByType(value, type);

        // Assert
        result.ShouldBe("invalid-date");
    }

    #endregion
}
