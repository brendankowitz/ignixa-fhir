/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Unit tests for SchemaAwareElement - validates choice type navigation,
 * type name normalization, and proper InstanceType resolution.
 */

using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Serialization.Tests.TestHelpers;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;
using Xunit;

namespace Ignixa.Serialization.Tests.SourceNodes;

/// <summary>
/// Tests for SchemaAwareElement focusing on FHIR choice type handling
/// and type name normalization per FHIRPath specification.
/// </summary>
public class SchemaAwareElementTests
{
    private readonly IFhirSchemaProvider _r4Provider;

    public SchemaAwareElementTests()
    {
        _r4Provider = FhirVersion.R4.GetSchemaProvider();
    }

    #region Choice Type Navigation Tests

    /// <summary>
    /// Test that navigating to 'value' (without [x] suffix) matches 'valueString' element.
    /// Per FHIR FHIRPath spec: choice elements are accessed by base name without suffix.
    /// </summary>
    [Fact]
    public void GivenObservationWithValueString_WhenNavigatingToValue_ThenReturnsValueStringElement()
    {
        // Arrange
        var observationJson = """
        {
          "resourceType": "Observation",
          "id": "obs1",
          "status": "final",
          "code": { "text": "test" },
          "valueString": "foo"
        }
        """;

        var resource = ResourceJsonNode.Parse(observationJson);
        var typedElement = resource.ToElement(_r4Provider);

        // Act
        var valueChildren = typedElement.Children("value").ToList();

        // Assert
        Assert.Single(valueChildren);
        Assert.Equal("valueString", valueChildren[0].Name);
        Assert.Equal("string", valueChildren[0].InstanceType);
        Assert.Equal("foo", valueChildren[0].Value);
    }

    /// <summary>
    /// Test that navigating to 'value' matches 'valueInteger' element.
    /// </summary>
    [Fact]
    public void GivenObservationWithValueInteger_WhenNavigatingToValue_ThenReturnsValueIntegerElement()
    {
        // Arrange
        var observationJson = """
        {
          "resourceType": "Observation",
          "id": "obs2",
          "status": "final",
          "code": { "text": "test" },
          "valueInteger": 42
        }
        """;

        var resource = ResourceJsonNode.Parse(observationJson);
        var typedElement = resource.ToElement(_r4Provider);

        // Act
        var valueChildren = typedElement.Children("value").ToList();

        // Assert
        Assert.Single(valueChildren);
        Assert.Equal("valueInteger", valueChildren[0].Name);
        Assert.Equal("integer", valueChildren[0].InstanceType);
        Assert.Equal(42, valueChildren[0].Value);
    }

    /// <summary>
    /// Test that navigating to 'value' matches 'valueBoolean' element.
    /// </summary>
    [Fact]
    public void GivenObservationWithValueBoolean_WhenNavigatingToValue_ThenReturnsValueBooleanElement()
    {
        // Arrange
        var observationJson = """
        {
          "resourceType": "Observation",
          "id": "obs3",
          "status": "final",
          "code": { "text": "test" },
          "valueBoolean": true
        }
        """;

        var resource = ResourceJsonNode.Parse(observationJson);
        var typedElement = resource.ToElement(_r4Provider);

        // Act
        var valueChildren = typedElement.Children("value").ToList();

        // Assert
        Assert.Single(valueChildren);
        Assert.Equal("valueBoolean", valueChildren[0].Name);
        Assert.Equal("boolean", valueChildren[0].InstanceType);
        Assert.Equal(true, valueChildren[0].Value);
    }

    /// <summary>
    /// Test that navigating to 'value' matches 'valueQuantity' (complex type).
    /// </summary>
    [Fact]
    public void GivenObservationWithValueQuantity_WhenNavigatingToValue_ThenReturnsValueQuantityElement()
    {
        // Arrange
        var observationJson = """
        {
          "resourceType": "Observation",
          "id": "obs4",
          "status": "final",
          "code": { "text": "test" },
          "valueQuantity": {
            "value": 185,
            "unit": "cm",
            "system": "http://unitsofmeasure.org",
            "code": "cm"
          }
        }
        """;

        var resource = ResourceJsonNode.Parse(observationJson);
        var typedElement = resource.ToElement(_r4Provider);

        // Act
        var valueChildren = typedElement.Children("value").ToList();

        // Assert
        Assert.Single(valueChildren);
        Assert.Equal("valueQuantity", valueChildren[0].Name);
        Assert.Equal("Quantity", valueChildren[0].InstanceType); // Complex types remain capitalized
    }

    /// <summary>
    /// Test that navigating to 'value' when no value[x] exists returns empty.
    /// </summary>
    [Fact]
    public void GivenObservationWithoutValue_WhenNavigatingToValue_ThenReturnsEmpty()
    {
        // Arrange
        var observationJson = """
        {
          "resourceType": "Observation",
          "id": "obs5",
          "status": "final",
          "code": { "text": "test" }
        }
        """;

        var resource = ResourceJsonNode.Parse(observationJson);
        var typedElement = resource.ToElement(_r4Provider);

        // Act
        var valueChildren = typedElement.Children("value").ToList();

        // Assert
        Assert.Empty(valueChildren);
    }

    /// <summary>
    /// Test that direct navigation to 'valueString' also works (specific navigation).
    /// </summary>
    [Fact]
    public void GivenObservationWithValueString_WhenNavigatingToValueString_ThenReturnsElement()
    {
        // Arrange
        var observationJson = """
        {
          "resourceType": "Observation",
          "id": "obs1",
          "status": "final",
          "code": { "text": "test" },
          "valueString": "foo"
        }
        """;

        var resource = ResourceJsonNode.Parse(observationJson);
        var typedElement = resource.ToElement(_r4Provider);

        // Act
        var valueStringChildren = typedElement.Children("valueString").ToList();

        // Assert
        Assert.Single(valueStringChildren);
        Assert.Equal("valueString", valueStringChildren[0].Name);
        Assert.Equal("string", valueStringChildren[0].InstanceType);
        Assert.Equal("foo", valueStringChildren[0].Value);
    }

    #endregion

    #region Type Normalization Tests

    /// <summary>
    /// Test that primitive type names are lowercase per FHIRPath spec.
    /// </summary>
    [Theory]
    [InlineData("valueString", "string", "test")]
    [InlineData("valueInteger", "integer", 42)]
    [InlineData("valueBoolean", "boolean", true)]
    [InlineData("valueDecimal", "decimal", "3.14")]
    [InlineData("valueUri", "uri", "http://example.com")]
    [InlineData("valueCode", "code", "final")]
    public void GivenObservationWithPrimitiveValue_WhenCheckingInstanceType_ThenReturnsLowercaseType(
        string propertyName, string expectedType, object value)
    {
        // Arrange
        // JSON serialization requires lowercase boolean values, ToLowerInvariant is intentional
#pragma warning disable CA1308 // Normalize strings to uppercase
        var observationJson = $$"""
        {
          "resourceType": "Observation",
          "id": "obs",
          "status": "final",
          "code": { "text": "test" },
          "{{propertyName}}": {{(value is string s ? $"\"{s}\"" : value.ToString()?.ToLowerInvariant())}}
        }
        """;
#pragma warning restore CA1308 // Normalize strings to uppercase

        var resource = ResourceJsonNode.Parse(observationJson);
        var typedElement = resource.ToElement(_r4Provider);

        // Act
        var valueChildren = typedElement.Children("value").ToList();

        // Assert
        Assert.Single(valueChildren);
        Assert.Equal(expectedType, valueChildren[0].InstanceType);
    }

    /// <summary>
    /// Test that complex type names remain capitalized.
    /// </summary>
    [Theory]
    [InlineData("valueQuantity", "Quantity")]
    [InlineData("valueCodeableConcept", "CodeableConcept")]
    [InlineData("valuePeriod", "Period")]
    [InlineData("valueRange", "Range")]
    [InlineData("valueRatio", "Ratio")]
    [InlineData("valueSampledData", "SampledData")]
    public void GivenObservationWithComplexValue_WhenCheckingInstanceType_ThenReturnsCapitalizedType(
        string propertyName, string expectedType)
    {
        // Arrange - using minimal valid complex type
        var observationJson = $$"""
        {
          "resourceType": "Observation",
          "id": "obs",
          "status": "final",
          "code": { "text": "test" },
          "{{propertyName}}": {}
        }
        """;

        var resource = ResourceJsonNode.Parse(observationJson);
        var typedElement = resource.ToElement(_r4Provider);

        // Act
        var valueChildren = typedElement.Children("value").ToList();

        // Assert
        Assert.Single(valueChildren);
        Assert.Equal(expectedType, valueChildren[0].InstanceType);
    }

    #endregion

    #region Multiple Choice Elements Tests

    /// <summary>
    /// Test that multiple choice elements in a collection can be navigated correctly.
    /// Example: Observation.component can have different value[x] types.
    /// </summary>
    [Fact]
    public void GivenObservationWithMultipleComponentValues_WhenNavigatingToComponentValue_ThenReturnsAllValues()
    {
        // Arrange
        var observationJson = """
        {
          "resourceType": "Observation",
          "id": "obs",
          "status": "final",
          "code": { "text": "test" },
          "component": [
            {
              "code": { "text": "comp1" },
              "valueString": "text"
            },
            {
              "code": { "text": "comp2" },
              "valueInteger": 10
            },
            {
              "code": { "text": "comp3" },
              "valueQuantity": { "value": 5, "unit": "mg" }
            }
          ]
        }
        """;

        var resource = ResourceJsonNode.Parse(observationJson);
        var typedElement = resource.ToElement(_r4Provider);

        // Act
        var components = typedElement.Children("component").ToList();
        var componentValues = components.SelectMany(c => c.Children("value")).ToList();

        // Assert
        Assert.Equal(3, components.Count);
        Assert.Equal(3, componentValues.Count);

        // Check types are correct
        Assert.Equal("string", componentValues[0].InstanceType);
        Assert.Equal("integer", componentValues[1].InstanceType);
        Assert.Equal("Quantity", componentValues[2].InstanceType);

        // Check values
        Assert.Equal("text", componentValues[0].Value);
        Assert.Equal(10, componentValues[1].Value);
    }

    #endregion

    #region Non-Choice Element Tests

    /// <summary>
    /// Test that non-choice elements work correctly (sanity check).
    /// </summary>
    [Fact]
    public void GivenObservation_WhenNavigatingToStatus_ThenReturnsStatusElement()
    {
        // Arrange
        var observationJson = """
        {
          "resourceType": "Observation",
          "id": "obs",
          "status": "final",
          "code": { "text": "test" }
        }
        """;

        var resource = ResourceJsonNode.Parse(observationJson);
        var typedElement = resource.ToElement(_r4Provider);

        // Act
        var statusChildren = typedElement.Children("status").ToList();

        // Assert
        Assert.Single(statusChildren);
        Assert.Equal("status", statusChildren[0].Name);
        Assert.Equal("code", statusChildren[0].InstanceType);
        Assert.Equal("final", statusChildren[0].Value);
    }

    /// <summary>
    /// Test that navigating to complex non-choice elements works.
    /// </summary>
    [Fact]
    public void GivenObservation_WhenNavigatingToCode_ThenReturnsCodeableConceptElement()
    {
        // Arrange
        var observationJson = """
        {
          "resourceType": "Observation",
          "id": "obs",
          "status": "final",
          "code": {
            "coding": [{
              "system": "http://loinc.org",
              "code": "15074-8",
              "display": "Glucose"
            }]
          }
        }
        """;

        var resource = ResourceJsonNode.Parse(observationJson);
        var typedElement = resource.ToElement(_r4Provider);

        // Act
        var codeChildren = typedElement.Children("code").ToList();

        // Assert
        Assert.Single(codeChildren);
        Assert.Equal("code", codeChildren[0].Name);
        Assert.Equal("CodeableConcept", codeChildren[0].InstanceType);
    }

    /// <summary>
    /// Test that navigating to 'effective' matches 'effectiveDateTime' element.
    /// This is the specific case causing date search indexing failures.
    /// </summary>
    [Fact]
    public void GivenObservationWithEffectiveDateTime_WhenNavigatingToEffective_ThenReturnsEffectiveDateTimeElement()
    {
        // Arrange
        var observationJson = """
        {
          "resourceType": "Observation",
          "id": "obs-date",
          "status": "final",
          "code": { "text": "test" },
          "effectiveDateTime": "1980-05-11"
        }
        """;

        var resource = ResourceJsonNode.Parse(observationJson);
        var typedElement = resource.ToElement(_r4Provider);

        // Act
        var effectiveChildren = typedElement.Children("effective").ToList();

        // Assert
        Assert.Single(effectiveChildren);
        Assert.Equal("effectiveDateTime", effectiveChildren[0].Name);
        Assert.Equal("dateTime", effectiveChildren[0].InstanceType);
        var temporalValue = Assert.IsType<FhirTemporal>(effectiveChildren[0].Value);
        Assert.Equal("1980-05-11", temporalValue.Literal);
    }

    /// <summary>
    /// Test that navigating to 'effective' matches 'effectivePeriod' element.
    /// </summary>
    [Fact]
    public void GivenObservationWithEffectivePeriod_WhenNavigatingToEffective_ThenReturnsEffectivePeriodElement()
    {
        // Arrange
        var observationJson = """
        {
          "resourceType": "Observation",
          "id": "obs-period",
          "status": "final",
          "code": { "text": "test" },
          "effectivePeriod": {
            "start": "1980-05-16",
            "end": "1980-05-17"
          }
        }
        """;

        var resource = ResourceJsonNode.Parse(observationJson);
        var typedElement = resource.ToElement(_r4Provider);

        // Act
        var effectiveChildren = typedElement.Children("effective").ToList();

        // Assert
        Assert.Single(effectiveChildren);
        Assert.Equal("effectivePeriod", effectiveChildren[0].Name);
        Assert.Equal("Period", effectiveChildren[0].InstanceType);
    }

    #endregion

    #region Edge Cases

    /// <summary>
    /// Test that navigating with null/empty name returns all children.
    /// </summary>
    [Fact]
    public void GivenObservation_WhenNavigatingWithNullName_ThenReturnsAllChildren()
    {
        // Arrange
        var observationJson = """
        {
          "resourceType": "Observation",
          "id": "obs",
          "status": "final",
          "code": { "text": "test" },
          "valueString": "foo"
        }
        """;

        var resource = ResourceJsonNode.Parse(observationJson);
        var typedElement = resource.ToElement(_r4Provider);

        // Act
        var allChildren = typedElement.Children().ToList();

        // Assert
        Assert.True(allChildren.Count >= 4); // id, status, code, valueString at minimum
        Assert.Contains(allChildren, c => c.Name == "id");
        Assert.Contains(allChildren, c => c.Name == "status");
        Assert.Contains(allChildren, c => c.Name == "code");
        Assert.Contains(allChildren, c => c.Name == "valueString");
    }

    /// <summary>
    /// Test that navigating to non-existent element returns empty.
    /// </summary>
    [Fact]
    public void GivenObservation_WhenNavigatingToNonExistentElement_ThenReturnsEmpty()
    {
        // Arrange
        var observationJson = """
        {
          "resourceType": "Observation",
          "id": "obs",
          "status": "final",
          "code": { "text": "test" }
        }
        """;

        var resource = ResourceJsonNode.Parse(observationJson);
        var typedElement = resource.ToElement(_r4Provider);

        // Act
        var nonExistentChildren = typedElement.Children("nonExistent").ToList();

        // Assert
        Assert.Empty(nonExistentChildren);
    }

    #endregion

    #region Primitive Extensions Tests

    /// <summary>
    /// Test that null values in arrays are preserved when they have shadow extensions.
    /// This is critical for FHIRPath expressions like Patient.name.given.select($this.hasValue())
    /// which need to return results for both null and non-null array elements.
    /// </summary>
    [Fact]
    public void GivenPatientWithNullGivenNameAndExtension_WhenNavigatingToGiven_ThenReturnsBothElements()
    {
        // Arrange - FHIR allows null values in arrays when there's extension data
        var patientJson = """
        {
          "resourceType": "Patient",
          "id": "pat1",
          "name": [{
            "given": [null, "James"],
            "_given": [
              {"extension": [{"url": "http://example.org/ext", "valueString": "five"}]},
              null
            ]
          }]
        }
        """;

        var resource = ResourceJsonNode.Parse(patientJson);
        var typedElement = resource.ToElement(_r4Provider);

        // Act
        var names = typedElement.Children("name").ToList();
        var givenNames = names.SelectMany(n => n.Children("given")).ToList();

        // Assert - both elements should be present (null with extension, and "James")
        Assert.Equal(2, givenNames.Count);

        // First element has no value but has extension
        Assert.Null(givenNames[0].Value);

        // Second element has value "James"
        Assert.Equal("James", givenNames[1].Value);
    }

    /// <summary>
    /// Test that extension data is accessible on null-valued primitive array elements.
    /// </summary>
    [Fact]
    public void GivenPatientWithNullGivenNameAndExtension_WhenAccessingExtension_ThenExtensionIsAvailable()
    {
        // Arrange
        var patientJson = """
        {
          "resourceType": "Patient",
          "id": "pat1",
          "name": [{
            "given": [null, "James"],
            "_given": [
              {"extension": [{"url": "http://example.org/ext", "valueString": "five"}]},
              null
            ]
          }]
        }
        """;

        var resource = ResourceJsonNode.Parse(patientJson);
        var typedElement = resource.ToElement(_r4Provider);

        // Act
        var givenNames = typedElement.Children("name")
            .SelectMany(n => n.Children("given"))
            .ToList();

        // Get extensions from first given name (the null one with extension)
        var firstGivenExtensions = givenNames[0].Children("extension").ToList();

        // Assert
        Assert.Single(firstGivenExtensions);
        var extensionUrls = firstGivenExtensions[0].Children("url").ToList();
        Assert.Single(extensionUrls);
        Assert.Equal("http://example.org/ext", extensionUrls[0].Value);
    }

    #endregion

    #region Shadow-Only Element Tests (Issue #216)

    [Fact]
    public void GivenPatientWithShadowBirthDateOnly_WhenNavigatingToExtension_ThenChildrenHaveCorrectTypes()
    {
        // Arrange - _birthDate WITHOUT birthDate
        var patientJson = """
        {
          "resourceType": "Patient",
          "id": "example",
          "_birthDate": {
            "extension": [{
              "url": "http://hl7.org/fhir/StructureDefinition/patient-birthTime",
              "valueDateTime": "2000-01-01T01:01:01-01:00"
            }]
          }
        }
        """;

        var resource = ResourceJsonNode.Parse(patientJson);
        var typedElement = resource.ToElement(_r4Provider);

        // Act
        var birthDateList = typedElement.Children("birthDate");

        // Assert - the shadow element should be accessible as "birthDate"
        Assert.Single(birthDateList);
        var birthDate = birthDateList[0];

        var extensions = birthDate.Children("extension").ToList();
        Assert.Single(extensions);
        Assert.Equal("Extension", extensions[0].InstanceType);

        var urls = extensions[0].Children("url");
        Assert.Single(urls);
        Assert.NotEmpty(urls[0].InstanceType);
        Assert.Equal("http://hl7.org/fhir/StructureDefinition/patient-birthTime", urls[0].Value);

        var valueDateTimes = extensions[0].Children("valueDateTime");
        Assert.Single(valueDateTimes);
        Assert.Equal("dateTime", valueDateTimes[0].InstanceType);
        var valueDateTimeValue = Assert.IsType<FhirTemporal>(valueDateTimes[0].Value);
        Assert.Equal("2000-01-01T01:01:01-01:00", valueDateTimeValue.Literal);
    }

    [Fact]
    public void GivenPatientWithBothBirthDateAndShadow_WhenNavigating_ThenChildrenHaveCorrectTypes()
    {
        // Arrange - both birthDate AND _birthDate (working case for comparison)
        var patientJson = """
        {
          "resourceType": "Patient",
          "id": "example",
          "birthDate": "2010-05-07",
          "_birthDate": {
            "extension": [{
              "url": "http://hl7.org/fhir/StructureDefinition/patient-birthTime",
              "valueDateTime": "2010-05-07T01:01:01-01:00"
            }]
          }
        }
        """;

        var resource = ResourceJsonNode.Parse(patientJson);
        var typedElement = resource.ToElement(_r4Provider);

        // Act
        var birthDateList = typedElement.Children("birthDate");

        // Assert
        Assert.Single(birthDateList);
        var birthDate = birthDateList[0];
        var birthDateTemporal = Assert.IsType<FhirTemporal>(birthDate.Value);
        Assert.Equal("2010-05-07", birthDateTemporal.Literal);

        var extensions = birthDate.Children("extension").ToList();
        Assert.Single(extensions);
        Assert.Equal("Extension", extensions[0].InstanceType);

        var valueDateTimes = extensions[0].Children("valueDateTime");
        Assert.Single(valueDateTimes);
        Assert.Equal("dateTime", valueDateTimes[0].InstanceType);
    }

    #endregion

    #region Temporal Value Tests

    [Theory]
    [InlineData("birthDate", "date", "1974-12-25", FhirTemporalPrecision.Day)]
    [InlineData("birthDate", "date", "1974", FhirTemporalPrecision.Year)]
    public void GivenPatientWithDateField_WhenReadingValue_ThenReturnsFhirTemporal(
        string field, string expectedInstanceType, string literal, FhirTemporalPrecision expectedPrecision)
    {
        // Arrange
        var patientJson = $$"""
        {
          "resourceType": "Patient",
          "id": "p1",
          "{{field}}": "{{literal}}"
        }
        """;

        var resource = ResourceJsonNode.Parse(patientJson);
        var typedElement = resource.ToElement(_r4Provider);

        // Act
        var children = typedElement.Children(field).ToList();

        // Assert
        Assert.Single(children);
        Assert.Equal(expectedInstanceType, children[0].InstanceType);
        var temporal = Assert.IsType<FhirTemporal>(children[0].Value);
        Assert.Equal(literal, temporal.Literal);
        Assert.Equal(expectedPrecision, temporal.Precision);
    }

    [Fact]
    public void GivenObservationWithEffectiveDateTime_WhenReadingValue_ThenReturnsFhirTemporal()
    {
        // Arrange
        var observationJson = """
        {
          "resourceType": "Observation",
          "id": "obs",
          "status": "final",
          "code": { "text": "test" },
          "effectiveDateTime": "1974-12-25T14:30:00Z"
        }
        """;

        var resource = ResourceJsonNode.Parse(observationJson);
        var typedElement = resource.ToElement(_r4Provider);

        // Act
        var effectiveChildren = typedElement.Children("effective").ToList();

        // Assert
        Assert.Single(effectiveChildren);
        Assert.Equal("dateTime", effectiveChildren[0].InstanceType);
        var temporal = Assert.IsType<FhirTemporal>(effectiveChildren[0].Value);
        Assert.Equal("1974-12-25T14:30:00Z", temporal.Literal);
        Assert.Equal(FhirTemporalPrecision.Second, temporal.Precision);
    }

    [Fact]
    public void GivenObservationWithInstant_WhenReadingValue_ThenReturnsFhirTemporal()
    {
        // Arrange
        var observationJson = """
        {
          "resourceType": "Observation",
          "id": "obs",
          "status": "final",
          "code": { "text": "test" },
          "issued": "1974-12-25T14:30:00.123Z"
        }
        """;

        var resource = ResourceJsonNode.Parse(observationJson);
        var typedElement = resource.ToElement(_r4Provider);

        // Act
        var issuedChildren = typedElement.Children("issued").ToList();

        // Assert
        Assert.Single(issuedChildren);
        Assert.Equal("instant", issuedChildren[0].InstanceType);
        var temporal = Assert.IsType<FhirTemporal>(issuedChildren[0].Value);
        Assert.Equal("1974-12-25T14:30:00.123Z", temporal.Literal);
        Assert.Equal(FhirTemporalPrecision.Millisecond, temporal.Precision);
    }

    [Fact]
    public void GivenUnparseableTemporalLiteral_WhenReadingValue_ThenFallsBackToRawString()
    {
        // Arrange — hour-precision dateTime deliberately fails FhirTemporal.TryParse
        // (GetPrecision returns Hour, which TryParseTemporal cannot handle via DateTimeOffset.TryParse
        // for a date-prefixed hour-only string — the round-trip through GetLowerBound returns null)
        var observationJson = """
        {
          "resourceType": "Observation",
          "id": "obs",
          "status": "final",
          "code": { "text": "test" },
          "effectiveDateTime": "2012-01-01T10"
        }
        """;

        var resource = ResourceJsonNode.Parse(observationJson);
        var typedElement = resource.ToElement(_r4Provider);

        // Act
        var effectiveChildren = typedElement.Children("effective").ToList();

        // Assert — raw string fallback, no FhirTemporal, no null, no exception
        Assert.Single(effectiveChildren);
        Assert.Equal("dateTime", effectiveChildren[0].InstanceType);
        Assert.Equal("2012-01-01T10", effectiveChildren[0].Value);
    }

    [Fact]
    public void GivenHourPrecisionTemporalLiteral_WhenReadingValue_ThenFallsBackToRawString()
    {
        // Arrange — "2012-01-01T10" is the canonical example from the task brief
        var patientJson = """
        {
          "resourceType": "Patient",
          "id": "p1",
          "birthDate": "2012-01-01T10"
        }
        """;

        var resource = ResourceJsonNode.Parse(patientJson);
        var typedElement = resource.ToElement(_r4Provider);

        // Act
        var children = typedElement.Children("birthDate").ToList();

        // Assert
        Assert.Single(children);
        Assert.Equal("date", children[0].InstanceType);
        Assert.Equal("2012-01-01T10", children[0].Value);
    }

    /// <summary>
    /// Exercises the <c>time</c> arm of <see cref="FhirTemporal"/>.
    /// A bare FHIR <c>time</c> is anchored internally to a synthetic 1900-01-01 date and is not a
    /// determinate calendar instant, so the anchor must stay invisible: <see cref="FhirTemporal.Literal"/>
    /// must round-trip the source text and <see cref="FhirTemporal.Kind"/> must be
    /// <see cref="FhirPrimitive.Time"/>.
    /// Field used: <c>HealthcareService.availableTime.availableStartTime</c> (typed as <c>time</c>
    /// in R4 and therefore a clean, shallow path that the schema provider resolves without fabricating
    /// wrapper elements).
    /// </summary>
    [Fact]
    public void GivenHealthcareServiceWithAvailableStartTime_WhenReadingValue_ThenReturnsFhirTemporalWithNullValue()
    {
        // Arrange
        var healthcareServiceJson = """
        {
          "resourceType": "HealthcareService",
          "id": "hs1",
          "availableTime": [
            {
              "availableStartTime": "09:00:00"
            }
          ]
        }
        """;

        var resource = ResourceJsonNode.Parse(healthcareServiceJson);
        var typedElement = resource.ToElement(_r4Provider);

        // Act
        var availableTimeChildren = typedElement.Children("availableTime").ToList();
        var startTimeChildren = availableTimeChildren[0].Children("availableStartTime").ToList();

        // Assert
        Assert.Single(startTimeChildren);
        Assert.Equal("time", startTimeChildren[0].InstanceType);
        var temporal = Assert.IsType<FhirTemporal>(startTimeChildren[0].Value);
        Assert.Equal(FhirPrimitive.Time, temporal.Kind);
        Assert.Equal("09:00:00", temporal.Literal);
        Assert.Equal(FhirTemporalPrecision.Second, temporal.Precision);
    }

    #endregion

    #region Value Memoisation Tests

    [Fact]
    public void GivenATemporalElement_WhenReadingValueTwice_ThenTheParsedValueIsMemoised()
    {
        // Arrange
        var element = ParseObservation("\"effectiveDateTime\": \"2013-04-02T10:30:10+01:00\"")
            .Children("effective")
            .Single();

        // Act
        var first = element.Value;
        var second = element.Value;

        // Assert
        Assert.IsType<FhirTemporal>(first);
        Assert.Same(first, second);
    }

    [Fact]
    public void GivenAnElementWithoutPrimitiveValue_WhenReadingValueTwice_ThenTheSourceIsReadOnlyOnce()
    {
        // Arrange - a recording navigator observes ISourceNavigator.Text reads directly, so removing
        // the memoisation of Value (both reads recomputing null independently) would still leave a
        // plain Assert.Null(first)/Assert.Null(second) test green. Asserting the read count instead
        // makes that regression fail.
        var resource = ResourceJsonNode.Parse($$"""
        {
          "resourceType": "Observation",
          "id": "obs1",
          "status": "final",
          "code": { "text": "test" },
          "valueString": "foo"
        }
        """);
        var recordingNavigator = new TextAccessRecordingSourceNavigator(resource.ToSourceNavigator());
        var element = recordingNavigator.ToElement(_r4Provider)
            .Children("code")
            .Single();

        // Act
        var first = element.Value;
        var second = element.Value;

        // Assert
        Assert.Null(first);
        Assert.Null(second);
        Assert.Equal(1, recordingNavigator.TextReadCount(element.Location));
    }

    [Fact]
    public void GivenAnUnparseableTemporal_WhenReadingValueTwice_ThenBothReadsFallBackToTheRawString()
    {
        // Arrange
        var element = ParseObservation("\"effectiveDateTime\": \"not-a-date\"")
            .Children("effective")
            .Single();

        // Act
        var first = element.Value;
        var second = element.Value;

        // Assert
        Assert.Equal("not-a-date", first);
        Assert.Same(first, second);
    }

    [Fact]
    public void GivenASharedElement_WhenReadingValueConcurrently_ThenEveryReaderSeesTheSameValue()
    {
        // Arrange
        var element = ParseObservation("\"effectiveDateTime\": \"2013-04-02T10:30:10+01:00\"")
            .Children("effective")
            .Single();

        var expected = FhirTemporal.TryParse("2013-04-02T10:30:10+01:00", FhirPrimitive.DateTime, out var parsed)
            ? parsed
            : null;

        // Act
        var observed = new object[256];
        Parallel.For(0, observed.Length, i => observed[i] = element.Value);

        // Assert
        Assert.NotNull(expected);
        Assert.All(observed, value => Assert.Equal(expected, value));
    }

    private IElement ParseObservation(string discriminatingProperty)
    {
        var observationJson = $$"""
        {
          "resourceType": "Observation",
          "id": "obs1",
          "status": "final",
          "code": { "text": "test" },
          {{discriminatingProperty}}
        }
        """;

        return ResourceJsonNode.Parse(observationJson).ToElement(_r4Provider);
    }

    #endregion

    #region Snapshot Invariant Tests

    // Memoising Value is only sound because an element is a snapshot of its source node. These tests pin
    // that invariant at the layer that actually supplies it, so a future live-reading ISourceNavigator
    // cannot make Value silently stale without turning a test red.

    [Fact]
    public void GivenARetainedSourceNavigator_WhenTheUnderlyingJsonIsReplaced_ThenTextStillReportsTheSnapshot()
    {
        // Arrange - ISourceNavigator.Text has never been memoised, at any point in this file's history.
        var resource = ResourceJsonNode.Parse(PatientJson);
        var gender = resource.ToSourceNavigator().Children("gender").Single();
        var textBeforeMutation = gender.Text;

        // Act
        MutableNodeOf(resource)["gender"] = "male";

        // Assert - the capture is frozen by JsonNodeSourceNode, not by any cache above it.
        Assert.Equal("female", textBeforeMutation);
        Assert.Equal("female", gender.Text);
        Assert.Equal("male", MutableNodeOf(resource)["gender"]!.GetValue<string>());
    }

    [Fact]
    public void GivenARetainedElement_WhenTheUnderlyingJsonIsReplaced_ThenValueStillReportsTheSnapshot()
    {
        // Arrange
        var resource = ResourceJsonNode.Parse(PatientJson);
        var gender = resource.ToElement(_r4Provider).Children("gender").Single();
        var valueBeforeMutation = gender.Value;

        // Act
        MutableNodeOf(resource)["gender"] = "male";

        // Assert
        Assert.Equal("female", valueBeforeMutation);
        Assert.Equal("female", gender.Value);
    }

    [Fact]
    public void GivenAMutatedResource_WhenCachesAreInvalidatedAndTheTreeIsRebuilt_ThenTheNewValueIsVisible()
    {
        // Arrange
        var resource = ResourceJsonNode.Parse(PatientJson);
        Assert.Equal("female", resource.ToElement(_r4Provider).Children("gender").Single().Value);

        // Act - the supported way to observe an edit: re-derive rather than re-read.
        MutableNodeOf(resource)["gender"] = "male";
        resource.InvalidateCaches();

        // Assert
        Assert.Equal("male", resource.ToElement(_r4Provider).Children("gender").Single().Value);
    }

    [Fact]
    public void GivenAMutatedResource_WhenTheTreeIsRebuiltWithoutInvalidatingCaches_ThenTheOldValuePersists()
    {
        // Arrange
        var resource = ResourceJsonNode.Parse(PatientJson);
        Assert.Equal("female", resource.ToElement(_r4Provider).Children("gender").Single().Value);

        // Act - ToElement() returns the cached tree, so re-deriving without invalidating changes nothing.
        MutableNodeOf(resource)["gender"] = "male";

        // Assert - this is why ResourceJsonNode.InvalidateCaches() exists.
        Assert.Equal("female", resource.ToElement(_r4Provider).Children("gender").Single().Value);
    }

    private const string PatientJson = """
    {
      "resourceType": "Patient",
      "id": "pat1",
      "gender": "female"
    }
    """;

    private static System.Text.Json.Nodes.JsonObject MutableNodeOf(ResourceJsonNode resource)
        => ((IMutableJsonNode)resource).MutableNode;

    #endregion

    #region Type Definition Memoisation Tests

    [Fact]
    public void GivenAnElementWhoseTypeIsUnknownToTheSchema_WhenReadingTypeTwice_ThenBothReadsAreNull()
    {
        // Arrange
        var element = ParseObservation("\"Bogus\": \"x\"")
            .Children("Bogus")
            .Single();

        // Act
        var first = element.Type;
        var second = element.Type;

        // Assert
        Assert.Equal("Bogus", element.InstanceType);
        Assert.Null(first);
        Assert.Null(second);
    }

    [Fact]
    public void GivenAnElementWithNoInstanceType_WhenReadingTypeTwice_ThenBothReadsAreNullWithoutQueryingTheSchema()
    {
        // Arrange
        var schema = new TypeLookupRecordingSchema(_r4Provider);
        var element = ParseObservation("\"bogus\": \"x\"", schema)
            .Children("bogus")
            .Single();
        schema.ResetCounts();

        // Act
        var first = element.Type;
        var second = element.Type;

        // Assert
        Assert.Equal(string.Empty, element.InstanceType);
        Assert.Null(first);
        Assert.Null(second);
        Assert.Equal(0, schema.TotalLookupCount);
    }

    [Fact]
    public void GivenAnElement_WhenReadingTypeRepeatedly_ThenTheSchemaIsQueriedOnce()
    {
        // Arrange
        var schema = new TypeLookupRecordingSchema(_r4Provider);
        var element = ParseObservation("\"valueString\": \"foo\"", schema);

        // Act
        var reads = new[] { element.Type, element.Type, element.Type, element.Type };

        // Assert
        Assert.All(reads, type => Assert.Same(reads[0], type));
        Assert.NotNull(reads[0]);
        Assert.Equal(1, schema.LookupCount("Observation"));
    }

    [Fact]
    public void GivenAnUnknownType_WhenReadingTypeRepeatedly_ThenTheNullResultIsMemoisedToo()
    {
        // Arrange
        var schema = new TypeLookupRecordingSchema(_r4Provider);
        var element = ParseObservation("\"Bogus\": \"x\"", schema)
            .Children("Bogus")
            .Single();
        schema.ResetCounts();

        // Act
        var reads = new[] { element.Type, element.Type, element.Type, element.Type };

        // Assert
        Assert.All(reads, Assert.Null);
        Assert.Equal(1, schema.LookupCount("Bogus"));
    }

    [Fact]
    public void GivenASharedElement_WhenReadingTypeConcurrently_ThenEveryReaderSeesTheSameDefinition()
    {
        // Arrange
        var element = ParseObservation("\"valueString\": \"foo\"");
        var expected = element.Type;

        // Act
        var observed = new IType[256];
        Parallel.For(0, observed.Length, i => observed[i] = element.Type);

        // Assert
        Assert.NotNull(expected);
        Assert.All(observed, type => Assert.Same(expected, type));
    }

    #endregion

    #region Child Definition Cache Tests

    [Fact]
    public void GivenAnUndefinedChildName_WhenNavigatingTwice_ThenBothPassesReportNoDefinition()
    {
        // Arrange
        var element = ParseObservation("\"Bogus\": \"x\"");

        // Act
        var first = element.Children("Bogus").Single();
        var second = element.Children("Bogus").Single();

        // Assert
        Assert.Null(first.Type);
        Assert.Null(second.Type);
    }

    [Fact]
    public void GivenTheSameElement_WhenNavigatingTheSameChildTwice_ThenTheSecondPassQueriesTheSchemaLessOften()
    {
        // Arrange
        var schema = new TypeLookupRecordingSchema(_r4Provider);
        var element = ParseObservation("\"valueString\": \"foo\"", schema);
        _ = element.Type;

        // Act
        schema.ResetCounts();
        var first = element.Children("status").Single();
        var firstPassLookups = schema.TotalLookupCount;

        schema.ResetCounts();
        var second = element.Children("status").Single();
        var secondPassLookups = schema.TotalLookupCount;

        // Assert
        Assert.Equal(first.InstanceType, second.InstanceType);
        Assert.Same(first.Type, second.Type);
        Assert.True(
            secondPassLookups < firstPassLookups,
            $"expected the cached pass to query the schema less often, got {secondPassLookups} vs {firstPassLookups}");
    }

    [Fact]
    public void GivenALeafElement_WhenNavigatingIntoIt_ThenTheChildDefinitionCacheIsNeverReached()
    {
        // Arrange
        var schema = new TypeLookupRecordingSchema(_r4Provider);
        var leaf = ParseObservation("\"valueString\": \"foo\"", schema)
            .Children("status")
            .Single();
        schema.ResetCounts();

        // Act
        var children = leaf.Children("anything");

        // Assert
        Assert.Empty(children);
        Assert.Equal(0, schema.LookupCount("code.anything"));
    }

    [Fact]
    public void GivenASharedElement_WhenNavigatingChildrenConcurrently_ThenEveryCallerSeesTheSameChildDefinition()
    {
        // Arrange
        var element = ParseObservation("\"valueString\": \"foo\"");
        var expected = element.Children("status").Single().Type;

        // Act
        var observed = new IType[256];
        Parallel.For(0, observed.Length, i => observed[i] = element.Children("status").Single().Type);

        // Assert
        Assert.NotNull(expected);
        Assert.All(observed, type => Assert.Same(expected, type));
    }

    private IElement ParseObservation(string discriminatingProperty, ISchema schema)
    {
        var observationJson = $$"""
        {
          "resourceType": "Observation",
          "id": "obs1",
          "status": "final",
          "code": { "text": "test" },
          {{discriminatingProperty}}
        }
        """;

        return ResourceJsonNode.Parse(observationJson).ToElement(schema);
    }

    #endregion
}
