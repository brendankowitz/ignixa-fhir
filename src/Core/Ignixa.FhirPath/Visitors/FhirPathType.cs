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
    {
        Type = type ?? throw new ArgumentNullException(nameof(type));
        IsCollection = isCollection || type.IsCollection;
        Path = path ?? type.Info.Name;
    }

    /// <summary>
    /// Creates a new FhirPathType from a type name (for primitives or when IType is not available).
    /// </summary>
    /// <param name="typeName">The FHIR type name</param>
    /// <param name="isCollection">Whether this is a collection</param>
    /// <param name="path">Optional definitional path for error reporting</param>
    public FhirPathType(string typeName, bool isCollection = false, string? path = null)
        : this(typeName, isCollection, path, isUnknown: false)
    {
    }

    private FhirPathType(string typeName, bool isCollection, string? path, bool isUnknown)
    {
        Type = null;
        _typeName = typeName ?? throw new ArgumentNullException(nameof(typeName));
        IsCollection = isCollection;
        Path = path ?? typeName;
        IsUnknown = isUnknown;
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
    public FhirPathType AsCollection() =>
        IsUnknown
            ? Unknown(isCollection: true, path: Path)
            : Type != null
            ? new FhirPathType(Type, isCollection: true, path: Path)
            : new FhirPathType(TypeName, isCollection: true, path: Path);

    /// <summary>
    /// Returns a new FhirPathType marked as a single value (not collection).
    /// </summary>
    public FhirPathType AsSingle() =>
        IsUnknown
            ? Unknown(isCollection: false, path: Path)
            : Type != null
            ? new FhirPathType(Type, isCollection: false, path: Path)
            : new FhirPathType(TypeName, isCollection: false, path: Path);

    /// <summary>
    /// Returns a new FhirPathType with the updated path.
    /// </summary>
    public FhirPathType WithPath(string newPath) =>
        IsUnknown
            ? Unknown(IsCollection, newPath)
            : Type != null
            ? new FhirPathType(Type, IsCollection, newPath)
            : new FhirPathType(TypeName, IsCollection, newPath);

    public bool Equals(FhirPathType other) =>
        TypeName == other.TypeName &&
        IsCollection == other.IsCollection &&
        IsUnknown == other.IsUnknown;

    public override bool Equals(object? obj) =>
        obj is FhirPathType other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(TypeName, IsCollection, IsUnknown);

    public override string ToString() =>
        IsCollection ? $"{TypeName}[]" : TypeName;

    public static bool operator ==(FhirPathType left, FhirPathType right) => left.Equals(right);
    public static bool operator !=(FhirPathType left, FhirPathType right) => !left.Equals(right);

    /// <summary>
    /// Creates a type whose runtime shape cannot be determined statically.
    /// </summary>
    public static FhirPathType Unknown(bool isCollection = false, string? path = null) =>
        new(IndeterminateTypeName, isCollection, path, isUnknown: true);

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
