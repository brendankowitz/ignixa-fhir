// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.ObjectModel;
using Ignixa.Abstractions;

namespace Ignixa.FhirPath.Visitors;

/// <summary>
/// Represents the type context during FhirPath expression validation.
/// Holds a collection of possible types that can result from an expression.
/// </summary>
/// <remarks>
/// This class is used by the expression visitor to track:
/// - All possible types at each point in the expression
/// - Whether the expression can return a collection
/// - Whether this is the root context
///
/// Multiple types are tracked because:
/// - Choice elements (value[x]) can have multiple possible types
/// - Union expressions (|) combine types from both sides
/// - Polymorphic navigation through different type paths
/// </remarks>
public sealed class FhirPathTypeSet
{
    /// <summary>
    /// Gets or sets whether this is the root context (entry point of expression).
    /// </summary>
    public bool IsRoot { get; set; }

    /// <summary>
    /// The collection of possible types at this point in the expression.
    /// </summary>
    public Collection<FhirPathType> Types { get; } = new();

    /// <summary>
    /// Creates an empty FhirPathTypeSet.
    /// </summary>
    public FhirPathTypeSet()
    {
    }

    /// <summary>
    /// Creates FhirPathTypeSet with an initial type.
    /// </summary>
    public FhirPathTypeSet(IType type, string? path = null)
    {
        Types.Add(new FhirPathType(type, path: path));
    }

    /// <summary>
    /// Creates FhirPathTypeSet with an initial type from schema.
    /// </summary>
    public FhirPathTypeSet(IFhirSchemaProvider schema, string typeName)
    {
        var type = schema.GetTypeDefinition(typeName);
        if (type != null)
        {
            Types.Add(new FhirPathType(type, path: typeName));
        }
    }

    /// <summary>
    /// Copies types from another FhirPathTypeSet instance.
    /// </summary>
    public void CopyFrom(FhirPathTypeSet other)
    {
        foreach (var t in other.Types)
        {
            Types.Add(t);
        }
    }

    /// <summary>
    /// Adds a type from the schema provider.
    /// </summary>
    public void AddType(IFhirSchemaProvider schema, string typeName, bool forceCollection = false, string? path = null)
    {
        var type = schema.GetTypeDefinition(typeName);
        if (type != null)
        {
            var np = new FhirPathType(type, forceCollection, path);
            Types.Add(np);
        }
        else
        {
            Types.Add(new FhirPathType(typeName, forceCollection, path));
        }
    }

    /// <summary>
    /// Adds a type directly.
    /// </summary>
    public void AddType(IType type, bool forceCollection = false, string? path = null)
    {
        Types.Add(new FhirPathType(type, forceCollection, path));
    }

    /// <summary>
    /// Adds a primitive type by name.
    /// </summary>
    public void AddPrimitiveType(string typeName, bool forceCollection = false)
    {
        Types.Add(new FhirPathType(typeName, forceCollection));
    }

    /// <summary>
    /// Gets comma-separated type names for display.
    /// </summary>
    public string TypeNames()
    {
        var result = ToString();
        return string.IsNullOrEmpty(result) ? "???" : result;
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return string.Join(", ", Types.Select(v => v.ToString()).Distinct());
    }

    /// <summary>
    /// Checks if the result can be of the specified type.
    /// </summary>
    /// <param name="typeName">The type name to check</param>
    /// <param name="singleOnly">If true, only match non-collection types</param>
    public bool CanBeOfType(string typeName, bool singleOnly = false)
    {
        if (Types.Any(v => v.TypeName.Equals(typeName, StringComparison.OrdinalIgnoreCase) &&
                          (!singleOnly || !v.IsCollection)))
        {
            return true;
        }

        if (Types.Any(v => NormalizeBaseType(v.TypeName).Equals(NormalizeBaseType(typeName), StringComparison.OrdinalIgnoreCase) &&
                          (!singleOnly || !v.IsCollection)))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Returns the type names that match the given type.
    /// </summary>
    public IEnumerable<string> CanBeOfTypes(string typeName)
    {
        return Types
            .Where(v => v.TypeName.Equals(typeName, StringComparison.OrdinalIgnoreCase) ||
                       NormalizeBaseType(v.TypeName).Equals(typeName, StringComparison.OrdinalIgnoreCase))
            .Select(v => v.TypeName);
    }

    /// <summary>
    /// Checks if any of the possible types is a collection.
    /// </summary>
    public bool IsCollection()
    {
        return Types.Any(v => v.IsCollection);
    }

    /// <summary>
    /// Returns a new FhirPathTypeSet with all types marked as single (not collection).
    /// </summary>
    public FhirPathTypeSet AsSingle()
    {
        var result = new FhirPathTypeSet { IsRoot = IsRoot };
        foreach (var t in Types)
        {
            result.Types.Add(t.AsSingle());
        }
        return result;
    }

    /// <summary>
    /// Returns a new FhirPathTypeSet with all types marked as collections.
    /// </summary>
    public FhirPathTypeSet AsCollection()
    {
        var result = new FhirPathTypeSet { IsRoot = IsRoot };
        foreach (var t in Types)
        {
            result.Types.Add(t.AsCollection());
        }
        return result;
    }

    /// <summary>
    /// Normalizes type names to their base FhirPath types.
    /// For example: code -> string, date -> dateTime, etc.
    /// </summary>
    private static string NormalizeBaseType(string typeName)
    {
        // FhirPath type names are lowercase by spec, ToLowerInvariant is intentional
#pragma warning disable CA1308 // Normalize strings to uppercase
        return typeName.ToLowerInvariant() switch
        {
            "code" or "markdown" or "id" or "uri" or "url" or "canonical" or
            "uuid" or "oid" or "base64binary" or "xhtml" => "string",
            "positiveint" or "unsignedint" => "integer",
            "date" or "instant" => "datetime",
            _ => typeName.ToLowerInvariant()
        };
#pragma warning restore CA1308 // Normalize strings to uppercase
    }
}
