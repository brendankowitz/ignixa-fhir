/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * FhirPath compiler using Superpower parser.
 * Entry point for parsing FhirPath expressions.
 *
 * Phase 7: Refactored to use visitor pattern for AST construction.
 * Parse tree is built first, then converted to AST via visitors.
 */

using Ignixa.FhirPath.Expressions;
using Ignixa.FhirPath.Parsing;
using Ignixa.FhirPath.Parsing.ParseTree;
using Superpower;

namespace Ignixa.FhirPath.Parser;

/// <summary>
/// Compiles FhirPath expression strings into abstract syntax trees.
/// This is the main entry point for parsing FhirPath expressions.
/// </summary>
/// <remarks>
/// <para>
/// <b>Architecture (Phase 7):</b>
/// </para>
/// <para>
/// The parser now uses a two-phase compilation approach:
/// </para>
/// <list type="number">
///   <item><description>Parse: Convert tokens into a parse tree (syntactic structure)</description></item>
///   <item><description>Build: Convert parse tree into AST via visitor pattern</description></item>
/// </list>
/// <para>
/// This separation enables:
/// </para>
/// <list type="bullet">
///   <item><description>Multiple compilation targets (optimized vs debug AST)</description></item>
///   <item><description>Better error recovery</description></item>
///   <item><description>Easier AST transformations</description></item>
///   <item><description>Tooling support (syntax highlighting, IDE integration)</description></item>
/// </list>
/// </remarks>
public class FhirPathParser
{
    private readonly Tokenizer<FhirPathTokenKind> _tokenizer;
    private readonly CompilationOptions _options;

    /// <summary>
    /// Creates a new FhirPath compiler with default options.
    /// </summary>
    /// <param name="preserveTrivia">
    /// If true, whitespace and comments are preserved for round-tripping.
    /// If false (default), trivia is ignored for faster parsing.
    /// </param>
    public FhirPathParser(bool preserveTrivia = false)
        : this(preserveTrivia ? CompilationOptions.Debug : CompilationOptions.Default)
    {
    }

    /// <summary>
    /// Creates a new FhirPath compiler with specified compilation options.
    /// </summary>
    /// <param name="options">Compilation options controlling optimization and output</param>
    public FhirPathParser(CompilationOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _tokenizer = options.PreserveTrivia
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
        var parseTree = ParseToTree(expression);
        return BuildAst(parseTree, expression);
    }

    /// <summary>
    /// Parses a FhirPath expression string into a parse tree.
    /// Use this for tooling, analysis, or custom AST building.
    /// </summary>
    /// <param name="expression">The FhirPath expression to parse</param>
    /// <returns>The root ParseNode of the parse tree</returns>
    /// <exception cref="FormatException">Thrown when the expression cannot be parsed</exception>
    internal ParseNode ParseToTree(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            throw new ArgumentException("Expression cannot be null or whitespace", nameof(expression));

        var tokenizeResult = _tokenizer.TryTokenize(expression);
        if (!tokenizeResult.HasValue)
            throw new FormatException($"Tokenization failed: {tokenizeResult}");

        var tokens = tokenizeResult.Value;

        if (_options.PreserveTrivia)
        {
            var filteredTokens = tokens.Where(t =>
                t.Kind != FhirPathTokenKind.Whitespace &&
                t.Kind != FhirPathTokenKind.LineComment &&
                t.Kind != FhirPathTokenKind.BlockComment).ToArray();
            tokens = new Superpower.Model.TokenList<FhirPathTokenKind>(filteredTokens);
        }

        var parseResult = FhirPathParseTreeGrammar.Expression.AtEnd().TryParse(tokens);
        if (!parseResult.HasValue)
            throw new FormatException($"Parsing failed: {parseResult}");

        return parseResult.Value;
    }

    /// <summary>
    /// Converts a parse tree into an AST using the configured visitor.
    /// </summary>
    /// <param name="parseTree">The parse tree to convert</param>
    /// <param name="sourceExpression">The original source expression (for trivia preservation)</param>
    /// <returns>The root Expression node of the AST</returns>
    internal Expression BuildAst(ParseNode parseTree, string? sourceExpression = null)
    {
        var context = new AstBuildContext
        {
            PreserveTrivia = _options.PreserveTrivia,
            SourceExpression = sourceExpression
        };

        IParseTreeVisitor<AstBuildContext, Expression> builder = _options.Optimize
            ? new OptimizingAstBuilder()
            : new AstBuilder();

        var result = parseTree.Accept(builder, context);

        if (_options.PreserveTrivia && sourceExpression is not null)
        {
            result.SourceText = sourceExpression;
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

    /// <summary>
    /// Tries to parse a FhirPath expression string into a parse tree.
    /// </summary>
    /// <param name="expression">The FhirPath expression to parse</param>
    /// <param name="result">The parse tree, or null if parsing failed</param>
    /// <param name="errorMessage">Error message if parsing failed, or null if successful</param>
    /// <returns>True if parsing succeeded, false otherwise</returns>
    internal bool TryParseToTree(string expression, out ParseNode? result, out string? errorMessage)
    {
        try
        {
            result = ParseToTree(expression);
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
