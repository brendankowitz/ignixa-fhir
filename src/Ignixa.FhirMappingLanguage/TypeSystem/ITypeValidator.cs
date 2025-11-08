/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Type validation interfaces for FHIR Mapping Language.
 */

using Ignixa.FhirMappingLanguage.Expressions;
using Ignixa.Abstractions;

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
    TypeValidationError? ValidateElement(ITypedElement element, string expectedType);
}

/// <summary>
/// Represents a type validation error.
/// </summary>
public class TypeValidationError
{
    public TypeValidationError(string message, ISourcePositionInfo? location = null)
    {
        Message = message ?? throw new ArgumentNullException(nameof(message));
        Location = location;
    }

    public string Message { get; }
    public ISourcePositionInfo? Location { get; }

    public override string ToString()
    {
        if (Location != null)
        {
            return $"{Location.LineNumber}:{Location.LinePosition} - {Message}";
        }
        return Message;
    }
}

/// <summary>
/// Represents resolved type information.
/// </summary>
public class TypeInfo
{
    public TypeInfo(string name, TypeCategory category, string? baseType = null)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Category = category;
        BaseType = baseType;
    }

    public string Name { get; }
    public TypeCategory Category { get; }
    public string? BaseType { get; }

    /// <summary>
    /// Whether this type is a primitive type.
    /// </summary>
    public bool IsPrimitive => Category == TypeCategory.Primitive;

    /// <summary>
    /// Whether this type is a complex type.
    /// </summary>
    public bool IsComplex => Category == TypeCategory.Complex || Category == TypeCategory.Resource;

    /// <summary>
    /// Whether this type is a resource type.
    /// </summary>
    public bool IsResource => Category == TypeCategory.Resource;
}

/// <summary>
/// Category of FHIR type.
/// </summary>
public enum TypeCategory
{
    /// <summary>
    /// Primitive type (string, integer, decimal, boolean, etc.)
    /// </summary>
    Primitive,

    /// <summary>
    /// Complex data type (HumanName, Address, CodeableConcept, etc.)
    /// </summary>
    Complex,

    /// <summary>
    /// Resource type (Patient, Observation, etc.)
    /// </summary>
    Resource,

    /// <summary>
    /// Unknown or unresolved type
    /// </summary>
    Unknown
}
