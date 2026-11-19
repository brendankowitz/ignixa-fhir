/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Pins surrogate-pair handling in string literal escapes (#537).
 */

using Ignixa.FhirPath.Expressions;
using Ignixa.FhirPath.Parser;

namespace Ignixa.FhirPath.Tests.Parsing;

/// <summary>
/// A non-BMP character is written as two <c>\uXXXX</c> escapes (a high then a low surrogate). The parser
/// must combine them into one code point, and must reject a surrogate that is not part of a pair rather
/// than leak an <see cref="ArgumentOutOfRangeException"/>.
/// </summary>
public class StringLiteralUnicodeEscapeTests
{
    private readonly FhirPathParser _parser = new();

    [Fact]
    public void GivenASurrogatePairOfEscapes_WhenParsed_ThenItIsOneNonBmpCodePoint()
    {
        // Act
        var parsed = _parser.Parse(@"'\uD83D\uDE00'");

        // Assert
        var value = parsed.ShouldBeOfType<ConstantExpression>().Value.ShouldBeOfType<string>();
        value.ShouldBe("\U0001F600");
        char.ConvertToUtf32(value, 0).ShouldBe(0x1F600);
    }

    [Fact]
    public void GivenAPairBetweenOrdinaryCharacters_WhenParsed_ThenTheSurroundingTextIsKept()
    {
        // Act
        var parsed = _parser.Parse(@"'a\uD83D\uDE00b\u0041'");

        // Assert
        parsed.ShouldBeOfType<ConstantExpression>().Value.ShouldBe("a\U0001F600bA");
    }

    [Theory]
    [InlineData(@"'\uD800'")]
    [InlineData(@"'\uDBFF'")]
    [InlineData(@"'\uDC00'")]
    [InlineData(@"'\uDFFF'")]
    [InlineData(@"'\uD83Dx'")]
    [InlineData(@"'\uD83D\u0041'")]
    [InlineData(@"'\uD83D\uD83D'")]
    [InlineData(@"'\uDE00\uD83D'")]
    [InlineData(@"'\uD83D\uDE00\uDE00'")]
    public void GivenASurrogateThatIsNotPartOfAPair_WhenParsed_ThenItFailsAsAFormatException(string expression)
    {
        // Act
        var parse = () => _parser.Parse(expression);

        // Assert
        Should.Throw<FormatException>(parse).Message.ShouldContain("surrogate");
    }
}
