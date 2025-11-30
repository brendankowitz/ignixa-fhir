/* Copyright (c) 2025, Ignixa Contributors */

using Ignixa.Abstractions;
using Ignixa.FhirMappingLanguage.Expressions;

namespace Ignixa.FhirMappingLanguage.TypeSystem;

/// <summary>
/// Interface for validating types in mapping expressions.
/// </summary>
public interface ITypeValidator
{
    /// <summary>
    /// Validates a map expression's type annotations.
    /// </summary>
    /// <param name="map">The map expression to validate</param>
    /// <returns>Collection of validation errors, empty if valid</returns>
    IEnumerable<TypeValidationError> ValidateMap(MapExpression map);

    /// <summary>
    /// Checks if a type is compatible with another type.
    /// </summary>
    /// <param name="sourceType">The source type</param>
    /// <param name="targetType">The target type</param>
    /// <returns>True if compatible, false otherwise</returns>
    bool IsTypeCompatible(string sourceType, string targetType);

    /// <summary>
    /// Gets the element type from a type name.
    /// For primitives, returns the primitive type. For complex types, attempts to resolve.
    /// </summary>
    /// <param name="typeName">The type name to resolve</param>
    /// <returns>Resolved type information, or null if not found</returns>
    TypeInfo? ResolveType(string typeName);

    /// <summary>
    /// Validates that an element matches the expected type.
    /// </summary>
    /// <param name="element">The element to validate</param>
    /// <param name="expectedType">The expected type</param>
    /// <returns>Validation error if type mismatch, null if valid</returns>
    TypeValidationError? ValidateElement(IElement element, string expectedType);
}
