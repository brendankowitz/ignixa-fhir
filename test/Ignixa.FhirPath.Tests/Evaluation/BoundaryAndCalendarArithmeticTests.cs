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

    /// <summary>
    /// FHIRPath "Date/Time Arithmetic": "If there is more than one item, an item of an incompatible type, or
    /// an unsupported unit for the type, the evaluation of the expression will end and signal an error to the
    /// calling environment. This includes attempting to add date components to a Time." The unit table pairs
    /// years through days with Date/DateTime and hours through milliseconds with DateTime/Time, so a Date has
    /// no component to receive an hour and a Time none to receive a day.
    /// </summary>
    /// <remarks>
    /// These previously asserted empty. FHIRPath N1 already required the error for the DateTime and Time
    /// rows ("the quantity unit must be one of ... or the evaluation will end and signal an error to the
    /// calling environment"); the current build extends it to Date and states the Time case outright.
    /// Firely 6.0.1 throws for every expression here, so erroring closes a divergence rather than opening
    /// one.
    /// </remarks>
    [Theory]
    [InlineData("@1973-12-25 + 1 'h'")]
    [InlineData("@1973-12-25 + 1 'min'")]
    [InlineData("@1973-12-25 + 1 hour")]
    [InlineData("@1973-12-25 + 1 second")]
    [InlineData("@1973-12-25 - 1 'ms'")]
    [InlineData("@T10:00 + 1 'd'")]
    [InlineData("@T10:00 + 1 day")]
    [InlineData("@T10:00 + 1 year")]
    [InlineData("@T10:00 - 1 week")]
    public void GivenInvalidUnitForOperandType_WhenDateTimeArithmetic_ThenSignalsError(string expression)
    {
        var expr = _parser.Parse(expression);

        Assert.Throws<FhirPathEvaluationException>(() => _evaluator.Evaluate(Root(), expr).ToList());
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

        Assert.Throws<FhirPathEvaluationException>(() => _evaluator.Evaluate(Root(), expr).ToList());
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

        Assert.Throws<FhirPathEvaluationException>(() => _evaluator.Evaluate(Root(), expr).ToList());
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

    /// <summary>
    /// A unit finer than the operand does not promote the result's precision. FHIRPath "Date/Time Arithmetic"
    /// converts the quantity down to "the highest precision in the partial (truncating any decimal fraction)"
    /// and adds at that precision, so 30 seconds on a minute-precision operand is 30/60 = 0.5 minutes, which
    /// truncates to nothing.
    /// </summary>
    /// <remarks>
    /// These previously asserted <c>1973-12-25T10:00:30</c> and <c>10:00:30</c> under a
    /// <c>MaxPrecision(operand, unit)</c> result precision - the exact promotion the quoted rule forbids.
    /// Firely 6.0.1 likewise keeps the operand's precision (<c>@2014-03 + 1 day</c> is <c>@2014-03</c>).
    /// No official test case covers this shape, so nothing in the suite moves either way.
    /// </remarks>
    [Theory]
    [InlineData("@1973-12-25T10:00 + 30 second", "1973-12-25T10:00", "dateTime")]
    [InlineData("@1973-12-25T10:00 + 90 second", "1973-12-25T10:01", "dateTime")]
    [InlineData("@T10:00 + 30 's'", "10:00", "time")]
    [InlineData("@T10:00 + 120 's'", "10:02", "time")]
    [InlineData("@2014-03 + 1 day", "2014-03", "date")]
    public void GivenMorePreciseUnit_WhenDateTimeArithmetic_ThenKeepsOperandPrecision(string expression, string expected, string expectedType)
    {
        var expr = _parser.Parse(expression);
        var result = _evaluator.Evaluate(Root(), expr).Single();

        Assert.Equal(expectedType, result.InstanceType);
        Assert.Equal(expected, result.Value);
    }

    /// <summary>
    /// The worked examples from FHIRPath "Date/Time Arithmetic", verbatim. Each pins the rule that the
    /// quantity is converted to the partial's precision, truncated, and then applied - which requires
    /// year-precision operands to be parseable at all, and forbids the result gaining precision.
    /// </summary>
    [Theory]
    [InlineData("@2014 + 24 months", "2016", "date")]
    [InlineData("@2014 + 23 months", "2015", "date")]
    [InlineData("@2014 + 11 months", "2014", "date")]
    [InlineData("@2016 + 365 days", "2017", "date")]
    [InlineData("@2014 - 24 months", "2012", "date")]
    [InlineData("@2014 - 1 month", "2014", "date")]
    [InlineData("@2026-02 + 5 weeks", "2026-03", "date")]
    [InlineData("@2026-02 + 4 weeks", "2026-02", "date")]
    [InlineData("@2026-02 - 1 day", "2026-02", "date")]
    [InlineData("@2019-03-01 + 24 months", "2021-03-01", "date")]
    [InlineData("@2019-03-01 - 24 months", "2017-03-01", "date")]
    [InlineData("@2026-01-31 + 1 month", "2026-02-28", "date")]
    [InlineData("@1973-12-25 + 7 days", "1974-01-01", "date")]
    [InlineData("@1973-12-25 + 7.9 days", "1974-01-01", "date")]
    [InlineData("@1973-12-25 + 1 week", "1974-01-01", "date")]
    [InlineData("@1973-12-25 + 1 'd'", "1973-12-26", "date")]
    [InlineData("@2026-01-01T13:00:00 + 30 minutes", "2026-01-01T13:30:00", "dateTime")]
    [InlineData("@1973-12-25T00:00:00.000+10:00 + 42.53 seconds", "1973-12-25T00:00:42.530+10:00", "dateTime")]
    [InlineData("@T23:30:00 + 1 hour", "00:30:00", "time")]
    [InlineData("@T01:00:00 + 48 hour", "01:00:00", "time")]
    public void GivenASpecWorkedExample_WhenTemporalArithmetic_ThenMatchesTheSpecifiedResult(string expression, string expected, string expectedType)
    {
        var expr = _parser.Parse(expression);
        var result = _evaluator.Evaluate(Root(), expr).Single();

        Assert.Equal(expectedType, result.InstanceType);
        Assert.Equal(expected, result.Value);
    }

    /// <summary>
    /// Sub-month units reach years through the 365-day factor, not by chaining days to months to years:
    /// "If the date/time value only has years present then when adding month quantities; use the direct
    /// conversion from months to years, otherwise convert the quantity to days, then to years". Chaining
    /// would put a year at 30 x 12 = 360 days and turn the first case below into @2017.
    /// </summary>
    [Theory]
    [InlineData("@2016 + 360 days", "2016")]
    [InlineData("@2016 + 364 days", "2016")]
    [InlineData("@2016 + 366 days", "2017")]
    [InlineData("@2016 + 52 weeks", "2016")]
    [InlineData("@2016 + 53 weeks", "2017")]
    public void GivenASubMonthUnitOnAYearPrecisionDate_WhenAdded_ThenConvertsThroughDaysAtThreeSixtyFive(string expression, string expected)
    {
        var expr = _parser.Parse(expression);
        var result = _evaluator.Evaluate(Root(), expr).Single();

        Assert.Equal("date", result.InstanceType);
        Assert.Equal(expected, result.Value);
    }

    /// <summary>
    /// FHIRPath "Date/Time Arithmetic": "The decimal portion of the time-valued quantity is only applied for
    /// second or millisecond precisions; for all other precisions, the decimal portion is ignored, since
    /// date/time arithmetic is performed with calendar duration semantics."
    /// </summary>
    /// <remarks>
    /// Hours and minutes previously kept their fraction, so <c>+ 1.5 hours</c> advanced 90 minutes. Firely
    /// 6.0.1 still does that (it truncates only at day precision and coarser), so this is a deliberate
    /// divergence in favour of the spec text, which is unchanged from N1.
    /// </remarks>
    [Theory]
    [InlineData("@2014-01-01T10:00:00 + 1.5 hours", "2014-01-01T11:00:00")]
    [InlineData("@2014-01-01T10:00:00 + 1.5 minutes", "2014-01-01T10:01:00")]
    [InlineData("@2014-01-01T10:00:00 - 1.5 hours", "2014-01-01T09:00:00")]
    [InlineData("@2014-01-01T10:00:00 + 1.9 days", "2014-01-02T10:00:00")]
    [InlineData("@2014-01-01T10:00:00 + 1.5 seconds", "2014-01-01T10:00:01")]
    [InlineData("@2014-01-01T10:00:00.000 + 1.5 seconds", "2014-01-01T10:00:01.500")]
    public void GivenAFractionalQuantity_WhenTemporalArithmetic_ThenAppliesTheFractionOnlyBelowMinutes(string expression, string expected)
    {
        var expr = _parser.Parse(expression);
        var result = _evaluator.Evaluate(Root(), expr).Single();

        Assert.Equal("dateTime", result.InstanceType);
        Assert.Equal(expected, result.Value);
    }
}
