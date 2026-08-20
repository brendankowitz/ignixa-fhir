// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;

namespace Ignixa.FhirPath.Visitors;

/// <summary>
/// Represents a single type node in the FhirPath type inference context.
/// Wraps IType from Ignixa schema with additional path and collection tracking.
/// </summary>
/// <remarks>
/// This struct is designed to be lightweight and efficient for passing type
/// information through the expression visitor. It tracks:
/// - The underlying IType from the schema
/// - Whether this is a collection or single value
/// - The definitional path for error reporting
/// </remarks>
public readonly struct FhirPathType : IEquatable<FhirPathType>
{
    /// <summary>
    /// The display name of a type whose runtime shape cannot be determined statically.
    /// </summary>
    /// <remarks>
    /// Deliberately not a legal FHIR type name and deliberately not <c>"unknown"</c>: that is the string
    /// <see cref="TypeName"/> falls back to for <c>default(FhirPathType)</c>, and any collision with it lets
    /// type-name matching mistake a default-valued struct for a genuine indeterminate type.
    /// </remarks>
    public const string IndeterminateTypeName = "?";

    /// <summary>
    /// Creates a new FhirPathType from an IType definition.
    /// </summary>
    /// <param name="type">The FHIR type definition from schema</param>
    /// <param name="isCollection">Whether this is a collection (max cardinality > 1)</param>
    /// <param name="path">Optional definitional path for error reporting</param>
    public FhirPathType(IType type, bool isCollection = false, string? path = null)
        : this(type ?? throw new ArgumentNullException(nameof(type)), null, isCollection, path, isUnknown: false, isSystemValue: false)
    {
    }

    /// <summary>
    /// Creates a new FhirPathType from a type name (for primitives or when IType is not available).
    /// </summary>
    /// <param name="typeName">The FHIR type name</param>
    /// <param name="isCollection">Whether this is a collection</param>
    /// <param name="path">Optional definitional path for error reporting</param>
    /// <param name="isSystemValue">Whether the value this type describes is a System-namespace value</param>
    public FhirPathType(string typeName, bool isCollection = false, string? path = null, bool isSystemValue = false)
        : this(null, typeName ?? throw new ArgumentNullException(nameof(typeName)), isCollection, path, isUnknown: false, isSystemValue)
    {
    }

    private FhirPathType(IType? type, string? typeName, bool isCollection, string? path, bool isUnknown, bool isSystemValue)
    {
        Type = type;
        _typeName = typeName;
        IsCollection = isCollection || (type?.IsCollection ?? false);
        Path = path ?? type?.Info.Name ?? typeName!;
        IsUnknown = isUnknown;
        IsSystemValue = isSystemValue;
    }

    /// <summary>
    /// The underlying FHIR type definition (may be null for primitives).
    /// </summary>
    public IType? Type { get; }

    /// <summary>
    /// The type name (derived from Type.Info.Name or provided directly).
    /// </summary>
    public string TypeName => Type?.Info.Name ?? _typeName ?? "unknown";
    private readonly string? _typeName = null;

    /// <summary>
    /// Whether this represents a collection (max cardinality > 1).
    /// </summary>
    public bool IsCollection { get; }

    /// <summary>
    /// Gets whether static analysis cannot determine the runtime type.
    /// </summary>
    public bool IsUnknown { get; }

    /// <summary>
    /// Gets whether the value this type describes is a System-namespace value rather than a FHIR element.
    /// </summary>
    /// <remarks>
    /// This is the static-analysis counterpart of <see cref="ISystemValueElement"/>: a value the expression
    /// constructs - a literal, an operator result, or the return of a function declared to produce a
    /// primitive - rather than one navigated out of a resource. The distinction is not cosmetic, because a
    /// System value carries FHIR's lower camel case spelling in its instance type, so <c>System.Integer</c>
    /// has to reach an <c>integer</c> instance type on every FHIR version. Without this flag the analyzer
    /// cannot tell <c>Patient.active.ofType(Boolean)</c>, which is a FHIR element and empties from R5
    /// onwards, from <c>Patient.name.exists().ofType(Boolean)</c>, which is a System value and never does.
    /// </remarks>
    public bool IsSystemValue { get; }

    /// <summary>
    /// Simple definitional path to the property (e.g., "Patient.name").
    /// Used for error reporting and path tracking.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Gets whether this type is a FHIR resource type.
    /// </summary>
    public bool IsResource => Type?.Info.IsResource ?? false;

    /// <summary>
    /// Gets whether this type is a primitive type.
    /// </summary>
    public bool IsPrimitive => Type?.Info.IsPrimitive ?? IsPrimitiveTypeName(TypeName);

    /// <summary>
    /// Returns a new FhirPathType marked as a collection.
    /// </summary>
    public FhirPathType AsCollection() => With(isCollection: true, Path);

    /// <summary>
    /// Returns a new FhirPathType marked as a single value (not collection).
    /// </summary>
    public FhirPathType AsSingle() => With(isCollection: false, Path);

    /// <summary>
    /// Returns a new FhirPathType with the updated path.
    /// </summary>
    public FhirPathType WithPath(string newPath) => With(IsCollection, newPath);

    private FhirPathType With(bool isCollection, string path) =>
        new(Type, _typeName, isCollection, path, IsUnknown, IsSystemValue);

    public bool Equals(FhirPathType other) =>
        TypeName == other.TypeName &&
        IsCollection == other.IsCollection &&
        IsUnknown == other.IsUnknown &&
        IsSystemValue == other.IsSystemValue;

    public override bool Equals(object? obj) =>
        obj is FhirPathType other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(TypeName, IsCollection, IsUnknown, IsSystemValue);

    public override string ToString() =>
        IsCollection ? $"{TypeName}[]" : TypeName;

    public static bool operator ==(FhirPathType left, FhirPathType right) => left.Equals(right);
    public static bool operator !=(FhirPathType left, FhirPathType right) => !left.Equals(right);

    /// <summary>
    /// Creates a type whose runtime shape cannot be determined statically.
    /// </summary>
    public static FhirPathType Unknown(bool isCollection = false, string? path = null) =>
        new(null, IndeterminateTypeName, isCollection, path, isUnknown: true, isSystemValue: false);

    public static bool IsPrimitiveTypeName(string typeName)
    {
        // FhirPath type names are lowercase by spec, ToLowerInvariant is intentional
#pragma warning disable CA1308 // Normalize strings to uppercase
        return typeName.ToLowerInvariant() switch
#pragma warning restore CA1308 // Normalize strings to uppercase
        {
            "boolean" or "integer" or "string" or "decimal" or "uri" or "url" or
            "canonical" or "base64binary" or "instant" or "date" or "datetime" or
            "time" or "code" or "oid" or "id" or "markdown" or "unsignedint" or
            "positiveint" or "uuid" or "xhtml" => true,
            _ => false
        };
    }
}
