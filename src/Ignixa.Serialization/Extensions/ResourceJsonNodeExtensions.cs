// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Serialization.Extensions;

/// <summary>
/// Extension methods for ResourceJsonNode type conversions.
/// </summary>
public static class ResourceJsonNodeExtensions
{
    /// <summary>
    /// Converts a ResourceJsonNode to a strongly-typed derived resource node.
    /// Uses the underlying MutableNode (JsonObject) to construct the specific type.
    /// </summary>
    /// <typeparam name="T">The specific ResourceJsonNode type to convert to (e.g., ProvenanceJsonNode, BundleJsonNode).</typeparam>
    /// <param name="resource">The resource to convert.</param>
    /// <returns>A new instance of the specified type wrapping the same underlying JsonObject.</returns>
    /// <remarks>
    /// This method creates a new instance of the target type using the same underlying JsonObject,
    /// allowing you to access type-specific properties and methods.
    /// Example: var provenance = resourceNode.As&lt;ProvenanceJsonNode&gt;();
    /// </remarks>
    public static T As<T>(this ResourceJsonNode resource) where T : ResourceJsonNode
    {
        ArgumentNullException.ThrowIfNull(resource);

        // Use Activator to create an instance of T with the JsonObject constructor
        // This assumes all ResourceJsonNode subclasses have a constructor accepting JsonObject
        var instance = (T)Activator.CreateInstance(
            typeof(T),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
            null,
            new object[] { resource.MutableNode.AsObject() },
            null)!;

        return instance;
    }
}
