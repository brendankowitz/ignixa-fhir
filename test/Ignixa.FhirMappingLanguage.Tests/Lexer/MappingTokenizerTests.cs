/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Comprehensive unit tests for FHIR Mapping Language lexer/tokenizer.
 */

using Shouldly;
using Ignixa.FhirMappingLanguage.Lexer;
using Xunit;

namespace Ignixa.FhirMappingLanguage.Tests.Lexer;

public class MappingTokenizerTests
{
    #region Helper Methods

    /// <summary>
    /// Unescapes a string token value by removing quotes and processing escape sequences.
    /// Mirrors the logic in MappingGrammar.UnescapeString.
    /// </summary>
    private static string UnescapeStringToken(string tokenValue)
    {
        if (tokenValue.StartsWith('\'') && tokenValue.EndsWith('\''))
        {
            tokenValue = tokenValue.Substring(1, tokenValue.Length - 2);
            tokenValue = tokenValue.Replace("''", "'", StringComparison.Ordinal);
        }
        return tokenValue;
    }

    #endregion

    #region Keyword Tests

    [Theory]
    [InlineData("map", MappingTokenKind.Map)]
    [InlineData("uses", MappingTokenKind.Uses)]
    [InlineData("as", MappingTokenKind.As)]
    [InlineData("alias", MappingTokenKind.Alias)]
    [InlineData("imports", MappingTokenKind.Imports)]
    [InlineData("group", MappingTokenKind.Group)]
    [InlineData("extends", MappingTokenKind.Extends)]
    [InlineData("default", MappingTokenKind.Default)]
    [InlineData("where", MappingTokenKind.Where)]
    [InlineData("check", MappingTokenKind.Check)]
    [InlineData("log", MappingTokenKind.Log)]
    [InlineData("then", MappingTokenKind.Then)]
    [InlineData("source", MappingTokenKind.Source)]
    [InlineData("target", MappingTokenKind.Target)]
    [InlineData("queried", MappingTokenKind.Queried)]
    [InlineData("produced", MappingTokenKind.Produced)]
    [InlineData("conceptMap", MappingTokenKind.ConceptMap)]
    [InlineData("prefix", MappingTokenKind.Prefix)]
    [InlineData("types", MappingTokenKind.Types)]
    [InlineData("type", MappingTokenKind.Type)]
    [InlineData("first", MappingTokenKind.First)]
    [InlineData("not_first", MappingTokenKind.NotFirst)]
    [InlineData("last", MappingTokenKind.Last)]
    [InlineData("not_last", MappingTokenKind.NotLast)]
    [InlineData("only_one", MappingTokenKind.OnlyOne)]
    [InlineData("share", MappingTokenKind.Share)]
    [InlineData("single", MappingTokenKind.Single)]
    public void GivenKeyword_WhenTokenizing_ThenReturnsCorrectTokenKind(string keyword, MappingTokenKind expectedKind)
    {
        // Arrange
        var tokenizer = MappingTokenizer.Create();

        // Act
        var result = tokenizer.Tokenize(keyword);

        // Assert
        result.Count().ShouldBe(1);
        result.First().Kind.ShouldBe(expectedKind);
    }

    [Theory]
    [InlineData("Map", MappingTokenKind.Identifier)] // Case sensitive
    [InlineData("MAP", MappingTokenKind.Identifier)]
    [InlineData("GROUP", MappingTokenKind.Identifier)]
    public void GivenKeywordWithWrongCase_WhenTokenizing_ThenReturnsIdentifier(string input, MappingTokenKind expectedKind)
    {
        // Arrange
        var tokenizer = MappingTokenizer.Create();

        // Act
        var result = tokenizer.Tokenize(input);

        // Assert
        result.Count().ShouldBe(1);
        result.First().Kind.ShouldBe(expectedKind);
    }

    [Fact]
    public void GivenKeywordAsPartOfIdentifier_WhenTokenizing_ThenReturnsIdentifier()
    {
        // Arrange
        var tokenizer = MappingTokenizer.Create();

        // Act
        var result = tokenizer.Tokenize("mapName");

        // Assert
        result.Count().ShouldBe(1);
        result.First().Kind.ShouldBe(MappingTokenKind.Identifier);
        result.First().ToStringValue().ShouldBe("mapName");
    }

    #endregion

    #region Operator Tests

    [Theory]
    [InlineData("->", MappingTokenKind.Arrow)]
    [InlineData(":", MappingTokenKind.Colon)]
    [InlineData("=", MappingTokenKind.Equals)]
    [InlineData(".", MappingTokenKind.Dot)]
    [InlineData(",", MappingTokenKind.Comma)]
    [InlineData(";", MappingTokenKind.Semicolon)]
    [InlineData("(", MappingTokenKind.LeftParen)]
    [InlineData(")", MappingTokenKind.RightParen)]
    [InlineData("{", MappingTokenKind.LeftBrace)]
    [InlineData("}", MappingTokenKind.RightBrace)]
    [InlineData("<", MappingTokenKind.LeftAngle)]
    [InlineData(">", MappingTokenKind.RightAngle)]
    [InlineData("[", MappingTokenKind.LeftBracket)]
    [InlineData("]", MappingTokenKind.RightBracket)]
    public void GivenOperator_WhenTokenizing_ThenReturnsCorrectToken(string op, MappingTokenKind expectedKind)
    {
        // Arrange
        var tokenizer = MappingTokenizer.Create();

        // Act
        var result = tokenizer.Tokenize(op);

        // Assert
        result.Count().ShouldBe(1);
        result.First().Kind.ShouldBe(expectedKind);
    }

    // NOTE: DoubleColon (::) test removed - not part of FHIR Mapping Language spec

    #endregion

    #region Literal Tests

    [Theory]
    [InlineData("'hello'", "hello")]
    [InlineData("'hello world'", "hello world")]
    [InlineData("''", "")]
    [InlineData("'don''t'", "don't")] // SQL-style escaping
    public void GivenStringLiteral_WhenTokenizing_ThenReturnsStringToken(string input, string expected)
    {
        // Arrange
        var tokenizer = MappingTokenizer.Create();

        // Act
        var result = tokenizer.Tokenize(input);

        // Assert
        result.Count().ShouldBe(1);
        result.First().Kind.ShouldBe(MappingTokenKind.StringLiteral);
        UnescapeStringToken(result.First().ToStringValue()).ShouldBe(expected);
    }

    [Theory]
    [InlineData("42")]
    [InlineData("0")]
    [InlineData("999999")]
    public void GivenIntegerLiteral_WhenTokenizing_ThenReturnsIntegerToken(string input)
    {
        // Arrange
        var tokenizer = MappingTokenizer.Create();

        // Act
        var result = tokenizer.Tokenize(input);

        // Assert
        result.Count().ShouldBe(1);
        result.First().Kind.ShouldBe(MappingTokenKind.IntegerLiteral);
        result.First().ToStringValue().ShouldBe(input);
    }

    [Theory]
    [InlineData("3.14")]
    [InlineData("0.5")]
    [InlineData("123.456")]
    public void GivenDecimalLiteral_WhenTokenizing_ThenReturnsDecimalToken(string input)
    {
        // Arrange
        var tokenizer = MappingTokenizer.Create();

        // Act
        var result = tokenizer.Tokenize(input);

        // Assert
        result.Count().ShouldBe(1);
        result.First().Kind.ShouldBe(MappingTokenKind.DecimalLiteral);
    }

    [Theory]
    [InlineData("true", MappingTokenKind.True)]
    [InlineData("false", MappingTokenKind.False)]
    public void GivenBooleanLiteral_WhenTokenizing_ThenReturnsBooleanToken(string input, MappingTokenKind expectedKind)
    {
        // Arrange
        var tokenizer = MappingTokenizer.Create();

        // Act
        var result = tokenizer.Tokenize(input);

        // Assert
        result.Count().ShouldBe(1);
        result.First().Kind.ShouldBe(expectedKind);
    }

    #endregion

    #region Identifier Tests

    [Theory]
    [InlineData("patient")]
    [InlineData("myVariable")]
    [InlineData("_underscore")]
    [InlineData("name123")]
    [InlineData("camelCase")]
    [InlineData("PascalCase")]
    public void GivenValidIdentifier_WhenTokenizing_ThenReturnsIdentifierToken(string identifier)
    {
        // Arrange
        var tokenizer = MappingTokenizer.Create();

        // Act
        var result = tokenizer.Tokenize(identifier);

        // Assert
        result.Count().ShouldBe(1);
        result.First().Kind.ShouldBe(MappingTokenKind.Identifier);
        result.First().ToStringValue().ShouldBe(identifier);
    }

    [Theory]
    [InlineData("`escaped identifier`")]
    [InlineData("\"quoted identifier\"")]
    public void GivenDelimitedIdentifier_WhenTokenizing_ThenReturnsDelimitedIdentifierToken(string identifier)
    {
        // Arrange
        var tokenizer = MappingTokenizer.Create();

        // Act
        var result = tokenizer.Tokenize(identifier);

        // Assert
        result.Count().ShouldBe(1);
        result.First().Kind.ShouldBe(MappingTokenKind.DelimitedIdentifier);
    }

    #endregion

    #region URL Tests

    [Theory]
    [InlineData("http://example.org/fhir/StructureMap/Example")]
    [InlineData("https://hl7.org/fhir/StructureDefinition/Patient")]
    [InlineData("urn:uuid:12345678-1234-1234-1234-123456789abc")]
    public void GivenURL_WhenTokenizing_ThenReturnsUrlToken(string url)
    {
        // Arrange
        var tokenizer = MappingTokenizer.Create();

        // Act
        var result = tokenizer.Tokenize(url);

        // Assert
        result.Count().ShouldBe(1);
        result.First().Kind.ShouldBe(MappingTokenKind.Url);
        result.First().ToStringValue().ShouldBe(url);
    }

    #endregion

    #region Comment Tests

    [Fact]
    public void GivenLineComment_WhenTokenizingWithTrivia_ThenReturnsCommentToken()
    {
        // Arrange
        var tokenizer = MappingTokenizer.CreateWithTrivia();
        var input = "// this is a comment";

        // Act
        var result = tokenizer.Tokenize(input);

        // Assert
        result.ShouldContain(t => t.Kind == MappingTokenKind.LineComment);
    }

    [Fact]
    public void GivenBlockComment_WhenTokenizingWithTrivia_ThenReturnsCommentToken()
    {
        // Arrange
        var tokenizer = MappingTokenizer.CreateWithTrivia();
        var input = "/* block comment */";

        // Act
        var result = tokenizer.Tokenize(input);

        // Assert
        result.ShouldContain(t => t.Kind == MappingTokenKind.BlockComment);
    }

    [Fact]
    public void GivenLineComment_WhenTokenizingWithoutTrivia_ThenIgnoresComment()
    {
        // Arrange
        var tokenizer = MappingTokenizer.Create();
        var input = "map // comment";

        // Act
        var result = tokenizer.Tokenize(input).ToList();

        // Assert
        result.Count.ShouldBe(1);
        result[0].Kind.ShouldBe(MappingTokenKind.Map);
    }

    #endregion

    #region Whitespace Tests

    [Fact]
    public void GivenWhitespace_WhenTokenizingWithTrivia_ThenReturnsWhitespaceToken()
    {
        // Arrange
        var tokenizer = MappingTokenizer.CreateWithTrivia();
        var input = "map   group";

        // Act
        var result = tokenizer.Tokenize(input).ToList();

        // Assert
        result.ShouldContain(t => t.Kind == MappingTokenKind.Whitespace);
    }

    [Fact]
    public void GivenWhitespace_WhenTokenizingWithoutTrivia_ThenIgnoresWhitespace()
    {
        // Arrange
        var tokenizer = MappingTokenizer.Create();
        var input = "map   group";

        // Act
        var result = tokenizer.Tokenize(input).ToList();

        // Assert
        result.Count.ShouldBe(2);
        result.ShouldNotContain(t => t.Kind == MappingTokenKind.Whitespace);
    }

    #endregion

    #region Complex Expression Tests

    [Fact]
    public void GivenCompleteMapDeclaration_WhenTokenizing_ThenReturnsAllTokens()
    {
        // Arrange
        var tokenizer = MappingTokenizer.Create();
        var input = "map 'http://example.org' = 'ExampleMap'";

        // Act
        var result = tokenizer.Tokenize(input).ToList();

        // Assert
        result.Count.ShouldBe(4); // map, 'url', =, 'name'
        result[0].Kind.ShouldBe(MappingTokenKind.Map);
        result[1].Kind.ShouldBe(MappingTokenKind.StringLiteral);
        result[2].Kind.ShouldBe(MappingTokenKind.Equals);
        result[3].Kind.ShouldBe(MappingTokenKind.StringLiteral);
    }

    [Fact]
    public void GivenUsesDeclaration_WhenTokenizing_ThenReturnsAllTokens()
    {
        // Arrange
        var tokenizer = MappingTokenizer.Create();
        var input = "uses 'http://hl7.org/fhir/StructureDefinition/Patient' alias Patient as source";

        // Act
        var result = tokenizer.Tokenize(input).ToList();

        // Assert
        result[0].Kind.ShouldBe(MappingTokenKind.Uses);
        result[1].Kind.ShouldBe(MappingTokenKind.StringLiteral);
        result[2].Kind.ShouldBe(MappingTokenKind.Alias);
        result[3].Kind.ShouldBe(MappingTokenKind.Identifier);
        result[4].Kind.ShouldBe(MappingTokenKind.As);
        result[5].Kind.ShouldBe(MappingTokenKind.Source);
    }

    [Fact]
    public void GivenGroupDeclaration_WhenTokenizing_ThenReturnsAllTokens()
    {
        // Arrange
        var tokenizer = MappingTokenizer.Create();
        var input = "group PatientToBundle(source src : Patient, target tgt : Bundle)";

        // Act
        var result = tokenizer.Tokenize(input).ToList();

        // Assert
        result[0].Kind.ShouldBe(MappingTokenKind.Group);
        result[1].Kind.ShouldBe(MappingTokenKind.Identifier); // PatientToBundle
        result[2].Kind.ShouldBe(MappingTokenKind.LeftParen);
        result[3].Kind.ShouldBe(MappingTokenKind.Source);
        result[4].Kind.ShouldBe(MappingTokenKind.Identifier); // src
        result[5].Kind.ShouldBe(MappingTokenKind.Colon); // Type annotation uses single colon
        result[6].Kind.ShouldBe(MappingTokenKind.Identifier); // Patient
        result[7].Kind.ShouldBe(MappingTokenKind.Comma);
    }

    [Fact]
    public void GivenRuleWithArrow_WhenTokenizing_ThenReturnsAllTokens()
    {
        // Arrange
        var tokenizer = MappingTokenizer.Create();
        var input = "src.name -> tgt.name";

        // Act
        var result = tokenizer.Tokenize(input).ToList();

        // Assert
        result[0].Kind.ShouldBe(MappingTokenKind.Identifier); // src
        result[1].Kind.ShouldBe(MappingTokenKind.Dot);
        result[2].Kind.ShouldBe(MappingTokenKind.Identifier); // name
        result[3].Kind.ShouldBe(MappingTokenKind.Arrow);
        result[4].Kind.ShouldBe(MappingTokenKind.Identifier); // tgt
        result[5].Kind.ShouldBe(MappingTokenKind.Dot);
        result[6].Kind.ShouldBe(MappingTokenKind.Identifier); // name
    }

    #endregion

    #region Error Handling Tests

    [Theory]
    [InlineData("'unterminated string")]
    [InlineData("\"unterminated identifier")]
    public void GivenUnterminatedString_WhenTokenizing_ThenThrowsException(string input)
    {
        // Arrange
        var tokenizer = MappingTokenizer.Create();

        // Act & Assert
        var act = () => tokenizer.Tokenize(input).ToList();
        Should.Throw<Exception>(act);
    }

    #endregion
}
