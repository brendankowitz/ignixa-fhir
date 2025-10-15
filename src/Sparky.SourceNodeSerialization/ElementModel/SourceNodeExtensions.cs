// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Sparky.SourceNodeSerialization.ElementModel;

/// <summary>
/// Extension methods for ISourceNode.
/// </summary>
public static class SourceNodeExtensions
{
    /// <summary>
    /// Gets the resource type indicator for a source node.
    /// For FHIR resources, this returns the value of the "resourceType" element.
    /// </summary>
    /// <param name="node">The source node to check.</param>
    /// <returns>The resource type if the node is a resource, otherwise null.</returns>
    public static string? GetResourceTypeIndicator(this ISourceNode node)
    {
        if (node == null) return null;

        // For FHIR resources, the resourceType is a child element named "resourceType"
        var resourceTypeNode = node.Children("resourceType").FirstOrDefault();
        return resourceTypeNode?.Text;
    }
}
