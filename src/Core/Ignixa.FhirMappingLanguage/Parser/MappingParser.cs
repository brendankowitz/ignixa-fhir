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
using Superpower;
using Superpower.Model;

namespace Ignixa.FhirMappingLanguage.Parser;

/// <summary>
/// Compiler for FHIR Mapping Language expressions.
/// Provides methods to parse mapping definitions and create executable mappings.
/// </summary>
public class MappingParser
{
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
    /// <exception cref="ParseException">Thrown when the mapping text cannot be parsed</exception>
    public MapExpression Parse(string mappingText)
    {
        if (string.IsNullOrWhiteSpace(mappingText))
        {
            throw new ArgumentException("Mapping text cannot be null or empty", nameof(mappingText));
        }

        try
        {
            // Tokenize
            var tokenizer = _preserveTrivia
                ? MappingTokenizer.CreateWithTrivia()
                : MappingTokenizer.Create();

            var tokens = tokenizer.Tokenize(mappingText);

            // Parse
            var result = MappingGrammar.Map.AtEnd().TryParse(tokens);

            if (!result.HasValue)
            {
                throw new ParseException(
                    $"Failed to parse mapping expression at line {result.ErrorPosition.Line}, column {result.ErrorPosition.Column}: {result.FormatErrorMessageFragment()}",
                    result.ErrorPosition);
            }

            return result.Value;
        }
        catch (ParseException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ParseException($"Failed to parse mapping expression: {ex.Message}", Position.Zero, ex);
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
