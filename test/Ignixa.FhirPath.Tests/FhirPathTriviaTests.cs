/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Unit tests for FhirPath trivia preservation (whitespace, comments).
 */

using Ignixa.FhirPath;
using Ignixa.FhirPath.Parser;
using Superpower;
using Superpower.Model;

namespace Ignixa.FhirPath.Tests;

public class FhirPathTriviaTests
{
    private readonly Tokenizer<FhirPathTokenKind> _tokenizer = FhirPathTokenizer.CreateWithTrivia();

    private Token<FhirPathTokenKind>[] Tokenize(string input)
    {
        var result = _tokenizer.TryTokenize(input);
        Assert.True(result.HasValue, $"Tokenization failed: {result}");
        return result.Value.ToArray();
    }

    [Fact]
    public void GivenWhitespace_WhenTokenizingWithTrivia_ThenPreservesWhitespace()
    {
        var tokens = Tokenize("Patient  .  name");

        Assert.Equal(5, tokens.Length);
        Assert.Equal(FhirPathTokenKind.Identifier, tokens[0].Kind);
        Assert.Equal(FhirPathTokenKind.Whitespace, tokens[1].Kind);
        Assert.Equal(FhirPathTokenKind.Dot, tokens[2].Kind);
        Assert.Equal(FhirPathTokenKind.Whitespace, tokens[3].Kind);
        Assert.Equal(FhirPathTokenKind.Identifier, tokens[4].Kind);
    }

    [Fact]
    public void GivenLineComment_WhenTokenizingWithTrivia_ThenPreservesComment()
    {
        var tokens = Tokenize("Patient // this is a comment\n.name");

        Assert.Contains(tokens, t => t.Kind == FhirPathTokenKind.LineComment);
        Assert.Contains(tokens, t => t.Kind == FhirPathTokenKind.Identifier && t.ToStringValue() == "Patient");
        Assert.Contains(tokens, t => t.Kind == FhirPathTokenKind.Identifier && t.ToStringValue() == "name");
    }

    [Fact]
    public void GivenBlockComment_WhenTokenizingWithTrivia_ThenPreservesComment()
    {
        var tokens = Tokenize("Patient /* comment */ .name");

        Assert.Contains(tokens, t => t.Kind == FhirPathTokenKind.BlockComment);
        Assert.Contains(tokens, t => t.Kind == FhirPathTokenKind.Identifier && t.ToStringValue() == "Patient");
        Assert.Contains(tokens, t => t.Kind == FhirPathTokenKind.Identifier && t.ToStringValue() == "name");
    }

    [Fact]
    public void GivenComplexExpressionWithTrivia_WhenTokenizing_ThenPreservesAllTrivia()
    {
        var input = @"Patient  // patient resource
            .name  /* the name */
            .given";

        var tokens = Tokenize(input);

        // Should have: Patient, whitespace, comment, whitespace, dot, name, whitespace, comment, whitespace, dot, given
        Assert.Contains(tokens, t => t.Kind == FhirPathTokenKind.Identifier && t.ToStringValue() == "Patient");
        Assert.Contains(tokens, t => t.Kind == FhirPathTokenKind.LineComment);
        Assert.Contains(tokens, t => t.Kind == FhirPathTokenKind.BlockComment);
        Assert.Contains(tokens, t => t.Kind == FhirPathTokenKind.Identifier && t.ToStringValue() == "name");
        Assert.Contains(tokens, t => t.Kind == FhirPathTokenKind.Identifier && t.ToStringValue() == "given");
    }

    [Fact]
    public void GivenExpressionWithoutTrivia_WhenTokenizingWithTrivia_ThenTokenizesNormally()
    {
        var tokens = Tokenize("Patient.name");

        Assert.Equal(3, tokens.Length);
        Assert.Equal(FhirPathTokenKind.Identifier, tokens[0].Kind);
        Assert.Equal(FhirPathTokenKind.Dot, tokens[1].Kind);
        Assert.Equal(FhirPathTokenKind.Identifier, tokens[2].Kind);
    }
}
