// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Evaluation.Functions;
using Ignixa.FhirPath.Parser;
using Ignixa.FhirPath.Types;
using Shouldly;

namespace Ignixa.FhirPath.Tests.Evaluation;

/// <summary>
/// The input failures that still answer empty / "not compatible" after the catch-alls behind them were
/// narrowed to the exceptions they mean (#538). Each row is a failure the narrowed handler must still catch.
/// </summary>
public class NarrowedFailureHandlingTests
{
    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    [Theory]
    [InlineData("'not*base64'.decode('base64')")]
    [InlineData("'abc'.decode('urlbase64')")]
    [InlineData("'zz'.decode('hex')")]
    [InlineData("'abc'.decode('hex')")]
    public void GivenTextThatIsNotInTheEncoding_WhenDecoding_ThenTheResultIsEmpty(string expression)
    {
        // Act & Assert
        Evaluate(expression).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("'100%'.unescape('url')", "100%")]
    [InlineData("'%E0%A4%A'.unescape('url')", "%E0%A4%A")]
    [InlineData("'&bogus; &amp'.unescape('html')", "&bogus; &amp")]
    [InlineData(@"'\\u12'.unescape('json')", @"\u12")]
    public void GivenAMalformedEscape_WhenUnescaping_ThenItIsLeftInPlace(string expression, string expected)
    {
        // Act
        var result = Evaluate(expression).Single().Value;

        // Assert
        result.ShouldBe(expected);
    }

    [Theory]
    [InlineData("'2023-02-30'.toDate()", false)]
    [InlineData("'2023-02-29'.toDate()", false)]
    [InlineData("'0000-01-01'.toDate()", false)]
    [InlineData("'2024-02-29'.toDate()", true)]
    [InlineData("'0000-01'.toDate()", true)]
    public void GivenADateString_WhenConvertingToDate_ThenOnlyARealCalendarDayConverts(string expression, bool converts)
    {
        // Act & Assert
        Evaluate(expression).Any().ShouldBe(converts);
    }

    [Theory]
    [InlineData("@0000-01-01.duration(@2020-01-01)")]
    [InlineData("@2020-01-01.difference(@0000-01-01)")]
    public void GivenADateDotNetCannotRepresent_WhenMeasuringBetweenDates_ThenTheResultIsEmpty(string expression)
    {
        // Act & Assert
        Evaluate(expression).ShouldBeEmpty();
    }

    /// <summary>
    /// One row per exception type Fhir.Metrics raises for a unit it cannot handle; removing any type from the
    /// converter's filter fails its row.
    /// </summary>
    [Theory]
    [InlineData("xyz")] // ArgumentException: unknown unit
    [InlineData("Cel")] // InvalidCastException: no path to base units
    [InlineData("m٢")] // FormatException: non-ASCII exponent digit
    [InlineData("m40000")] // OverflowException: exponent beyond Int16
    public void GivenAUnitTheUcumLibraryCannotHandle_WhenConverting_ThenItIsReportedAsIncompatible(string unit)
    {
        // Arrange
        var converter = QuantityUnitConverter.Instance;
        var metre = new FhirQuantity(1m, "m");
        var odd = new FhirQuantity(1m, unit);

        // Act & Assert
        converter.IsCompatible("m", unit).ShouldBeFalse();
        converter.Convert(1m, unit, "m").ShouldBeNull();
        converter.GetDimensionality(unit).ShouldBeNull();
        converter.Multiply(odd, metre).ShouldBeNull();
        converter.Divide(odd, metre).ShouldBeNull();
    }

    private List<IElement> Evaluate(string expression) =>
        _evaluator.Evaluate(FunctionHelpers.CreateInteger(0), _parser.Parse(expression)).ToList();
}
