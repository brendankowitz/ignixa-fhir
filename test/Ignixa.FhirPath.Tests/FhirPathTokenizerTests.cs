/*
 * Copyright (c) 2025, Sparky Contributors
 *
 * Unit tests for FhirPath tokenizer.
 */

using Ignixa.FhirPath.Lexer;
using Superpower;
using Superpower.Model;

namespace Ignixa.FhirPath.Tests;

public class FhirPathTokenizerTests
{
    private readonly Tokenizer<FhirPathTokenKind> _tokenizer = FhirPathTokenizer.Create();

    // Helper method to tokenize and verify
    private Token<FhirPathTokenKind>[] Tokenize(string input)
    {
        var result = _tokenizer.TryTokenize(input);
        Assert.True(result.HasValue, $"Tokenization failed: {result}");
        return result.Value.ToArray();
    }

    [Fact]
    public void GivenStringLiteral_WhenTokenizing_ThenProducesStringLiteralToken()
    {
        var tokens = Tokenize("'hello world'");

        Assert.Single(tokens);
        Assert.Equal(FhirPathTokenKind.StringLiteral, tokens[0].Kind);
        Assert.Equal("'hello world'", tokens[0].ToStringValue());
    }

    [Fact]
    public void GivenIntegerLiteral_WhenTokenizing_ThenProducesIntegerLiteralToken()
    {
        var tokens = Tokenize("42");

        Assert.Single(tokens);
        Assert.Equal(FhirPathTokenKind.IntegerLiteral, tokens[0].Kind);
        Assert.Equal("42", tokens[0].ToStringValue());
    }

    [Fact]
    public void GivenDecimalLiteral_WhenTokenizing_ThenProducesDecimalLiteralToken()
    {
        var tokens = Tokenize("3.14159");

        Assert.Single(tokens);
        Assert.Equal(FhirPathTokenKind.DecimalLiteral, tokens[0].Kind);
        Assert.Equal("3.14159", tokens[0].ToStringValue());
    }

    [Fact]
    public void GivenBooleanTrue_WhenTokenizing_ThenProducesBooleanLiteralToken()
    {
        var tokens = Tokenize("true");

        Assert.Single(tokens);
        Assert.Equal(FhirPathTokenKind.BooleanLiteral, tokens[0].Kind);
        Assert.Equal("true", tokens[0].ToStringValue());
    }

    [Fact]
    public void GivenBooleanFalse_WhenTokenizing_ThenProducesBooleanLiteralToken()
    {
        var tokens = Tokenize("false");

        Assert.Single(tokens);
        Assert.Equal(FhirPathTokenKind.BooleanLiteral, tokens[0].Kind);
        Assert.Equal("false", tokens[0].ToStringValue());
    }

    [Fact]
    public void GivenDateLiteral_WhenTokenizing_ThenProducesDateLiteralToken()
    {
        var tokens = Tokenize("@2024-01-15");

        Assert.Single(tokens);
        Assert.Equal(FhirPathTokenKind.DateLiteral, tokens[0].Kind);
        Assert.Equal("@2024-01-15", tokens[0].ToStringValue());
    }

    [Fact]
    public void GivenPartialDateLiteral_WhenTokenizing_ThenProducesDateLiteralToken()
    {
        var tokens = Tokenize("@2024");

        Assert.Single(tokens);
        Assert.Equal(FhirPathTokenKind.DateLiteral, tokens[0].Kind);
        Assert.Equal("@2024", tokens[0].ToStringValue());
    }

    [Fact]
    public void GivenDateTimeLiteral_WhenTokenizing_ThenProducesDateTimeLiteralToken()
    {
        var tokens = Tokenize("@2024-01-15T14:30:00Z");

        Assert.Single(tokens);
        Assert.Equal(FhirPathTokenKind.DateTimeLiteral, tokens[0].Kind);
        Assert.Equal("@2024-01-15T14:30:00Z", tokens[0].ToStringValue());
    }

    [Fact]
    public void GivenTimeLiteral_WhenTokenizing_ThenProducesTimeLiteralToken()
    {
        var tokens = Tokenize("@T14:30:00");

        Assert.Single(tokens);
        Assert.Equal(FhirPathTokenKind.TimeLiteral, tokens[0].Kind);
        Assert.Equal("@T14:30:00", tokens[0].ToStringValue());
    }

    [Fact]
    public void GivenIdentifier_WhenTokenizing_ThenProducesIdentifierToken()
    {
        var tokens = Tokenize("Patient");

        Assert.Single(tokens);
        Assert.Equal(FhirPathTokenKind.Identifier, tokens[0].Kind);
        Assert.Equal("Patient", tokens[0].ToStringValue());
    }

    [Fact]
    public void GivenDelimitedIdentifier_WhenTokenizing_ThenProducesDelimitedIdentifierToken()
    {
        var tokens = Tokenize("`quoted identifier`");

        Assert.Single(tokens);
        Assert.Equal(FhirPathTokenKind.DelimitedIdentifier, tokens[0].Kind);
    }

    [Fact]
    public void GivenExternalConstant_WhenTokenizing_ThenProducesExternalConstantToken()
    {
        var tokens = Tokenize("%context");

        Assert.Single(tokens);
        Assert.Equal(FhirPathTokenKind.ExternalConstant, tokens[0].Kind);
    }

    [Fact]
    public void GivenAxisThis_WhenTokenizing_ThenProducesAxisToken()
    {
        var tokens = Tokenize("$this");

        Assert.Single(tokens);
        Assert.Equal(FhirPathTokenKind.Axis, tokens[0].Kind);
        Assert.Equal("$this", tokens[0].ToStringValue());
    }

    [Fact]
    public void GivenAxisIndex_WhenTokenizing_ThenProducesAxisToken()
    {
        var tokens = Tokenize("$index");

        Assert.Single(tokens);
        Assert.Equal(FhirPathTokenKind.Axis, tokens[0].Kind);
        Assert.Equal("$index", tokens[0].ToStringValue());
    }

    [Theory]
    [InlineData("and", FhirPathTokenKind.And)]
    [InlineData("or", FhirPathTokenKind.Or)]
    [InlineData("xor", FhirPathTokenKind.Xor)]
    [InlineData("implies", FhirPathTokenKind.Implies)]
    [InlineData("is", FhirPathTokenKind.Is)]
    [InlineData("as", FhirPathTokenKind.As)]
    [InlineData("div", FhirPathTokenKind.Div)]
    [InlineData("mod", FhirPathTokenKind.Mod)]
    [InlineData("in", FhirPathTokenKind.In)]
    [InlineData("contains", FhirPathTokenKind.Contains)]
    public void GivenKeyword_WhenTokenizing_ThenProducesKeywordToken(string keyword, FhirPathTokenKind expectedKind)
    {
        var tokens = Tokenize(keyword);

        Assert.Single(tokens);
        Assert.Equal(expectedKind, tokens[0].Kind);
        Assert.Equal(keyword, tokens[0].ToStringValue());
    }

    [Theory]
    [InlineData("+", FhirPathTokenKind.Plus)]
    [InlineData("-", FhirPathTokenKind.Minus)]
    [InlineData("*", FhirPathTokenKind.Multiply)]
    [InlineData("/", FhirPathTokenKind.Divide)]
    [InlineData("&", FhirPathTokenKind.Ampersand)]
    [InlineData("|", FhirPathTokenKind.Union)]
    [InlineData("=", FhirPathTokenKind.Equals)]
    [InlineData("!=", FhirPathTokenKind.NotEquals)]
    [InlineData("~", FhirPathTokenKind.Equivalent)]
    [InlineData("!~", FhirPathTokenKind.NotEquivalent)]
    [InlineData("<", FhirPathTokenKind.LessThan)]
    [InlineData("<=", FhirPathTokenKind.LessThanOrEqual)]
    [InlineData(">", FhirPathTokenKind.GreaterThan)]
    [InlineData(">=", FhirPathTokenKind.GreaterThanOrEqual)]
    public void GivenOperator_WhenTokenizing_ThenProducesOperatorToken(string op, FhirPathTokenKind expectedKind)
    {
        var tokens = Tokenize(op);

        Assert.Single(tokens);
        Assert.Equal(expectedKind, tokens[0].Kind);
        Assert.Equal(op, tokens[0].ToStringValue());
    }

    [Theory]
    [InlineData("(", FhirPathTokenKind.LeftParen)]
    [InlineData(")", FhirPathTokenKind.RightParen)]
    [InlineData("[", FhirPathTokenKind.LeftBracket)]
    [InlineData("]", FhirPathTokenKind.RightBracket)]
    [InlineData("{", FhirPathTokenKind.LeftBrace)]
    [InlineData("}", FhirPathTokenKind.RightBrace)]
    [InlineData(",", FhirPathTokenKind.Comma)]
    [InlineData(".", FhirPathTokenKind.Dot)]
    public void GivenDelimiter_WhenTokenizing_ThenProducesDelimiterToken(string delimiter, FhirPathTokenKind expectedKind)
    {
        var tokens = Tokenize(delimiter);

        Assert.Single(tokens);
        Assert.Equal(expectedKind, tokens[0].Kind);
        Assert.Equal(delimiter, tokens[0].ToStringValue());
    }

    [Fact]
    public void GivenSimplePath_WhenTokenizing_ThenProducesCorrectTokenSequence()
    {
        var tokens = Tokenize("Patient.name");

        Assert.Equal(3, tokens.Length);
        Assert.Equal(FhirPathTokenKind.Identifier, tokens[0].Kind);
        Assert.Equal("Patient", tokens[0].ToStringValue());
        Assert.Equal(FhirPathTokenKind.Dot, tokens[1].Kind);
        Assert.Equal(FhirPathTokenKind.Identifier, tokens[2].Kind);
        Assert.Equal("name", tokens[2].ToStringValue());
    }

    [Fact]
    public void GivenFunctionCall_WhenTokenizing_ThenProducesCorrectTokenSequence()
    {
        var tokens = Tokenize("name.exists()");

        Assert.Equal(5, tokens.Length);
        Assert.Equal(FhirPathTokenKind.Identifier, tokens[0].Kind);
        Assert.Equal("name", tokens[0].ToStringValue());
        Assert.Equal(FhirPathTokenKind.Dot, tokens[1].Kind);
        Assert.Equal(FhirPathTokenKind.Identifier, tokens[2].Kind);
        Assert.Equal("exists", tokens[2].ToStringValue());
        Assert.Equal(FhirPathTokenKind.LeftParen, tokens[3].Kind);
        Assert.Equal(FhirPathTokenKind.RightParen, tokens[4].Kind);
    }

    [Fact]
    public void GivenBinaryExpression_WhenTokenizing_ThenProducesCorrectTokenSequence()
    {
        var tokens = Tokenize("age > 18");

        Assert.Equal(3, tokens.Length);
        Assert.Equal(FhirPathTokenKind.Identifier, tokens[0].Kind);
        Assert.Equal("age", tokens[0].ToStringValue());
        Assert.Equal(FhirPathTokenKind.GreaterThan, tokens[1].Kind);
        Assert.Equal(FhirPathTokenKind.IntegerLiteral, tokens[2].Kind);
        Assert.Equal("18", tokens[2].ToStringValue());
    }

    [Fact]
    public void GivenLogicalExpression_WhenTokenizing_ThenProducesCorrectTokenSequence()
    {
        var tokens = Tokenize("active = true and gender = 'male'");

        Assert.Equal(7, tokens.Length);
        Assert.Equal(FhirPathTokenKind.Identifier, tokens[0].Kind);
        Assert.Equal("active", tokens[0].ToStringValue());
        Assert.Equal(FhirPathTokenKind.Equals, tokens[1].Kind);
        Assert.Equal(FhirPathTokenKind.BooleanLiteral, tokens[2].Kind);
        Assert.Equal("true", tokens[2].ToStringValue());
        Assert.Equal(FhirPathTokenKind.And, tokens[3].Kind);
        Assert.Equal(FhirPathTokenKind.Identifier, tokens[4].Kind);
        Assert.Equal("gender", tokens[4].ToStringValue());
        Assert.Equal(FhirPathTokenKind.Equals, tokens[5].Kind);
        Assert.Equal(FhirPathTokenKind.StringLiteral, tokens[6].Kind);
        Assert.Equal("'male'", tokens[6].ToStringValue());
    }

    [Fact]
    public void GivenComplexFhirPathExpression_WhenTokenizing_ThenProducesCorrectTokenSequence()
    {
        var tokens = Tokenize("Patient.name.given.where($this != '')");

        Assert.Equal(12, tokens.Length);
        Assert.Equal(FhirPathTokenKind.Identifier, tokens[0].Kind); // Patient
        Assert.Equal(FhirPathTokenKind.Dot, tokens[1].Kind);
        Assert.Equal(FhirPathTokenKind.Identifier, tokens[2].Kind); // name
        Assert.Equal(FhirPathTokenKind.Dot, tokens[3].Kind);
        Assert.Equal(FhirPathTokenKind.Identifier, tokens[4].Kind); // given
        Assert.Equal(FhirPathTokenKind.Dot, tokens[5].Kind);
        Assert.Equal(FhirPathTokenKind.Identifier, tokens[6].Kind); // where
        Assert.Equal(FhirPathTokenKind.LeftParen, tokens[7].Kind);
        Assert.Equal(FhirPathTokenKind.Axis, tokens[8].Kind); // $this
        Assert.Equal(FhirPathTokenKind.NotEquals, tokens[9].Kind);
        Assert.Equal(FhirPathTokenKind.StringLiteral, tokens[10].Kind); // ''
        Assert.Equal(FhirPathTokenKind.RightParen, tokens[11].Kind);
    }

    [Fact]
    public void GivenIndexerExpression_WhenTokenizing_ThenProducesCorrectTokenSequence()
    {
        var tokens = Tokenize("name[0]");

        Assert.Equal(4, tokens.Length);
        Assert.Equal(FhirPathTokenKind.Identifier, tokens[0].Kind); // name
        Assert.Equal(FhirPathTokenKind.LeftBracket, tokens[1].Kind);
        Assert.Equal(FhirPathTokenKind.IntegerLiteral, tokens[2].Kind); // 0
        Assert.Equal(FhirPathTokenKind.RightBracket, tokens[3].Kind);
    }

    [Fact]
    public void GivenEmptySet_WhenTokenizing_ThenProducesCorrectTokenSequence()
    {
        var tokens = Tokenize("{}");

        Assert.Equal(2, tokens.Length);
        Assert.Equal(FhirPathTokenKind.LeftBrace, tokens[0].Kind);
        Assert.Equal(FhirPathTokenKind.RightBrace, tokens[1].Kind);
    }

    [Theory]
    [InlineData("issued", "issued")]
    [InlineData("DiagnosticReport.issued", "DiagnosticReport.issued")]
    [InlineData("assigned", "assigned")]
    [InlineData("integer", "integer")]
    [InlineData("organization", "organization")]
    [InlineData("android", "android")]
    public void GivenIdentifierStartingWithKeyword_WhenTokenizing_ThenProducesIdentifierToken(string expression, string expectedText)
    {
        var tokens = Tokenize(expression);

        // Should tokenize as identifiers, not keywords
        var identifiers = tokens.Where(t => t.Kind == FhirPathTokenKind.Identifier).ToArray();
        Assert.True(identifiers.Length > 0, $"Expected at least one identifier token, but got: {string.Join(", ", tokens.Select(t => $"{t.Kind}:{t.ToStringValue()}"))}");

        // For simple identifiers, should be a single token
        if (!expression.Contains('.', StringComparison.Ordinal))
        {
            Assert.Single(tokens);
            Assert.Equal(FhirPathTokenKind.Identifier, tokens[0].Kind);
            Assert.Equal(expectedText, tokens[0].ToStringValue());
        }
        else
        {
            // For paths like "DiagnosticReport.issued", verify "issued" is an identifier
            var issuedToken = tokens.Last(t => t.Kind == FhirPathTokenKind.Identifier);
            Assert.Equal("issued", issuedToken.ToStringValue());
        }
    }

    [Theory]
    [InlineData("is type")]
    [InlineData("as Type")]
    [InlineData("in collection")]
    [InlineData("name or other")]
    public void GivenKeywordFollowedBySpace_WhenTokenizing_ThenProducesKeywordToken(string expression)
    {
        var tokens = Tokenize(expression);

        // Second token should be a keyword "or", not part of first token
        // For example, "name or other" should tokenize as: Identifier("name"), Or, Identifier("other")
        // not as: Identifier("nameor"), Identifier("other")
        var keywordTokens = tokens.Where(t => t.Kind != FhirPathTokenKind.Identifier).ToArray();
        Assert.True(
            keywordTokens.Length > 0,
            $"Expected at least one keyword token, but got only identifiers: {string.Join(", ", tokens.Select(t => $"{t.Kind}:{t.ToStringValue()}"))}");
    }
}
