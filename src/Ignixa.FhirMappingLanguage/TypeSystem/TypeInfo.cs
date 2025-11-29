/* Copyright (c) 2025, Ignixa Contributors */

namespace Ignixa.FhirMappingLanguage.TypeSystem;

/// <summary>
/// Represents resolved type information.
/// </summary>
public class TypeInfo
{
    public TypeInfo(string name, TypeCategory category, string? baseType = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
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
