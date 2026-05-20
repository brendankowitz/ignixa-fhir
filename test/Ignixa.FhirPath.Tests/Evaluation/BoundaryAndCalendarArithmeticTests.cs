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
    [InlineData("@T10:00 + 1 'a'")]
    [InlineData("@T10:00 + 1 'd'")]
    public void GivenInvalidUnitForOperandType_WhenDateTimeArithmetic_ThenReturnsEmpty(string expression)
    {
        var expr = _parser.Parse(expression);
        var result = _evaluator.Evaluate(Root(), expr).ToList();

        Assert.Empty(result);
    }

    [Theory]
    [InlineData("@1973-12-25 + 1 'a'", "1974-12-25")]
    [InlineData("@1973-12-25 + 1 'mo'", "1974-01-25")]
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
    [InlineData("@1973-12-25T10:00:00Z + 1 'a'", "1974-12-25T10:00:00+00:00")]
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
