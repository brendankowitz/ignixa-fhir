/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Regression tests for FHIRPath spec compliance issues identified by fhirpath-lab.
 * Covers:
 *   #243 - highBoundary/lowBoundary must preserve trailing zeros to the requested precision.
 *   #245 - Date + Quantity arithmetic must reject UCUM-syntax inexact units ('a', 'mo')
 *          and any non-calendar/non-time unit (e.g. 'cm').
 */

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Evaluation.Functions;
using Ignixa.FhirPath.Parser;
using Xunit;

namespace Ignixa.FhirPath.Tests.Evaluation;

public class BoundaryAndCalendarArithmeticTests
{
    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    private static IElement Root() => new FunctionHelpers.PrimitiveElement(0, "integer");

    private static int GetDecimalScale(decimal value)
    {
        var bits = decimal.GetBits(value);
        return (bits[3] >> 16) & 0xFF;
    }

    // ---------------------------------------------------------------------------------
    // Issue #243 - Decimal boundary must preserve trailing zeros to requested precision.
    // ---------------------------------------------------------------------------------

    [Theory]
    [InlineData("1.587.highBoundary(8)", "1.58750000")]
    [InlineData("1.587.highBoundary()", "1.58750000")]
    [InlineData("1.587.highBoundary(6)", "1.587500")]
    [InlineData("1.587.lowBoundary(8)", "1.58650000")]
    [InlineData("1.highBoundary(5)", "1.50000")]
    [InlineData("120.highBoundary(2)", "120.50")]
    [InlineData("(-1.587).highBoundary()", "-1.58650000")]
    [InlineData("12.500.highBoundary(4)", "12.5005")]
    public void GivenDecimalBoundary_WhenEvaluated_ThenResultStringPreservesTrailingZeros(string expression, string expectedString)
    {
        var expr = _parser.Parse(expression);
        var result = _evaluator.Evaluate(Root(), expr).Single();

        Assert.IsType<decimal>(result.Value);
        var actual = (decimal)result.Value;
        Assert.Equal(expectedString, actual.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    // ---------------------------------------------------------------------------------
    // Issue #245 - UCUM gate in date arithmetic.
    //   - 'a' (UCUM mean year) and 'mo' (UCUM mean month) are inexact and must be rejected.
    //   - Non-time UCUM units like 'cm' must be rejected.
    //   - Calendar keywords (year, month, ...) and exact UCUM time units ('wk', 'd',
    //     'h', 'min', 's', 'ms') continue to work.
    // ---------------------------------------------------------------------------------

    [Theory]
    [InlineData("@1973-12-25 + 1 'a'")]    // UCUM mean year - ambiguous, must be rejected
    [InlineData("@1973-12-25 + 1 'mo'")]   // UCUM mean month - ambiguous, must be rejected
    [InlineData("@1973-12-25 + 1 'cm'")]   // non-time unit, must be rejected
    [InlineData("@1973-12-25 - 1 'a'")]
    [InlineData("@1973-12-25 - 1 'mo'")]
    public void GivenAmbiguousOrNonTimeUcumUnit_WhenDateArithmetic_ThenReturnsEmpty(string expression)
    {
        var expr = _parser.Parse(expression);
        var result = _evaluator.Evaluate(Root(), expr).ToList();
        Assert.Empty(result);
    }

    [Theory]
    [InlineData("@1973-12-25 + 1 year", "1974-12-25")]
    [InlineData("@1973-12-25 + 1 month", "1974-01-25")]
    [InlineData("@1973-12-25 + 1 'wk'", "1974-01-01")]
    [InlineData("@1973-12-25 + 1 'd'", "1973-12-26")]
    [InlineData("@1973-12-25 + 1 week", "1974-01-01")]
    [InlineData("@1973-12-25 + 1 day", "1973-12-26")]
    public void GivenCalendarOrExactUcumUnit_WhenDateArithmetic_ThenReturnsExpectedDate(string expression, string expected)
    {
        var expr = _parser.Parse(expression);
        var result = _evaluator.Evaluate(Root(), expr).Single();

        Assert.Equal(expected, result.Value);
    }
}
