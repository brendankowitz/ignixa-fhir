/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Pins that an out-of-range numeric literal fails the parse as a FormatException (#537).
 */

using Ignixa.FhirPath.Expressions;
using Ignixa.FhirPath.Parser;

namespace Ignixa.FhirPath.Tests.Parsing;

/// <summary>
/// An integer, long, decimal, quantity or calendar-duration literal that does not fit its CLR type is a
/// malformed expression. <see cref="FhirPathParser.Parse"/> documents <see cref="FormatException"/> as its
/// only failure, so <see cref="OverflowException"/> must not escape.
/// </summary>
/// <remarks>
/// FHIRPath Integer is 32-bit, so <c>-2147483648</c> is unary minus applied to the out-of-range literal
/// <c>2147483648</c> and is rejected like it. The official suite has no case for it; write it as a Long
/// (<c>-2147483648L</c>) or Decimal instead.
/// </remarks>
public class NumericLiteralRangeTests
{
    private readonly FhirPathParser _parser = new();

    [Theory]
    [InlineData("2147483648")]
    [InlineData("-2147483648")]
    [InlineData("99999999999999999999")]
    [InlineData("9223372036854775808L")]
    [InlineData("79228162514264337593543950336.0")]
    [InlineData("79228162514264337593543950336 'mg'")]
    [InlineData("79228162514264337593543950336.5 'mg'")]
    [InlineData("79228162514264337593543950336 days")]
    [InlineData("79228162514264337593543950336.5 years")]
    [InlineData("1 + 2147483648")]
    [InlineData("name.where(value = 2147483648)")]
    public void GivenAnOutOfRangeNumericLiteral_WhenParsed_ThenItFailsAsAFormatException(string expression)
    {
        // Act
        var parse = () => _parser.Parse(expression);

        // Assert
        var failure = Should.Throw<FormatException>(parse);
        failure.Message.ShouldContain("outside the range");
    }

    [Theory]
    [InlineData("2147483647", 2147483647)]
    [InlineData("9223372036854775807L", 9223372036854775807L)]
    public void GivenTheLargestIntegerOrLongLiteral_WhenParsed_ThenItIsAccepted(string expression, object expected)
    {
        // Act
        var parsed = _parser.Parse(expression);

        // Assert
        parsed.ShouldBeOfType<ConstantExpression>().Value.ShouldBe(expected);
    }

    [Fact]
    public void GivenTheLargestDecimalLiteral_WhenParsed_ThenItIsAccepted()
    {
        // Act
        var parsed = _parser.Parse("79228162514264337593543950335.0");

        // Assert
        parsed.ShouldBeOfType<ConstantExpression>().Value.ShouldBe(decimal.MaxValue);
    }

    [Fact]
    public void GivenAnOutOfRangeLiteral_WhenTryParsing_ThenItReportsFailureWithoutThrowing()
    {
        // Act
        var succeeded = _parser.TryParse("2147483648", out var expression, out var message);

        // Assert
        succeeded.ShouldBeFalse();
        expression.ShouldBeNull();
        message.ShouldNotBeNull().ShouldContain("2147483648");
    }
}
