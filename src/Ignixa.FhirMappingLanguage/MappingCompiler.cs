/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * FHIR Mapping Language compiler.
 * Entry point for parsing and compiling mapping expressions.
 */

using Ignixa.FhirMappingLanguage.Evaluation;
using Ignixa.FhirMappingLanguage.Expressions;
using Ignixa.FhirMappingLanguage.Lexer;
using Ignixa.FhirMappingLanguage.Parser;
using Ignixa.FhirMappingLanguage.TypeSystem;
using Superpower;
using Superpower.Model;

namespace Ignixa.FhirMappingLanguage;

/// <summary>
/// Compiler for FHIR Mapping Language expressions.
/// Provides methods to parse mapping definitions and create executable mappings.
/// </summary>
public class MappingCompiler
{
    private readonly bool _preserveTrivia;
    private readonly ITypeValidator? _typeValidator;

    /// <summary>
    /// Creates a new mapping compiler.
    /// </summary>
    /// <param name="preserveTrivia">Whether to preserve whitespace and comments for round-tripping</param>
    /// <param name="typeValidator">Optional type validator for type checking</param>
    public MappingCompiler(bool preserveTrivia = false, ITypeValidator? typeValidator = null)
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

/// <summary>
/// Represents a compiled mapping ready for execution.
/// </summary>
public class CompiledMapping
{
    private readonly MapExpression _map;
    private readonly MappingEvaluator _evaluator;
    private readonly MappingContext _context;

    internal CompiledMapping(MapExpression map, MappingEvaluator evaluator, MappingContext context)
    {
        _map = map;
        _evaluator = evaluator;
        _context = context;
    }

    /// <summary>
    /// Gets the map URL.
    /// </summary>
    public string Url => _map.Url;

    /// <summary>
    /// Gets the map identifier.
    /// </summary>
    public string Identifier => _map.Identifier;

    /// <summary>
    /// Gets the groups defined in this mapping.
    /// </summary>
    public IReadOnlyList<string> Groups => _map.Groups.Select(g => g.Name).ToList();

    /// <summary>
    /// Executes the default group (first group) in the mapping.
    /// </summary>
    public void Execute()
    {
        _evaluator.Execute(_map, _context);
    }

    /// <summary>
    /// Executes a specific group by name.
    /// </summary>
    /// <param name="groupName">The name of the group to execute</param>
    public void ExecuteGroup(string groupName)
    {
        _evaluator.ExecuteGroup(_map, groupName, _context);
    }

    /// <summary>
    /// Gets the evaluation context (for setting sources/targets).
    /// </summary>
    public MappingContext Context => _context;
}

/// <summary>
/// Exception thrown when parsing fails.
/// </summary>
public class ParseException : Exception
{
    public ParseException(string message, Position position) : base(message)
    {
        Position = position;
    }

    public ParseException(string message, Position position, Exception innerException)
        : base(message, innerException)
    {
        Position = position;
    }

    public Position Position { get; }
}

/// <summary>
/// Exception thrown when type validation fails.
/// </summary>
public class TypeValidationException : Exception
{
    public TypeValidationException(string message, IEnumerable<TypeValidationError> errors)
        : base(FormatMessage(message, errors))
    {
        Errors = errors?.ToList() ?? new List<TypeValidationError>();
    }

    public IReadOnlyList<TypeValidationError> Errors { get; }

    private static string FormatMessage(string message, IEnumerable<TypeValidationError> errors)
    {
        var errorList = errors?.ToList() ?? new List<TypeValidationError>();
        if (!errorList.Any())
        {
            return message;
        }

        var formattedErrors = string.Join(Environment.NewLine, errorList.Select(e => $"  - {e}"));
        return $"{message}:{Environment.NewLine}{formattedErrors}";
    }
}
