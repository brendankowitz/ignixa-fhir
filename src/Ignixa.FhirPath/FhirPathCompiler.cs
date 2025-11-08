/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * FhirPath compiler using Superpower parser.
 * Entry point for parsing FhirPath expressions.
 */

using Ignixa.FhirPath.Expressions;
using Ignixa.FhirPath.Lexer;
using Ignixa.FhirPath.Parser;
using Superpower;

namespace Ignixa.FhirPath;

/// <summary>
/// Compiles FhirPath expression strings into abstract syntax trees.
/// This is the main entry point for parsing FhirPath expressions.
/// </summary>
public class FhirPathCompiler
{
    private readonly Tokenizer<FhirPathTokenKind> _tokenizer;
    private readonly bool _preserveTrivia;

    /// <summary>
    /// Creates a new FhirPath compiler.
    /// </summary>
    /// <param name="preserveTrivia">
    /// If true, whitespace and comments are preserved for round-tripping.
    /// If false (default), trivia is ignored for faster parsing.
    /// </param>
    public FhirPathCompiler(bool preserveTrivia = false)
    {
        _preserveTrivia = preserveTrivia;
        _tokenizer = preserveTrivia
            ? FhirPathTokenizer.CreateWithTrivia()
            : FhirPathTokenizer.Create();
    }

    /// <summary>
    /// Parses a FhirPath expression string into an abstract syntax tree.
    /// </summary>
    /// <param name="expression">The FhirPath expression to parse</param>
    /// <returns>The root Expression node of the parsed AST</returns>
    /// <exception cref="FormatException">Thrown when the expression cannot be parsed</exception>
    public Expression Parse(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            throw new ArgumentException("Expression cannot be null or whitespace", nameof(expression));

        // Tokenize
        var tokenizeResult = _tokenizer.TryTokenize(expression);
        if (!tokenizeResult.HasValue)
            throw new FormatException($"Tokenization failed: {tokenizeResult}");

        var tokens = tokenizeResult.Value;

        // Filter out trivia tokens if preserveTrivia is enabled
        // (Grammar expects only significant tokens, but we need trivia for source text extraction)
        if (_preserveTrivia)
        {
            var filteredTokens = tokens.Where(t =>
                t.Kind != FhirPathTokenKind.Whitespace &&
                t.Kind != FhirPathTokenKind.LineComment &&
                t.Kind != FhirPathTokenKind.BlockComment).ToArray();
            tokens = new Superpower.Model.TokenList<FhirPathTokenKind>(filteredTokens);
        }

        // Parse tokens into AST
        var parseResult = FhirPathGrammar.Expression.AtEnd().TryParse(tokens);
        if (!parseResult.HasValue)
            throw new FormatException($"Parsing failed: {parseResult}");

        var result = parseResult.Value;

        // Populate source text for round-tripping if trivia is preserved
        // For simplicity, store the entire input expression as the root's SourceText
        if (_preserveTrivia)
        {
            result.SourceText = expression;
        }

        return result;
    }

    /// <summary>
    /// Tries to parse a FhirPath expression string into an abstract syntax tree.
    /// </summary>
    /// <param name="expression">The FhirPath expression to parse</param>
    /// <param name="result">The parsed expression, or null if parsing failed</param>
    /// <param name="errorMessage">Error message if parsing failed, or null if successful</param>
    /// <returns>True if parsing succeeded, false otherwise</returns>
    public bool TryParse(string expression, out Expression? result, out string? errorMessage)
    {
        try
        {
            result = Parse(expression);
            errorMessage = null;
            return true;
        }
        catch (Exception ex)
        {
            result = null;
            errorMessage = ex.Message;
            return false;
        }
    }
}
