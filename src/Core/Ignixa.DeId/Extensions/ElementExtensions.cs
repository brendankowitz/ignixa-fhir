// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Text.RegularExpressions;
using Ignixa.Abstractions;

namespace Ignixa.DeId.Extensions;

/// <summary>
/// Extension methods for inspecting FHIR element types, navigating node hierarchies, and checking primitives.
/// </summary>
internal static class ElementExtensions
{
    private static readonly string LocationToFhirPathRegex = @"\[.*?\]";

    public static bool IsDateNode(this IElement node)
    {
        return node is not null && string.Equals(node.InstanceType, Constants.DateTypeName, StringComparison.InvariantCultureIgnoreCase);
    }

    public static bool IsDateTimeNode(this IElement node)
    {
        return node is not null && string.Equals(node.InstanceType, Constants.DateTimeTypeName, StringComparison.InvariantCultureIgnoreCase);
    }

    public static bool IsInstantNode(this IElement node)
    {
        return node is not null && string.Equals(node.InstanceType, Constants.InstantTypeName, StringComparison.InvariantCultureIgnoreCase);
    }

    public static bool IsAgeNode(this IElement node)
    {
        return node is not null && string.Equals(node.InstanceType, Constants.AgeTypeName, StringComparison.InvariantCultureIgnoreCase);
    }

    public static bool IsBundleNode(this IElement node)
    {
        return node is not null && string.Equals(node.InstanceType, Constants.BundleTypeName, StringComparison.InvariantCultureIgnoreCase);
    }

    public static bool IsReferenceNode(this IElement node)
    {
        return node is not null && string.Equals(node.InstanceType, Constants.ReferenceTypeName, StringComparison.InvariantCultureIgnoreCase);
    }

    public static bool IsPostalCodeNode(this IElement node)
    {
        return node is not null && string.Equals(node.Name, Constants.PostalCodeNodeName, StringComparison.InvariantCultureIgnoreCase);
    }

    public static bool IsEntryNode(this IElement node)
    {
        return node is not null && string.Equals(node.Name, Constants.EntryNodeName, StringComparison.InvariantCultureIgnoreCase);
    }

    public static bool IsContainedNode(this IElement node)
    {
        return node is not null && string.Equals(node.Name, Constants.ContainedNodeName, StringComparison.InvariantCultureIgnoreCase);
    }

    public static bool HasContainedNode(this IElement node)
    {
        return node is not null && node.Children(Constants.ContainedNodeName).Count > 0;
    }

    public static bool IsFhirResource(this IElement node)
    {
        return node?.Type?.Info.IsResource == true;
    }

    public static string GetFhirPath(this IElement node)
    {
        return node is null ? string.Empty : Regex.Replace(node.Location, LocationToFhirPathRegex, string.Empty);
    }

    public static string GetNodeId(this IElement node)
    {
        var id = node.Children("id").FirstOrDefault();
        return id?.Value?.ToString() ?? string.Empty;
    }

    public static IElement? GetMeta(this IElement node)
    {
        return node?.Children("meta").FirstOrDefault();
    }

    /// <summary>
    /// Checks if this element is an Age.value decimal node.
    /// Since IElement has no Parent, we infer from the parent parameter or from Location.
    /// </summary>
    public static bool IsAgeDecimalNode(this IElement node, IElement? parent)
    {
        if (node is null)
        {
            return false;
        }

        // If parent is provided, use it directly
        if (parent is not null)
        {
            return parent.IsAgeNode() &&
                   string.Equals(node.InstanceType, Constants.DecimalTypeName, StringComparison.InvariantCultureIgnoreCase);
        }

        // Fall back to checking Location if no parent provided
        if (node.Location is not null && node.Location.Contains("Age", StringComparison.Ordinal))
        {
            return string.Equals(node.InstanceType, Constants.DecimalTypeName, StringComparison.InvariantCultureIgnoreCase);
        }

        return false;
    }

    /// <summary>
    /// Checks if this element is a Reference.reference string node.
    /// </summary>
    public static bool IsReferenceStringNode(this IElement node, IElement? parent)
    {
        if (node is null)
        {
            return false;
        }

        // If parent is provided, use it directly
        if (parent is not null)
        {
            return parent.IsReferenceNode() &&
                   string.Equals(node.Name, Constants.ReferenceStringNodeName, StringComparison.InvariantCultureIgnoreCase);
        }

        // Fall back to checking Location if no parent provided
        if (node.Location is not null && node.Location.Contains("Reference", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(node.Name, Constants.ReferenceStringNodeName, StringComparison.InvariantCultureIgnoreCase);
        }

        return false;
    }

    /// <summary>
    /// Overload for IsReferenceStringNode that infers parent from Location.
    /// </summary>
    public static bool IsReferenceStringNode(this IElement node)
    {
        return node.IsReferenceStringNode(parent: null);
    }

    /// <summary>
    /// Enumerates all descendants of a node.
    /// </summary>
    public static IEnumerable<IElement> Descendants(this IElement node)
    {
        foreach (var child in node.Children())
        {
            yield return child;
            foreach (var descendant in child.Descendants())
            {
                yield return descendant;
            }
        }
    }

    /// <summary>
    /// Checks whether an element's type is a FHIR primitive type.
    /// Uses the Ignixa IType.Info.IsPrimitive property.
    /// </summary>
    public static bool IsPrimitiveType(this IElement node)
    {
        return node?.Type?.Info.IsPrimitive == true;
    }

    /// <summary>
    /// Checks whether an element is a FHIR primitive type.
    /// Falls back to checking InstanceType against known primitive type names when Type metadata is unavailable
    /// (e.g., for choice-type elements like value[x]).
    /// </summary>
    public static bool IsPrimitiveElement(this IElement node)
    {
        if (node?.Type?.Info.IsPrimitive == true)
            return true;

        // Fallback for choice types where Type metadata may not be available
        return node?.InstanceType is not null && KnownPrimitiveTypes.Contains(node.InstanceType);
    }

    private static readonly HashSet<string> KnownPrimitiveTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "boolean", "integer", "string", "decimal", "uri", "base64Binary",
        "instant", "date", "dateTime", "time", "code", "oid", "id",
        "markdown", "unsignedInt", "positiveInt", "url", "canonical", "uuid", "integer64"
    };
}
