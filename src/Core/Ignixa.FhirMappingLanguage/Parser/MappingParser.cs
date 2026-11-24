/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * FHIR Mapping Language compiler.
 * Entry point for parsing and compiling mapping expressions.
 */

using Ignixa.FhirMappingLanguage.Evaluation;
using Ignixa.FhirMappingLanguage.Expressions;
using Ignixa.FhirMappingLanguage.Lexer;
using Ignixa.FhirMappingLanguage.TypeSystem;
using Ignixa.FhirPath;
using Superpower;
using Superpower.Model;

namespace Ignixa.FhirMappingLanguage.Parser;

/// <summary>
/// Compiler for FHIR Mapping Language expressions.
/// Provides methods to parse mapping definitions and create executable mappings.
/// </summary>
public class MappingParser
{
    /// <summary>
    /// The longest mapping text, in characters, that <see cref="Parse"/> accepts.
    /// </summary>
    public const int MaxMappingTextLength = 1_048_576;

    /// <summary>
    /// The deepest brace nesting (<c>group { ... then { ... } }</c>) that <see cref="Parse"/> accepts.
    /// </summary>
    /// <remarks>
    /// Each nested rule set costs the grammar several kilobytes of stack; the bound keeps every admitted map
    /// well inside a 1 MB thread stack.
    /// </remarks>
    public const int MaxNestingDepth = 32;

    private readonly bool _preserveTrivia;
    private readonly ITypeValidator? _typeValidator;

    /// <summary>
    /// Creates a new mapping compiler.
    /// </summary>
    /// <param name="preserveTrivia">Whether to preserve whitespace and comments for round-tripping</param>
    /// <param name="typeValidator">Optional type validator for type checking</param>
    public MappingParser(bool preserveTrivia = false, ITypeValidator? typeValidator = null)
    {
        _preserveTrivia = preserveTrivia;
        _typeValidator = typeValidator;
    }

    /// <summary>
    /// Parses a FHIR Mapping Language expression into an abstract syntax tree.
    /// </summary>
    /// <param name="mappingText">The mapping language text to parse</param>
    /// <returns>The parsed MapExpression</returns>
    /// <exception cref="ParseException">
    /// Thrown when the mapping text cannot be parsed, is longer than <see cref="MaxMappingTextLength"/>, or nests
    /// braces deeper than <see cref="MaxNestingDepth"/> or the current thread's stack allows.
    /// </exception>
    public MapExpression Parse(string mappingText)
    {
        if (string.IsNullOrWhiteSpace(mappingText))
        {
            throw new ArgumentException("Mapping text cannot be null or empty", nameof(mappingText));
        }

        if (mappingText.Length > MaxMappingTextLength)
        {
            throw new ParseException(
                $"Mapping text is {mappingText.Length} characters long; the maximum is {MaxMappingTextLength}.",
                Position.Zero);
        }

        try
        {
            // Tokenize
            var tokenizer = _preserveTrivia
                ? MappingTokenizer.CreateWithTrivia()
                : MappingTokenizer.Create();

            var tokens = tokenizer.Tokenize(mappingText);

            // Nesting is bounded on the token list before the recursive grammar sees it; the grammar also checks
            // the stack at every nested rule set, which covers threads too small for this bound.
            RejectExcessiveNesting(tokens);

            var result = MappingGrammar.Map.AtEnd().TryParse(tokens);

            if (!result.HasValue)
            {
                throw new ParseException(
                    $"Failed to parse mapping expression at line {result.ErrorPosition.Line}, column {result.ErrorPosition.Column}: {result.FormatErrorMessageFragment()}",
                    result.ErrorPosition);
            }

            if (result.Value.Groups.Count == 0)
            {
                throw new ParseException(
                    "The input has no group declarations; at least one group is required.",
                    Position.Zero);
            }

            return result.Value;
        }
        catch (ParseException)
        {
            throw;
        }
        catch (InsufficientExecutionStackException ex)
        {
            throw new ParseException(
                "Mapping text is nested too deeply to parse on the current thread's stack.", Position.Zero, ex);
        }
        catch (Exception ex) when (!ExceptionFilters.IsCritical(ex))
        {
            throw new ParseException($"Failed to parse mapping expression: {ex.Message}", Position.Zero, ex);
        }
    }

    private static void RejectExcessiveNesting(TokenList<MappingTokenKind> tokens)
    {
        // Only braces recurse in the mapping grammar; parenthesised FHIRPath is collected iteratively and bounded
        // by the FHIRPath parser when compiled.
        var depth = 0;
        foreach (var token in tokens)
        {
            if (token.Kind == MappingTokenKind.LeftBrace && ++depth > MaxNestingDepth)
            {
                throw new ParseException(
                    $"Mapping text nests braces too deeply at line {token.Position.Line}, column {token.Position.Column}; "
                    + $"the maximum nesting depth is {MaxNestingDepth}.",
                    token.Position);
            }

            if (token.Kind == MappingTokenKind.RightBrace)
            {
                // Unbalanced closers are the grammar's to report; only the high-water mark matters here.
                depth = Math.Max(0, depth - 1);
            }
        }
    }

    /// <summary>
    /// Creates a mapping evaluator for executing a parsed mapping.
    /// </summary>
    /// <returns>A new MappingEvaluator instance</returns>
    public MappingEvaluator CreateEvaluator()
    {
        return new MappingEvaluator();
    }

    /// <summary>
    /// Validates a parsed mapping for type errors.
    /// </summary>
    /// <param name="map">The parsed map expression</param>
    /// <returns>Collection of validation errors, empty if valid</returns>
    public IEnumerable<TypeValidationError> Validate(MapExpression map)
    {
        if (_typeValidator == null)
        {
            return Enumerable.Empty<TypeValidationError>();
        }

        return _typeValidator.ValidateMap(map);
    }

    /// <summary>
    /// Convenience method to parse and compile a mapping in one step.
    /// </summary>
    /// <param name="mappingText">The mapping language text</param>
    /// <param name="context">The evaluation context</param>
    /// <param name="validateTypes">Whether to validate types (throws on type errors if true)</param>
    /// <returns>The compiled map that can be executed</returns>
    /// <exception cref="TypeValidationException">Thrown when type validation fails and validateTypes is true</exception>
    public CompiledMapping Compile(string mappingText, MappingContext? context = null, bool validateTypes = false)
    {
        var map = Parse(mappingText);

        // Validate types if requested
        if (validateTypes && _typeValidator != null)
        {
            var errors = Validate(map).ToList();
            if (errors.Any())
            {
                throw new TypeValidationException("Type validation failed", errors);
            }
        }

        var evaluator = CreateEvaluator();
        context ??= new MappingContext();

        return new CompiledMapping(map, evaluator, context);
    }
}
