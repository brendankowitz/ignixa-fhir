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
        ["url"] = "string",
        ["canonical"] = "string",
        ["uuid"] = "string",
        ["oid"] = "string",
        
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
        var parts = new List<string>();
        Expression? current = propExpr;
        
        while (current is PropertyAccessExpression prop)
        {
            parts.Insert(0, prop.PropertyName);
            current = prop.Focus;
        }

        if (current is IdentifierExpression id)
        {
            parts.Insert(0, id.Name);
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
        if (typeName.Contains('.', StringComparison.Ordinal))
        {
            var parts = typeName.Split('.');
            if (parts.Length == 2 && 
                (parts[0].Equals("FHIR", StringComparison.OrdinalIgnoreCase) || 
                 parts[0].Equals("System", StringComparison.OrdinalIgnoreCase)))
            {
                return parts[1];
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
    public static bool MatchesTypeWithInheritance(IElement element, string typeName)
    {
        var elementType = element.InstanceType;
        if (string.IsNullOrEmpty(elementType))
            return false;

        // Direct match
        if (elementType.Equals(typeName, StringComparison.OrdinalIgnoreCase))
            return true;

        // Check inheritance: is the element's type a subtype of the target?
        if (TypeInheritance.TryGetValue(elementType, out var baseType))
        {
            return baseType.Equals(typeName, StringComparison.OrdinalIgnoreCase);
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
