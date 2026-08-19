/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Regression tests for aggregate functions composing with the operations that dispatch on a typed
 * temporal value, over a parsed resource rather than a hand-built element.
 */

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;

namespace Ignixa.FhirPath.Tests.Evaluation;

/// <summary>
/// Pins that min()/max() hand back the element they selected, so the next operator in the chain
/// still sees a typed temporal.
/// </summary>
/// <remarks>
/// <para>
/// min() and max() select an item; they do not construct one. The temporal branch used to rebuild the
/// winner as a PrimitiveElement over its wire literal, which turned a resource-backed
/// <see cref="FhirTemporal"/> back into a string. Nothing caught it: the aggregate's own tests
/// asserted the rendered text, and text is exactly what a FhirTemporal and its wire literal have in
/// common.
/// </para>
/// <para>
/// The bug only bites on collections of two or more - Min/Max short-circuit a single-element
/// collection by returning list[0] untouched - so every test here uses a repeating element, and the
/// subject is parsed rather than hand-built so the typed value comes from the schema-aware parser
/// instead of from a test author's choice.
/// </para>
/// </remarks>
public class ResourceBackedAggregateCompositionTests
{
    private static readonly IFhirSchemaProvider Schema = FhirVersion.R5.GetSchemaProvider();

    private const string PatientJson = """
    {
      "resourceType": "Patient",
      "id": "p1",
      "contact": [
        { "period": { "start": "2020-06-15T00:00:00Z", "end": "2020-12-31T00:00:00Z" } },
        { "period": { "start": "2019-01-01T00:00:00Z", "end": "2019-12-31T00:00:00Z" } },
        { "period": { "start": "2021-12-31T00:00:00Z", "end": "2022-12-31T00:00:00Z" } }
      ]
    }
    """;

    [Fact]
    public void GivenARepeatingResourceBackedDateTime_WhenMin_ThenReturnsTheTypedElement()
    {
        // Arrange
        var patient = Parse(PatientJson);

        // Act
        var result = patient.Select("Patient.contact.period.start.min()").Single();

        // Assert
        result.InstanceType.ShouldBe("dateTime");
        result.Value.ShouldBeOfType<FhirTemporal>().Literal.ShouldBe("2019-01-01T00:00:00Z");
    }

    [Fact]
    public void GivenARepeatingResourceBackedDateTime_WhenMax_ThenReturnsTheTypedElement()
    {
        // Arrange
        var patient = Parse(PatientJson);

        // Act
        var result = patient.Select("Patient.contact.period.end.max()").Single();

        // Assert
        result.InstanceType.ShouldBe("dateTime");
        result.Value.ShouldBeOfType<FhirTemporal>().Literal.ShouldBe("2022-12-31T00:00:00Z");
    }

    [Fact]
    public void GivenMinOverARepeatingResourceBackedDateTime_WhenAddingCalendarYear_ThenShiftsTheDate()
    {
        // The composition that the de-typing broke: a string-valued min() result falls through to
        // the '+' operator's string branch instead of the temporal arithmetic branch.

        // Arrange
        var patient = Parse(PatientJson);

        // Act
        var result = patient.Select("Patient.contact.period.start.min() + 1 year").Single();

        // Assert
        result.InstanceType.ShouldBe("dateTime");
        result.Value?.ToString().ShouldStartWith("2020-01-01");
    }

    [Fact]
    public void GivenMaxOverARepeatingResourceBackedDateTime_WhenSubtractingCalendarYear_ThenShiftsTheDate()
    {
        // Arrange
        var patient = Parse(PatientJson);

        // Act
        var result = patient.Select("Patient.contact.period.end.max() - 1 year").Single();

        // Assert
        result.InstanceType.ShouldBe("dateTime");
        result.Value?.ToString().ShouldStartWith("2021-12-31");
    }

    [Fact]
    public void GivenMinOverARepeatingResourceBackedDateTime_WhenAddingUcumYear_ThenSignalsError()
    {
        // The de-typed string path silently concatenated instead of signalling. FHIRPath 3.0
        // "Date/Time Arithmetic" requires an error for UCUM 'a' on a DateTime, so reaching the
        // temporal arithmetic branch at all is what this pins.

        // Arrange
        var patient = Parse(PatientJson);

        // Act
        var evaluate = () => patient.Select("Patient.contact.period.start.min() + 1 'a'").ToList();

        // Assert
        evaluate.ShouldThrow<FhirPathEvaluationException>();
    }

    [Fact]
    public void GivenMinOverARepeatingResourceBackedDateTime_WhenAskedItsType_ThenReportsTheFhirTypeName()
    {
        // The de-typing's one observable wrong answer rather than merely-untidy shape: with the
        // winner rebuilt over a wire string, type() had no typed value left to inspect and inferred
        // the CLR name, answering "DateTime" where FHIRPath requires the FHIR type name "dateTime".
        // Casing is not cosmetic here - type().name feeds string comparisons in profiles and
        // invariants.

        // Arrange
        var patient = Parse(PatientJson);

        // Act
        var result = patient.Select("Patient.contact.period.start.min().type().name").Single();

        // Assert
        result.Value.ShouldBe("dateTime");
    }

    [Fact]
    public void GivenMinOverARepeatingResourceBackedDateTime_WhenComparedToALiteral_ThenOrders()
    {
        // Arrange
        var patient = Parse(PatientJson);

        // Act
        var result = patient.Select("Patient.contact.period.start.min() < @2020-01-01T00:00:00Z").Single();

        // Assert
        result.Value.ShouldBe(true);
    }

    [Fact]
    public void GivenMinOverARepeatingResourceBackedDateTime_WhenTakingLowBoundary_ThenReturnsAPrecisionBound()
    {
        // Arrange
        var patient = Parse(PatientJson);

        // Act
        var result = patient.Select("Patient.contact.period.start.min().lowBoundary()").Single();

        // Assert
        result.Value?.ToString().ShouldStartWith("2019-01-01T00:00:00");
    }

    private static IElement Parse(string json) => ResourceJsonNode.Parse(json).ToElement(Schema);
}
