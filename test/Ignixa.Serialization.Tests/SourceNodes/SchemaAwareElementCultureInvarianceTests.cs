/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Pins the invariant that FHIR primitive wire text is parsed independently of the host
 * culture. The FHIR wire format always uses '.' as the decimal separator and never uses
 * group separators, so a host locale must not be allowed to reinterpret it.
 */

using System.Globalization;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;
using Xunit;

namespace Ignixa.Serialization.Tests.SourceNodes;

/// <summary>
/// Regression tests for culture-sensitive parsing of FHIR primitives in <c>SchemaAwareElement</c>.
/// Before the fix, <c>decimal.TryParse</c> ran under <c>CurrentCulture</c> with the default
/// <c>NumberStyles.Number</c>, which permits group separators. On de-DE the group separator is '.',
/// so the wire value "1.5" was silently ingested as 15 — a hundredfold error on "1234.5".
///
/// These tests mutate <c>CultureInfo.CurrentCulture</c>. They are deliberately synchronous with no
/// await between the assignment and the restore, so the value cannot leak across xunit's parallel
/// worker threads (<c>CurrentCulture</c> is thread-scoped in .NET Core), and each restores in a
/// <c>finally</c> — matching the convention already used in ConversionAndStringFunctionTests and
/// DiscoveredScenarioParameterTests.
/// </summary>
public class SchemaAwareElementCultureInvarianceTests
{
    private readonly IFhirSchemaProvider _r4Provider = FhirVersion.R4.GetSchemaProvider();

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("en-US")]
    [InlineData("ar-SA")]
    public void GivenAFhirDecimal_WhenTheHostCultureVaries_ThenTheParsedValueDoesNot(string culture)
    {
        // Arrange - "1234.5" is the load-bearing case: de-DE reads '.' as a group separator, so the
        // pre-fix code parsed this as 12345.
        var observationJson = """
        {
          "resourceType": "Observation",
          "id": "obs1",
          "status": "final",
          "code": { "text": "test" },
          "valueQuantity": { "value": 1234.5, "unit": "mg" }
        }
        """;

        // Act
        var value = ValueUnderCulture(observationJson, culture, "value", "value");

        // Assert
        Assert.Equal(1234.5m, Assert.IsType<decimal>(value));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("en-US")]
    public void GivenAFhirDecimalWithASingleFractionalDigit_WhenTheHostCultureVaries_ThenTheParsedValueDoesNot(string culture)
    {
        // Arrange - the canonical corruption: de-DE yields 15, fr-FR fails to parse at all and the
        // pre-fix code fell through to returning the raw string.
        var observationJson = """
        {
          "resourceType": "Observation",
          "id": "obs1",
          "status": "final",
          "code": { "text": "test" },
          "valueQuantity": { "value": 1.5, "unit": "mg" }
        }
        """;

        // Act
        var value = ValueUnderCulture(observationJson, culture, "value", "value");

        // Assert
        Assert.Equal(1.5m, Assert.IsType<decimal>(value));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("ar-SA")]
    public void GivenANegativeFhirDecimal_WhenTheHostCultureVaries_ThenTheParsedValueDoesNot(string culture)
    {
        // Arrange - ar-SA's NegativeSign is U+061C + '-', not plain '-'.
        var observationJson = """
        {
          "resourceType": "Observation",
          "id": "obs1",
          "status": "final",
          "code": { "text": "test" },
          "valueQuantity": { "value": -1.5, "unit": "mg" }
        }
        """;

        // Act
        var value = ValueUnderCulture(observationJson, culture, "value", "value");

        // Assert
        Assert.Equal(-1.5m, Assert.IsType<decimal>(value));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("en-US")]
    [InlineData("ar-SA")]
    public void GivenAFhirInteger_WhenTheHostCultureVaries_ThenTheParsedValueDoesNot(string culture)
    {
        // Arrange
        var patientJson = """
        {
          "resourceType": "Patient",
          "id": "p1",
          "multipleBirthInteger": 1234567
        }
        """;

        // Act
        var value = ValueUnderCulture(patientJson, culture, "multipleBirth");

        // Assert
        Assert.Equal(1234567, Assert.IsType<int>(value));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("ar-SA")]
    [InlineData("en-US")]
    public void GivenANegativeFhirInteger_WhenTheHostCultureVaries_ThenTheParsedValueDoesNot(string culture)
    {
        // Arrange - ar-SA's NegativeSign is not ascii '-', so the pre-fix parse failed there and the
        // element's Value degraded from int to string.
        var patientJson = """
        {
          "resourceType": "Patient",
          "id": "p1",
          "multipleBirthInteger": -42
        }
        """;

        // Act
        var value = ValueUnderCulture(patientJson, culture, "multipleBirth");

        // Assert
        Assert.Equal(-42, Assert.IsType<int>(value));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("en-US")]
    public void GivenAFhirUnsignedInt_WhenTheHostCultureVaries_ThenTheParsedValueDoesNot(string culture)
    {
        // Arrange
        var studyJson = """
        {
          "resourceType": "ImagingStudy",
          "id": "s1",
          "status": "available",
          "subject": { "reference": "Patient/p1" },
          "numberOfSeries": 1234
        }
        """;

        // Act
        var value = ValueUnderCulture(studyJson, culture, "numberOfSeries");

        // Assert
        Assert.Equal(1234, Assert.IsType<int>(value));
    }

    [Fact]
    public void GivenAFhirDecimalInExponentNotation_WhenParsed_ThenItBecomesADecimal()
    {
        // Arrange - FHIR permits exponent notation in decimals. NumberStyles.Number (the pre-fix
        // default) rejects it outright in every culture, so these degraded to raw strings.
        var observationJson = """
        {
          "resourceType": "Observation",
          "id": "obs1",
          "status": "final",
          "code": { "text": "test" },
          "valueQuantity": { "value": 1.2e3, "unit": "mg" }
        }
        """;

        // Act
        var value = ValueUnderCulture(observationJson, "de-DE", "value", "value");

        // Assert
        Assert.Equal(1200m, Assert.IsType<decimal>(value));
    }

    private object ValueUnderCulture(string json, string culture, params string[] path)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);

            var element = ResourceJsonNode.Parse(json).ToElement(_r4Provider);
            foreach (var segment in path)
            {
                element = element.Children(segment).Single();
            }

            return element.Value;
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }
}
