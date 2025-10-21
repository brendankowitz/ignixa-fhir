// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using EnsureThat;
using Ignixa.SourceNodeSerialization.Abstractions;
using Ignixa.SourceNodeSerialization.Models;

namespace Ignixa.SourceNodeSerialization.Extensions;

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

    /// <summary>
    /// Removes an extension from the meta.extension array that matches the given URL.
    /// </summary>
    /// <param name="node">The MetaJsonNode to remove the extension from.</param>
    /// <param name="url">The URL of the extension to remove.</param>
    /// <returns>True if an extension was removed, false if no matching extension was found.</returns>
    public static bool RemoveExtension(this MetaJsonNode node, string url)
    {
        EnsureArg.IsNotNull(node, nameof(node));
        EnsureArg.IsNotNullOrWhiteSpace(url, nameof(url));

        var metaNode = node.MutableNode;
        if (metaNode.TryGetPropertyValue("extension", out var extensionNode) && extensionNode is JsonArray extensionArray)
        {
            // Find the index of the extension with matching URL
            int indexToRemove = -1;
            for (int i = 0; i < extensionArray.Count; i++)
            {
                if (extensionArray[i] is JsonObject extObj &&
                    extObj.TryGetPropertyValue("url", out var urlNode) &&
                    urlNode?.GetValue<string>() == url)
                {
                    indexToRemove = i;
                    break;
                }
            }

            if (indexToRemove >= 0)
            {
                extensionArray.RemoveAt(indexToRemove);

                // If the array is now empty, remove the extension property entirely
                if (extensionArray.Count == 0)
                {
                    metaNode.Remove("extension");
                }

                return true;
            }
        }

        return false;
    }
}
