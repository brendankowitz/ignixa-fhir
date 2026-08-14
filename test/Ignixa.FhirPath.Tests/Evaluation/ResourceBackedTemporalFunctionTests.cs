/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Regression tests for FHIRPath operations over temporal values that originate from a parsed
 * resource rather than from an @-literal.
 *
 * IElement.Value returns a FhirTemporal for date/dateTime/instant/time elements, while @-literals
 * are still raw strings. Every one of these expressions used to be exercised only through literals,
 * so the engine's `is string` branches were never tested against the typed value and silently
 * returned empty, returned false, or threw once the typed value shipped.
 */

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;

namespace Ignixa.FhirPath.Tests.Evaluation;

public class ResourceBackedTemporalFunctionTests
{
    private static readonly IFhirSchemaProvider Schema = FhirVersion.R5.GetSchemaProvider();

    private const string PatientJson = """
    {
      "resourceType": "Patient",
      "id": "p1",
      "birthDate": "1974-12-25",
      "deceasedDateTime": "2020-03-04T10:00:00+00:00"
    }
    """;

    private const string ObservationJson = """
    {
      "resourceType": "Observation",
      "id": "o1",
      "status": "final",
      "code": { "text": "probe" },
      "issued": "2024-03-15T10:00:00Z"
    }
    """;

    private const string TimePatientJson = """
    {
      "resourceType": "Patient",
      "id": "p2",
      "extension": [ { "url": "http://example.org/birth-time", "valueTime": "10:30:00" } ]
    }
    """;

    [Fact]
    public void GivenResourceBackedDate_WhenAddingCalendarYear_ThenReturnsShiftedDate()
    {
        // Arrange
        var patient = Parse(PatientJson);

        // Act
        var result = patient.Select("Patient.birthDate + 1 year").Single();

        // Assert
        result.Value.ShouldBe("1975-12-25");
    }

    [Fact]
    public void GivenResourceBackedDate_WhenAddingUcumYear_ThenReturnsShiftedDate()
    {
        // Arrange
        var patient = Parse(PatientJson);

        // Act
        var result = patient.Select("Patient.birthDate + 1 'a'").Single();

        // Assert
        result.Value.ShouldBe("1975-12-25");
    }

    [Fact]
    public void GivenResourceBackedDate_WhenSubtractingUcumYear_ThenReturnsShiftedDate()
    {
        // Arrange
        var patient = Parse(PatientJson);

        // Act
        var result = patient.Select("Patient.birthDate - 1 'a'").Single();

        // Assert
        result.Value.ShouldBe("1973-12-25");
    }

    [Fact]
    public void GivenResourceBackedDate_WhenSubtractingCalendarYear_ThenReturnsShiftedDate()
    {
        // Arrange
        var patient = Parse(PatientJson);

        // Act
        var result = patient.Select("Patient.birthDate - 1 year").Single();

        // Assert
        result.Value.ShouldBe("1973-12-25");
    }

    [Fact]
    public void GivenResourceBackedDateTime_WhenAddingUcumHour_ThenPreservesOffsetAndPrecision()
    {
        // Arrange
        var patient = Parse(PatientJson);

        // Act
        var result = patient.Select("Patient.deceasedDateTime + 1 'h'").Single();

        // Assert
        result.Value.ShouldBe("2020-03-04T11:00:00+00:00");
    }

    [Fact]
    public void GivenResourceBackedTime_WhenAddingUcumHour_ThenReturnsShiftedTime()
    {
        // Arrange
        var patient = Parse(TimePatientJson);

        // Act
        var result = patient.Select("Patient.extension.value + 1 'h'").Single();

        // Assert
        result.Value.ShouldBe("11:30:00");
    }

    [Fact]
    public void GivenResourceBackedDate_WhenQuantityIsTheLeftOperand_ThenStillPerformsTemporalArithmetic()
    {
        // Arrange
        var patient = Parse(PatientJson);

        // Act
        var result = patient.Select("1 year + Patient.birthDate").Single();

        // Assert
        result.Value.ShouldBe("1975-12-25");
    }

    [Fact]
    public void GivenResourceBackedDate_WhenToDate_ThenReturnsWireLiteral()
    {
        // Arrange
        var patient = Parse(PatientJson);

        // Act
        var result = patient.Select("Patient.birthDate.toDate()").Single();

        // Assert
        result.Value.ShouldBe("1974-12-25");
        result.InstanceType.ShouldBe("date");
    }

    [Fact]
    public void GivenResourceBackedDate_WhenToDateTime_ThenReturnsWireLiteral()
    {
        // Arrange
        var patient = Parse(PatientJson);

        // Act
        var result = patient.Select("Patient.birthDate.toDateTime()").Single();

        // Assert
        result.Value.ShouldBe("1974-12-25");
        result.InstanceType.ShouldBe("dateTime");
    }

    [Fact]
    public void GivenResourceBackedInstant_WhenToDateTime_ThenReturnsWireLiteral()
    {
        // Arrange
        var observation = Parse(ObservationJson);

        // Act
        var result = observation.Select("Observation.issued.toDateTime()").Single();

        // Assert
        result.Value.ShouldBe("2024-03-15T10:00:00Z");
    }

    [Fact]
    public void GivenResourceBackedTime_WhenToTime_ThenReturnsWireLiteral()
    {
        // Arrange
        var patient = Parse(TimePatientJson);

        // Act
        var result = patient.Select("Patient.extension.value.toTime()").Single();

        // Assert
        result.Value.ShouldBe("10:30:00");
        result.InstanceType.ShouldBe("time");
    }

    [Fact]
    public void GivenResourceBackedDate_WhenConvertsToDate_ThenReturnsTrue()
    {
        // Arrange
        var patient = Parse(PatientJson);

        // Act
        var result = patient.Select("Patient.birthDate.convertsToDate()").Single();

        // Assert
        result.Value.ShouldBe(true);
    }

    [Fact]
    public void GivenResourceBackedDate_WhenConvertsToDateTime_ThenReturnsTrue()
    {
        // Arrange
        var patient = Parse(PatientJson);

        // Act
        var result = patient.Select("Patient.birthDate.convertsToDateTime()").Single();

        // Assert
        result.Value.ShouldBe(true);
    }

    [Fact]
    public void GivenResourceBackedTime_WhenConvertsToTime_ThenReturnsTrue()
    {
        // Arrange
        var patient = Parse(TimePatientJson);

        // Act
        var result = patient.Select("Patient.extension.value.convertsToTime()").Single();

        // Assert
        result.Value.ShouldBe(true);
    }

    [Fact]
    public void GivenResourceBackedDate_WhenEquivalentToMatchingLiteral_ThenReturnsTrue()
    {
        // Arrange
        var patient = Parse(PatientJson);

        // Act
        var result = patient.Select("Patient.birthDate ~ @1974-12-25").Single();

        // Assert
        result.Value.ShouldBe(true);
    }

    [Fact]
    public void GivenResourceBackedTime_WhenEquivalentToMatchingLiteral_ThenReturnsTrue()
    {
        // Arrange
        var patient = Parse(TimePatientJson);

        // Act
        var result = patient.Select("Patient.extension.value ~ @T10:30:00").Single();

        // Assert
        result.Value.ShouldBe(true);
    }

    [Fact]
    public void GivenResourceBackedDate_WhenEquivalentToDifferentLiteral_ThenReturnsFalse()
    {
        // Arrange
        var patient = Parse(PatientJson);

        // Act
        var result = patient.Select("Patient.birthDate ~ @1980-01-01").Single();

        // Assert
        result.Value.ShouldBe(false);
    }

    [Fact]
    public void GivenResourceBackedDates_WhenJoined_ThenConcatenatesWireLiterals()
    {
        // Arrange
        var patient = Parse(PatientJson);

        // Act
        var result = patient.Select("(Patient.birthDate | Patient.deceasedDateTime).join(',')").Single();

        // Assert
        result.Value.ShouldBe("1974-12-25,2020-03-04T10:00:00+00:00");
    }

    [Theory]
    [InlineData("Patient.birthDate.startsWith('1974')", true)]
    [InlineData("Patient.birthDate.endsWith('25')", true)]
    [InlineData("Patient.birthDate.contains('12')", true)]
    [InlineData("Patient.birthDate.matches('^1974-')", true)]
    public void GivenResourceBackedDate_WhenBooleanStringFunction_ThenEvaluatesOverWireLiteral(string expression, bool expected)
    {
        // Arrange
        var patient = Parse(PatientJson);

        // Act
        var result = patient.Select(expression).Single();

        // Assert
        result.Value.ShouldBe(expected);
    }

    [Theory]
    [InlineData("Patient.birthDate.substring(0,4)", "1974")]
    [InlineData("Patient.birthDate.upper()", "1974-12-25")]
    [InlineData("Patient.birthDate.lower()", "1974-12-25")]
    [InlineData("Patient.birthDate.trim()", "1974-12-25")]
    [InlineData("Patient.birthDate.replace('-','/')", "1974/12/25")]
    [InlineData("Patient.birthDate.replaceMatches('^1974','YYYY')", "YYYY-12-25")]
    public void GivenResourceBackedDate_WhenStringFunction_ThenReturnsWireLiteralResult(string expression, string expected)
    {
        // Arrange
        var patient = Parse(PatientJson);

        // Act
        var result = patient.Select(expression).Single();

        // Assert
        result.Value.ShouldBe(expected);
    }

    [Theory]
    [InlineData("Patient.birthDate.length()", 10)]
    [InlineData("Patient.birthDate.indexOf('12')", 5)]
    [InlineData("Patient.birthDate.split('-').count()", 3)]
    [InlineData("Patient.birthDate.toChars().count()", 10)]
    public void GivenResourceBackedDate_WhenIntegerStringFunction_ThenEvaluatesOverWireLiteral(string expression, int expected)
    {
        // Arrange
        var patient = Parse(PatientJson);

        // Act
        var result = patient.Select(expression).Single();

        // Assert
        result.Value.ShouldBe(expected);
    }

    [Fact]
    public void GivenResourceBackedInstant_WhenStringFunction_ThenEvaluatesOverWireLiteral()
    {
        // Arrange
        var observation = Parse(ObservationJson);

        // Act
        var result = observation.Select("Observation.issued.startsWith('2024')").Single();

        // Assert
        result.Value.ShouldBe(true);
    }

    [Fact]
    public void GivenResourceBackedTime_WhenStringFunction_ThenEvaluatesOverWireLiteral()
    {
        // Arrange
        var patient = Parse(TimePatientJson);

        // Act
        var result = patient.Select("Patient.extension.value.startsWith('10')").Single();

        // Assert
        result.Value.ShouldBe(true);
    }

    [Fact]
    public void GivenResourceBackedDate_WhenConcatenatedWithPlus_ThenUsesWireLiteral()
    {
        // Arrange
        var patient = Parse(PatientJson);

        // Act
        var result = patient.Select("Patient.birthDate + 'X'").Single();

        // Assert
        result.Value.ShouldBe("1974-12-25X");
    }

    [Fact]
    public void GivenResourceBackedInstant_WhenComparedToEarlierLiteral_ThenOrdersGreaterThan()
    {
        // Arrange
        // instant was missing from the evaluator's ordering type gate, so the comparison fell through
        // to a `is IComparable` check that FhirTemporal does not satisfy and yielded empty.
        var observation = Parse(ObservationJson);

        // Act
        var result = observation.Select("Observation.select(issued > @2024-01-01T00:00:00Z)").Single();

        // Assert
        result.Value.ShouldBe(true);
    }

    [Fact]
    public void GivenResourceBackedInstant_WhenComparedToLaterLiteral_ThenOrdersLessThan()
    {
        // Arrange
        var observation = Parse(ObservationJson);

        // Act
        var result = observation.Select("Observation.select(issued < @2025-01-01T00:00:00Z)").Single();

        // Assert
        result.Value.ShouldBe(true);
    }

    [Fact]
    public void GivenResourceBackedInstant_WhenFilteredOnOrdering_ThenWhereMatches()
    {
        // Arrange
        var observation = Parse(ObservationJson);

        // Act
        var result = observation.Select("Observation.where(issued > @2024-01-01T00:00:00Z).exists()").Single();

        // Assert
        result.Value.ShouldBe(true);
    }

    [Fact]
    public void GivenResourceBackedTime_WhenComparedToEqualLiteral_ThenReturnsTrue()
    {
        // Arrange
        // time was missing from the evaluator's equality type gate, so the comparison fell through to
        // object equality between a FhirTemporal and a string and always reported false.
        var patient = Parse(TimePatientJson);

        // Act
        var result = patient.Select("Patient.select(extension.value = @T10:30:00)").Single();

        // Assert
        result.Value.ShouldBe(true);
    }

    [Fact]
    public void GivenResourceBackedTime_WhenComparedToDifferentLiteral_ThenReturnsFalse()
    {
        // Arrange
        var patient = Parse(TimePatientJson);

        // Act
        var result = patient.Select("Patient.select(extension.value = @T09:00:00)").Single();

        // Assert
        result.Value.ShouldBe(false);
    }

    [Fact]
    public void GivenResourceBackedDate_WhenComparedToTimeLiteral_ThenReportsNotEqual()
    {
        // Arrange
        // A time of day and a calendar value are different types, so inequality is definitely true.
        // FhirTemporal.Compare reports the pairing as indeterminate, which is correct for ordering but
        // would wrongly surface as empty here.
        var patient = Parse(PatientJson);

        // Act
        var result = patient.Select("Patient.select(birthDate != @T12:14)").Single();

        // Assert
        result.Value.ShouldBe(true);
    }

    [Fact]
    public void GivenResourceBackedDate_WhenComparedToEqualStringLiteral_ThenReturnsTrue()
    {
        // Arrange
        // The equality gate requires both operands to be temporal, so a plain string literal falls back
        // to value equality, which must compare the temporal on its wire literal.
        var patient = Parse(PatientJson);

        // Act
        var result = patient.Select("Patient.select(birthDate = '1974-12-25')").Single();

        // Assert
        result.Value.ShouldBe(true);
    }

    [Fact]
    public void GivenResourceBackedDate_WhenFilteredOnStringEquality_ThenWhereMatches()
    {
        // Arrange
        // The compiled fast path used object equality, which never matches a FhirTemporal against the
        // string its predicate was written with.
        var patient = Parse(PatientJson);

        // Act
        var result = patient.Select("Patient.where(birthDate = '1974-12-25').exists()").Single();

        // Assert
        result.Value.ShouldBe(true);
    }

    private static IElement Parse(string json) => ResourceJsonNode.Parse(json).ToElement(Schema);
}
