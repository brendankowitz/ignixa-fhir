/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Standard transform functions for FHIR Mapping Language.
 * Implements the built-in transforms defined in the FHIR specification.
 */

namespace Ignixa.FhirMappingLanguage.Transforms;

/// <summary>
/// Registry of standard transform functions.
/// </summary>
public static class StandardTransforms
{
    private static readonly Dictionary<string, ITransformFunction> _functions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Core functions
        ["create"] = new CreateTransform(),
        ["copy"] = new CopyTransform(),
        ["uuid"] = new UuidTransform(),

        // String functions
        ["truncate"] = new TruncateTransform(),
        ["escape"] = new EscapeTransform(),
        ["append"] = new AppendTransform(),

        // Type conversion functions
        ["cast"] = new CastTransform(),
        ["evaluate"] = new EvaluateTransform(),

        // FHIR-specific functions
        ["cc"] = new CodeableConceptTransform(),
        ["c"] = new CodingTransform(),
        ["qty"] = new QuantityTransform(),
        ["id"] = new IdentifierTransform(),
        ["cp"] = new ContactPointTransform(),
        ["reference"] = new ReferenceTransform(),

        // Terminology functions
        ["translate"] = new TranslateTransform(),

        // Utility functions
        ["pointer"] = new PointerTransform(),
        ["dateOp"] = new DateOpTransform()
    };

    /// <summary>
    /// Registers a transform function.
    /// </summary>
    public static void Register(ITransformFunction function)
    {
        ArgumentNullException.ThrowIfNull(function);
        _functions[function.Name] = function;
    }

    /// <summary>
    /// Gets a transform function by name.
    /// </summary>
    public static ITransformFunction? Get(string name)
    {
        _functions.TryGetValue(name, out var function);
        return function;
    }

    /// <summary>
    /// Gets all registered transform functions.
    /// </summary>
    public static IEnumerable<ITransformFunction> All() => _functions.Values;
}
