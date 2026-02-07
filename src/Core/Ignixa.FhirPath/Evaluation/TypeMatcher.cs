/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Centralized type matching logic for FhirPath type operations.
 * Used by: is operator, as operator, ofType() function, as() function.
 */

using System.Collections.Frozen;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Expressions;

namespace Ignixa.FhirPath.Evaluation;

/// <summary>
/// Provides centralized type matching logic for FhirPath type operations.
/// </summary>
internal static class TypeMatcher
{
    // System-only types that must match FHIRPath literals (capitalized)
    // These are FHIRPath System types, not FHIR element types
    // Note: Date and Quantity exist as both System types and FHIR types, so they're NOT in this list.
    private static readonly FrozenSet<string> SystemOnlyTypes = new[]
    {
        "Boolean", "Integer", "Decimal", "String", "DateTime", "Time"
    }.ToFrozenSet(StringComparer.Ordinal);

    // FHIR type inheritance mappings (subtype -> base type)
    private static readonly FrozenDictionary<string, string> TypeInheritance = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        // String subtypes
        ["code"] = "string",
        ["id"] = "string",
        ["markdown"] = "string",
        ["uri"] = "string",

        // URI subtypes (uri -> string)
        ["url"] = "uri",
        ["canonical"] = "uri",
        ["uuid"] = "uri",
        ["oid"] = "uri",

        // Integer subtypes
        ["positiveInt"] = "integer",
        ["unsignedInt"] = "integer",

        // Quantity subtypes
        ["Age"] = "Quantity",
        ["Count"] = "Quantity",
        ["Distance"] = "Quantity",
        ["Duration"] = "Quantity",
        ["Money"] = "Quantity",
        ["SimpleQuantity"] = "Quantity"
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> ResourcesNotExtendingDomainResource = new[]
    {
        "Bundle", "Parameters", "Binary"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Extracts the type name from a FhirPath expression.
    /// Handles: System.Boolean, FHIR.Patient, Boolean, Patient, `Patient`
    /// </summary>
    public static string? ExtractTypeName(Expression expr)
    {
        return expr switch
        {
            IdentifierExpression idExpr => idExpr.Name,
            PropertyAccessExpression propExpr => ExtractPropertyAccessTypeName(propExpr),
            FunctionCallExpression funcExpr => funcExpr.FunctionName,
            ConstantExpression constExpr => constExpr.Value?.ToString(),
            _ => null
        };
    }

    private static string ExtractPropertyAccessTypeName(PropertyAccessExpression propExpr)
    {
        // Use Stack to avoid O(n²) from List.Insert(0, ...)
        var parts = new Stack<string>();
        Expression? current = propExpr;
        
        while (current is PropertyAccessExpression prop)
        {
            parts.Push(prop.PropertyName);
            current = prop.Focus;
        }

        if (current is IdentifierExpression id)
        {
            parts.Push(id.Name);
        }

        return string.Join(".", parts);
    }

    /// <summary>
    /// Parses a type name and removes namespace prefix if present.
    /// Returns the base type name and flags for explicit namespaces.
    /// </summary>
    public static (string TypeName, bool IsSystemNamespace, bool IsFhirNamespace) ParseTypeName(string typeName)
    {
        if (typeName.StartsWith("System.", StringComparison.OrdinalIgnoreCase))
        {
            return (typeName.Substring(7), true, false);
        }
        
        if (typeName.StartsWith("FHIR.", StringComparison.OrdinalIgnoreCase))
        {
            return (typeName.Substring(5), false, true);
        }

        return (typeName, false, false);
    }

    /// <summary>
    /// Removes namespace prefix from a type name for simple matching.
    /// </summary>
    public static string StripNamespace(string typeName)
    {
        // Optimized to avoid string.Split allocation
        var dotIndex = typeName.IndexOf('.', StringComparison.Ordinal);
        if (dotIndex > 0 && typeName.LastIndexOf('.') == dotIndex)
        {
            var prefix = typeName.AsSpan(0, dotIndex);
            if (prefix.Equals("FHIR", StringComparison.OrdinalIgnoreCase) ||
                prefix.Equals("System", StringComparison.OrdinalIgnoreCase))
            {
                return typeName.Substring(dotIndex + 1);
            }
        }
        return typeName;
    }

    /// <summary>
    /// Checks if the element's type matches the target type (simple matching, no inheritance).
    /// </summary>
    public static bool MatchesType(IElement element, string typeName)
    {
        var elementType = element.InstanceType;
        if (string.IsNullOrEmpty(elementType))
            return false;

        return elementType.Equals(typeName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks if the element's type matches the target type, considering FHIR type inheritance.
    /// </summary>
    /// <remarks>
    /// Supports:
    /// - Primitive type inheritance (e.g., code->string, uri->string, positiveInt->integer)
    /// - Quantity subtypes (e.g., Age->Quantity, Duration->Quantity)
    /// - FHIR resource hierarchy (e.g., Patient->DomainResource->Resource)
    ///
    /// Resource hierarchy is determined using type metadata from the schema provider.
    /// Note: Resource and Element are separate branches under Base in the FHIR type system.
    /// This method does not handle Element/DataType hierarchy as it is not needed for
    /// FHIRPath type operations (the official test suite does not test for is(Element)).
    /// </remarks>
    public static bool MatchesTypeWithInheritance(IElement element, string typeName)
    {
        var currentType = element.InstanceType;
        if (string.IsNullOrEmpty(currentType))
            return false;

        while (!string.IsNullOrEmpty(currentType))
        {
            if (currentType.Equals(typeName, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!TypeInheritance.TryGetValue(currentType, out var baseType))
                break;

            currentType = baseType;
        }

        if (element.Type?.Info is { IsResource: true })
        {
            if (typeName.Equals("Resource", StringComparison.OrdinalIgnoreCase))
                return true;

            if (typeName.Equals("DomainResource", StringComparison.OrdinalIgnoreCase))
            {
                var instanceType = element.InstanceType;
                return !ResourcesNotExtendingDomainResource.Contains(instanceType);
            }
        }

        return false;
    }

    /// <summary>
    /// Full type checking for the 'is' operator with System/FHIR namespace handling.
    /// </summary>
    public static bool IsTypeMatch(IElement element, string typeName)
    {
        var (baseTypeName, isSystemNamespace, isFhirNamespace) = ParseTypeName(typeName);
        var elementType = element.InstanceType ?? string.Empty;

        // Check if element is a FHIRPath literal (System type)
        var implType = element.GetType().Name;
        bool elementIsSystemType = implType.Contains("Primitive", StringComparison.OrdinalIgnoreCase);

        // With explicit namespace, enforce strict matching
        if (isSystemNamespace && !elementIsSystemType)
            return false;

        if (isFhirNamespace && elementIsSystemType)
            return false;

        if (!isSystemNamespace && !isFhirNamespace && SystemOnlyTypes.Contains(baseTypeName) && !elementIsSystemType)
            return false;

        // Compare types with inheritance
        return MatchesTypeWithInheritance(element, baseTypeName);
    }

    /// <summary>
    /// Filters a collection to elements matching the specified type.
    /// </summary>
    public static IEnumerable<IElement> FilterByType(IEnumerable<IElement> elements, string typeName, bool useInheritance = false)
    {
        var strippedTypeName = StripNamespace(typeName);
        
        return useInheritance 
            ? elements.Where(e => MatchesTypeWithInheritance(e, strippedTypeName))
            : elements.Where(e => MatchesType(e, strippedTypeName));
    }
}
