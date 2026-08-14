/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Regression tests for FHIRPath date/time arithmetic unit gating and precision handling.
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

    [Theory]
    [InlineData("@1973-12-25 + 1 'h'")]
    [InlineData("@1973-12-25 + 1 'min'")]
    [InlineData("@T10:00 + 1 'd'")]
    public void GivenInvalidUnitForOperandType_WhenDateTimeArithmetic_ThenReturnsEmpty(string expression)
    {
        var expr = _parser.Parse(expression);
        var result = _evaluator.Evaluate(Root(), expr).ToList();

        Assert.Empty(result);
    }

    /// <summary>
    /// FHIRPath requires the other operand of <c>+</c>/<c>-</c> on a Date, DateTime or Time to be a Quantity
    /// with a time-valued unit, and to signal an error otherwise rather than yield a value or an empty
    /// collection. Official cases: <c>testPlus6</c> (<c>@1974-12-25 + 7</c>, semantic) and <c>testMinus6</c>
    /// (<c>@1974-12-25 - 1 'cm'</c>, execution).
    /// </summary>
    [Theory]
    [InlineData("@1974-12-25 + 7")]
    [InlineData("@1974-12-25 - 7")]
    [InlineData("7 + @1974-12-25")]
    [InlineData("@1974-12-25 + 'X'")]
    [InlineData("@1974-12-25T10:00:00Z + 7")]
    [InlineData("@T10:00 + 7")]
    [InlineData("@1974-12-25 - 1 'cm'")]
    [InlineData("@1974-12-25 + 1 'kg'")]
    [InlineData("@T10:00 + 1 'cm'")]
    public void GivenTemporalWithNonTimeValuedOperand_WhenAddedOrSubtracted_ThenSignalsError(string expression)
    {
        var expr = _parser.Parse(expression);

        Assert.Throws<InvalidOperationException>(() => _evaluator.Evaluate(Root(), expr).ToList());
    }

    /// <summary>
    /// The error above must not swallow the spec's empty-operand propagation: an empty operand still yields
    /// empty, never an error. Official cases <c>testPlusEmpty1/2/3</c> and <c>testMinusEmpty1/2/3</c> cover
    /// the numeric form; these pin the temporal form, which the error path could plausibly have broken.
    /// </summary>
    [Theory]
    [InlineData("@1974-12-25 + {}")]
    [InlineData("{} + @1974-12-25")]
    [InlineData("@1974-12-25 - {}")]
    [InlineData("{} - @1974-12-25")]
    [InlineData("@1974-12-25T10:00:00Z + {}")]
    [InlineData("@T10:00 + {}")]
    [InlineData("1 + {}")]
    [InlineData("{} + 1")]
    [InlineData("{} + {}")]
    public void GivenAnEmptyOperand_WhenAddedOrSubtractedWithATemporal_ThenReturnsEmpty(string expression)
    {
        var expr = _parser.Parse(expression);
        var result = _evaluator.Evaluate(Root(), expr).ToList();

        Assert.Empty(result);
    }

    /// <summary>
    /// The UCUM definite durations <c>'a'</c> (a fixed 365.25 days) and <c>'mo'</c> (a fixed twelfth of it)
    /// have no calendar equivalent, so FHIRPath 3.0 "Date/Time Arithmetic" says using either signals an
    /// error. Official cases: <c>testPlusDate16</c> (<c>@1973-12-25 + 1 'a'</c>), <c>testPlusDate17</c>
    /// (<c>@1975-12-25 + 1 'a'</c>) and <c>testPlusDate14</c> (<c>@1973-12-25 + 1 'mo'</c>), all marked
    /// <c>invalid="execution"</c> in R4/R4B/R5; Firely 4.3.0/5.11.4/6.0.1 and HAPI all throw.
    /// </summary>
    /// <remarks>
    /// These previously asserted values - <c>1974-12-25</c>, <c>1974-01-25</c> and
    /// <c>1974-12-25T10:00:00+00:00</c> respectively - by treating 'a'/'mo' as synonyms for the calendar
    /// keywords, and <c>@T10:00 + 1 'a'</c> asserted empty. The calendar-keyword forms they were really
    /// covering stay pinned by <see cref="GivenCalendarKeyword_WhenTemporalArithmetic_ThenReturnsExpectedValue"/>.
    /// </remarks>
    [Theory]
    [InlineData("@1973-12-25 + 1 'a'")]
    [InlineData("@1975-12-25 + 1 'a'")]
    [InlineData("@1973-12-25 + 1 'mo'")]
    [InlineData("@1973-12-25 - 1 'a'")]
    [InlineData("@1973-12-25 - 1 'mo'")]
    [InlineData("@1973-12-25T10:00:00Z + 1 'a'")]
    [InlineData("@1973-12-25T10:00:00Z + 1 'mo'")]
    [InlineData("@T10:00 + 1 'a'")]
    [InlineData("@T10:00 + 1 'mo'")]
    [InlineData("1 'a' + @1973-12-25")]
    public void GivenUcumDefiniteDurationUnit_WhenTemporalArithmetic_ThenSignalsError(string expression)
    {
        var expr = _parser.Parse(expression);

        Assert.Throws<InvalidOperationException>(() => _evaluator.Evaluate(Root(), expr).ToList());
    }

    /// <summary>
    /// The calendar keywords stay valid - the error above is specific to the UCUM codes, and must not
    /// over-tighten into the keyword forms the rejected cases used to stand in for.
    /// </summary>
    [Theory]
    [InlineData("@1973-12-25 + 1 year", "1974-12-25", "date")]
    [InlineData("@1973-12-25 + 1 month", "1974-01-25", "date")]
    [InlineData("@1973-12-25 - 1 year", "1972-12-25", "date")]
    [InlineData("@1973-12-25 - 1 month", "1973-11-25", "date")]
    [InlineData("@1973-12-25T10:00:00Z + 1 year", "1974-12-25T10:00:00+00:00", "dateTime")]
    [InlineData("@1973-12-25T10:00:00Z + 1 month", "1974-01-25T10:00:00+00:00", "dateTime")]
    [InlineData("1 year + @1973-12-25", "1974-12-25", "date")]
    public void GivenCalendarKeyword_WhenTemporalArithmetic_ThenReturnsExpectedValue(string expression, string expected, string expectedType)
    {
        var expr = _parser.Parse(expression);
        var result = _evaluator.Evaluate(Root(), expr).Single();

        Assert.Equal(expectedType, result.InstanceType);
        Assert.Equal(expected, result.Value);
    }

    /// <summary>
    /// The remaining UCUM time units are definite <em>and</em> unambiguous, so they keep working. Official
    /// cases <c>testPlusDate15</c> (<c>@1973-12-25 + 1 'wk'</c>) and <c>testPlusDate18</c>
    /// (<c>... + 1 's'</c>) pin two of them.
    /// </summary>
    [Theory]
    [InlineData("@1973-12-25 + 1 'wk'", "1974-01-01")]
    [InlineData("@1973-12-25 + 1 'd'", "1973-12-26")]
    public void GivenValidDateUnit_WhenDateArithmetic_ThenReturnsExpectedDate(string expression, string expected)
    {
        var expr = _parser.Parse(expression);
        var result = _evaluator.Evaluate(Root(), expr).Single();

        Assert.Equal("date", result.InstanceType);
        Assert.Equal(expected, result.Value);
    }

    [Theory]
    [InlineData("@1973-12-25T10:00:00Z + 1 'h'", "1973-12-25T11:00:00+00:00")]
    [InlineData("@1973-12-25T10:00:00Z + 1 'wk'", "1974-01-01T10:00:00+00:00")]
    [InlineData("@1973-12-25T10:00:00Z + 1 'min'", "1973-12-25T10:01:00+00:00")]
    public void GivenValidDateTimeUnit_WhenDateTimeArithmetic_ThenReturnsExpectedDateTime(string expression, string expected)
    {
        var expr = _parser.Parse(expression);
        var result = _evaluator.Evaluate(Root(), expr).Single();

        Assert.Equal("dateTime", result.InstanceType);
        Assert.Equal(expected, result.Value);
    }

    [Theory]
    [InlineData("@1973-12-25T10:00 + 30 second", "1973-12-25T10:00:30", "dateTime")]
    [InlineData("@T10:00 + 30 's'", "10:00:30", "time")]
    public void GivenMorePreciseUnit_WhenDateTimeArithmetic_ThenPromotesResultPrecision(string expression, string expected, string expectedType)
    {
        var expr = _parser.Parse(expression);
        var result = _evaluator.Evaluate(Root(), expr).Single();

        Assert.Equal(expectedType, result.InstanceType);
        Assert.Equal(expected, result.Value);
    }
}
