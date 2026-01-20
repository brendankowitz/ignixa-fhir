/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Comprehensive edge case tests for FhirPath parser.
 * These tests cover malformed expressions, consecutive operators,
 * unclosed brackets, and other error scenarios to ensure the parser
 * fails gracefully with clear error messages.
 */

using Ignixa.FhirPath.Parser;

namespace Ignixa.FhirPath.Tests.Parsing;

/// <summary>
/// Tests for parser edge cases, particularly around error handling
/// for malformed expressions that should be rejected.
/// </summary>
public class ParserEdgeCaseTests
{
    private readonly FhirPathParser _parser = new();

    #region Consecutive Dot Operator Tests

    [Theory]
    [InlineData("Patient..name")]
    [InlineData("..name")]
    [InlineData("name..")]
    [InlineData("a.b..c")]
    [InlineData("a.b.c..d.e")]
    [InlineData("trace('trc').given.join(' ')..combine(family).join(', ')")]
    public void GivenConsecutiveDots_WhenParsing_ThenThrowsFormatException(string expression)
    {
        var ex = Assert.Throws<FormatException>(() => _parser.Parse(expression));
        Assert.NotNull(ex.Message);
    }

    [Theory]
    [InlineData("Patient..name")]
    [InlineData("..name")]
    [InlineData("name..")]
    [InlineData("a.b..c")]
    public void GivenConsecutiveDots_WhenTryParse_ThenReturnsFalseWithError(string expression)
    {
        var success = _parser.TryParse(expression, out var result, out var error);

        Assert.False(success);
        Assert.Null(result);
        Assert.NotNull(error);
    }

    #endregion

    #region Trailing Operator Tests

    [Theory]
    [InlineData("Patient.")]
    [InlineData("name.given.")]
    [InlineData("a.b.c.")]
    public void GivenTrailingDot_WhenParsing_ThenThrowsFormatException(string expression)
    {
        var ex = Assert.Throws<FormatException>(() => _parser.Parse(expression));
        Assert.NotNull(ex.Message);
    }

    [Theory]
    [InlineData("1 +")]
    [InlineData("a -")]
    [InlineData("x *")]
    [InlineData("y /")]
    [InlineData("a and")]
    [InlineData("b or")]
    [InlineData("c =")]
    [InlineData("d !=")]
    [InlineData("e >")]
    [InlineData("f <")]
    [InlineData("g >=")]
    [InlineData("h <=")]
    public void GivenTrailingBinaryOperator_WhenParsing_ThenThrowsFormatException(string expression)
    {
        var ex = Assert.Throws<FormatException>(() => _parser.Parse(expression));
        Assert.NotNull(ex.Message);
    }

    /// <summary>
    /// Tests that trailing type operators (as/is) without a type throw FormatException.
    /// </summary>
    [Theory]
    [InlineData("value as")]
    [InlineData("value is")]
    public void GivenTrailingTypeOperator_WhenParsing_ThenThrowsFormatException(string expression)
    {
        var ex = Assert.Throws<FormatException>(() => _parser.Parse(expression));
        Assert.NotNull(ex.Message);
    }

    #endregion

    #region Consecutive Binary Operator Tests

    [Theory]
    [InlineData("1 + * 2")]
    [InlineData("1 - / 2")]
    [InlineData("a and or b")]
    [InlineData("a = != b")]
    [InlineData("1 > < 2")]
    [InlineData("1 + + + 2")]
    public void GivenConsecutiveBinaryOperators_WhenParsing_ThenThrowsFormatException(string expression)
    {
        var ex = Assert.Throws<FormatException>(() => _parser.Parse(expression));
        Assert.NotNull(ex.Message);
    }

    #endregion

    #region Leading Operator Tests

    [Theory]
    [InlineData("* 2")]
    [InlineData("/ 2")]
    [InlineData("and b")]
    [InlineData("or b")]
    [InlineData("= 5")]
    [InlineData("!= 5")]
    [InlineData("> 5")]
    [InlineData("< 5")]
    public void GivenLeadingBinaryOperator_WhenParsing_ThenThrowsFormatException(string expression)
    {
        var ex = Assert.Throws<FormatException>(() => _parser.Parse(expression));
        Assert.NotNull(ex.Message);
    }

    [Fact]
    public void GivenLeadingUnaryMinus_WhenParsing_ThenSucceeds()
    {
        // Unary minus is valid
        var expr = _parser.Parse("-5");
        Assert.NotNull(expr);
    }

    [Fact]
    public void GivenLeadingUnaryPlus_WhenParsing_ThenSucceeds()
    {
        // Unary plus is valid
        var expr = _parser.Parse("+5");
        Assert.NotNull(expr);
    }

    #endregion

    #region Unclosed Bracket Tests

    [Theory]
    [InlineData("name[0")]
    [InlineData("name[")]
    [InlineData("a[b[c]")]
    [InlineData("items[0][1")]
    public void GivenUnclosedBracket_WhenParsing_ThenThrowsFormatException(string expression)
    {
        var ex = Assert.Throws<FormatException>(() => _parser.Parse(expression));
        Assert.NotNull(ex.Message);
    }

    [Theory]
    [InlineData("name]")]
    [InlineData("]name")]
    [InlineData("a[0]]")]
    public void GivenExtraBracket_WhenParsing_ThenThrowsFormatException(string expression)
    {
        var ex = Assert.Throws<FormatException>(() => _parser.Parse(expression));
        Assert.NotNull(ex.Message);
    }

    [Theory]
    [InlineData("a[]")]
    public void GivenEmptyIndexer_WhenParsing_ThenThrowsFormatException(string expression)
    {
        var ex = Assert.Throws<FormatException>(() => _parser.Parse(expression));
        Assert.NotNull(ex.Message);
    }

    #endregion

    #region Unclosed Parenthesis Tests

    [Theory]
    [InlineData("(1 + 2")]
    [InlineData("name.exists(")]
    [InlineData("where(x = 1")]
    [InlineData("((a)")]
    [InlineData("func(a, b")]
    public void GivenUnclosedParenthesis_WhenParsing_ThenThrowsFormatException(string expression)
    {
        var ex = Assert.Throws<FormatException>(() => _parser.Parse(expression));
        Assert.NotNull(ex.Message);
    }

    [Theory]
    [InlineData("1 + 2)")]
    [InlineData(")name")]
    [InlineData("(a))")]
    [InlineData("func())")]
    public void GivenExtraParenthesis_WhenParsing_ThenThrowsFormatException(string expression)
    {
        var ex = Assert.Throws<FormatException>(() => _parser.Parse(expression));
        Assert.NotNull(ex.Message);
    }

    [Fact]
    public void GivenEmptyParentheses_WhenParsing_ThenThrowsFormatException()
    {
        // () by itself is not valid - empty collection is {}
        var ex = Assert.Throws<FormatException>(() => _parser.Parse("()"));
        Assert.NotNull(ex.Message);
    }

    #endregion

    #region Malformed String Literal Tests

    [Theory]
    [InlineData("'unclosed")]
    [InlineData("'missing end quote")]
    public void GivenUnterminatedString_WhenParsing_ThenThrowsFormatException(string expression)
    {
        var ex = Assert.Throws<FormatException>(() => _parser.Parse(expression));
        Assert.NotNull(ex.Message);
    }

    [Fact]
    public void GivenStringWithNewline_WhenParsing_ThenSucceeds()
    {
        // FHIRPath spec allows newlines in string literals
        var expr = _parser.Parse("'has\nnewline'");
        Assert.NotNull(expr);
    }

    [Fact]
    public void GivenEmptyString_WhenParsing_ThenSucceeds()
    {
        // '' is a valid empty string literal
        var expr = _parser.Parse("''");
        Assert.NotNull(expr);
    }

    #endregion

    #region Malformed Function Call Tests

    [Theory]
    [InlineData("name.exists(,)")]
    [InlineData("name.where(,a)")]
    [InlineData("name.select(a,)")]
    [InlineData("func(,,)")]
    public void GivenMalformedFunctionArguments_WhenParsing_ThenThrowsFormatException(string expression)
    {
        var ex = Assert.Throws<FormatException>(() => _parser.Parse(expression));
        Assert.NotNull(ex.Message);
    }

    [Theory]
    [InlineData("name.()")]
    [InlineData(".exists()")]
    public void GivenMissingFunctionName_WhenParsing_ThenThrowsFormatException(string expression)
    {
        var ex = Assert.Throws<FormatException>(() => _parser.Parse(expression));
        Assert.NotNull(ex.Message);
    }

    #endregion

    #region Numeric Literal Edge Cases

    [Fact]
    public void GivenValidDecimal_WhenParsing_ThenSucceeds()
    {
        var expr = _parser.Parse("1.5");
        Assert.NotNull(expr);
    }

    [Fact]
    public void GivenMultipleDecimals_WhenParsing_ThenThrowsFormatException()
    {
        // 1.2.3 is not a valid number
        var ex = Assert.Throws<FormatException>(() => _parser.Parse("1.2.3"));
        Assert.NotNull(ex.Message);
    }

    #endregion

    #region Empty and Whitespace Tests

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public void GivenEmptyOrWhitespace_WhenParsing_ThenThrowsArgumentException(string expression)
    {
        Assert.Throws<ArgumentException>(() => _parser.Parse(expression));
    }

    #endregion

    #region Type Operator Edge Cases

    [Theory]
    [InlineData("as String")]
    [InlineData("is Integer")]
    public void GivenTypeOperatorWithoutOperand_WhenParsing_ThenThrowsFormatException(string expression)
    {
        var ex = Assert.Throws<FormatException>(() => _parser.Parse(expression));
        Assert.NotNull(ex.Message);
    }

    #endregion

    #region External Constant Edge Cases

    [Theory]
    [InlineData("%")]
    [InlineData("% ")]
    public void GivenIncompleteExternalConstant_WhenParsing_ThenThrowsFormatException(string expression)
    {
        var ex = Assert.Throws<FormatException>(() => _parser.Parse(expression));
        Assert.NotNull(ex.Message);
    }

    [Theory]
    [InlineData("%`unclosed")]
    [InlineData("%`missing backtick")]
    public void GivenUnterminatedDelimitedExternalConstant_WhenParsing_ThenThrowsFormatException(string expression)
    {
        var ex = Assert.Throws<FormatException>(() => _parser.Parse(expression));
        Assert.NotNull(ex.Message);
    }

    #endregion

    #region Special Character Tests

    [Theory]
    [InlineData("@")]
    [InlineData("#")]
    [InlineData("$")]
    [InlineData("^")]
    [InlineData("&")]
    [InlineData("~")]
    public void GivenInvalidCharacter_WhenParsing_ThenThrowsFormatException(string expression)
    {
        var ex = Assert.Throws<FormatException>(() => _parser.Parse(expression));
        Assert.NotNull(ex.Message);
    }

    #endregion

    #region Complex Malformed Expressions

    [Theory]
    [InlineData("Patient.name.where(given..first())")]
    [InlineData("(1 + 2) * (3 +)")]
    [InlineData("a.b[0].c().d..e")]
    [InlineData("func1().func2(arg..).func3()")]
    public void GivenComplexMalformedExpression_WhenParsing_ThenThrowsFormatException(string expression)
    {
        var ex = Assert.Throws<FormatException>(() => _parser.Parse(expression));
        Assert.NotNull(ex.Message);
    }

    #endregion

    #region Union and Contains Edge Cases

    [Theory]
    [InlineData("| b")]
    [InlineData("a |")]
    [InlineData("a | | b")]
    public void GivenMalformedUnion_WhenParsing_ThenThrowsFormatException(string expression)
    {
        var ex = Assert.Throws<FormatException>(() => _parser.Parse(expression));
        Assert.NotNull(ex.Message);
    }

    /// <summary>
    /// Note: "in" and "contains" are valid identifiers in FHIRPath when used standalone.
    /// They only become operators when in context (e.g., "a in b").
    /// </summary>
    [Theory]
    [InlineData("a in")]
    [InlineData("in b")]
    public void GivenMalformedInOperator_WhenParsing_ThenThrowsFormatException(string expression)
    {
        var ex = Assert.Throws<FormatException>(() => _parser.Parse(expression));
        Assert.NotNull(ex.Message);
    }

    [Fact]
    public void GivenStandaloneInKeyword_WhenParsing_ThenParsesAsIdentifier()
    {
        // "in" can be a valid identifier when standalone (property/element name)
        var expr = _parser.Parse("in");
        Assert.NotNull(expr);
    }

    /// <summary>
    /// Note: "contains" is both a function and an operator.
    /// When standalone, it parses as an identifier/property access.
    /// </summary>
    [Theory]
    [InlineData("a contains")]
    [InlineData("contains b")]
    public void GivenMalformedContainsOperator_WhenParsing_ThenThrowsFormatException(string expression)
    {
        var ex = Assert.Throws<FormatException>(() => _parser.Parse(expression));
        Assert.NotNull(ex.Message);
    }

    [Fact]
    public void GivenStandaloneContainsKeyword_WhenParsing_ThenParsesAsIdentifier()
    {
        // "contains" can be a valid identifier when standalone (e.g., ValueSet.expansion.contains)
        var expr = _parser.Parse("contains");
        Assert.NotNull(expr);
    }

    #endregion

    #region Implies Operator Edge Cases

    [Theory]
    [InlineData("implies")]
    [InlineData("a implies")]
    [InlineData("implies b")]
    public void GivenMalformedImpliesOperator_WhenParsing_ThenThrowsFormatException(string expression)
    {
        var ex = Assert.Throws<FormatException>(() => _parser.Parse(expression));
        Assert.NotNull(ex.Message);
    }

    #endregion

    #region Xor Operator Edge Cases

    [Theory]
    [InlineData("xor")]
    [InlineData("a xor")]
    [InlineData("xor b")]
    public void GivenMalformedXorOperator_WhenParsing_ThenThrowsFormatException(string expression)
    {
        var ex = Assert.Throws<FormatException>(() => _parser.Parse(expression));
        Assert.NotNull(ex.Message);
    }

    #endregion

    #region Quantity and Duration Edge Cases

    [Fact]
    public void GivenValidQuantity_WhenParsing_ThenSucceeds()
    {
        var expr = _parser.Parse("5 'kg'");
        Assert.NotNull(expr);
    }

    [Fact]
    public void GivenValidDuration_WhenParsing_ThenSucceeds()
    {
        var expr = _parser.Parse("1 year");
        Assert.NotNull(expr);
    }

    #endregion
}
