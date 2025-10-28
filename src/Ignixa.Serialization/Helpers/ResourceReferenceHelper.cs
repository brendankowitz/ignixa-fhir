// <copyright file="ResourceReferenceHelper.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

#nullable enable

using System.Text.Json.Nodes;
using Ignixa.Serialization.Abstractions;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Serialization.Helpers;

/// <summary>
/// Provides efficient methods to find and update ResourceReference values in ResourceJsonNode objects.
/// Uses metadata from IReferenceMetadataProvider for optimized lookup.
/// </summary>
public static class ResourceReferenceHelper
{
    /// <summary>
    /// Gets all ResourceReference values from a ResourceJsonNode using metadata for efficient lookup.
    /// </summary>
    /// <param name="resource">The resource to search for references.</param>
    /// <param name="resourceType">The FHIR resource type (e.g., "Patient", "Observation").</param>
    /// <param name="metadataProvider">The metadata provider for reference field information.</param>
    /// <returns>A list of all references found in the resource.</returns>
    public static IReadOnlyList<ResourceReference> GetReferences(ResourceJsonNode resource, string resourceType, IReferenceMetadataProvider metadataProvider)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(resourceType);
        ArgumentNullException.ThrowIfNull(metadataProvider);

        // Get metadata for this resource type
        if (!metadataProvider.HasReferences(resourceType))
        {
            return Array.Empty<ResourceReference>();
        }

        var metadata = metadataProvider.GetMetadata(resourceType);
        var references = new List<ResourceReference>();

        // Get the internal JsonObject
        var jsonObject = resource.MutableNode;

        // Iterate through all reference fields defined in metadata
        foreach (var fieldMetadata in metadata)
        {
            // Check if this field exists in the resource's JsonObject
            if (jsonObject.TryGetPropertyValue(fieldMetadata.ElementPath, out var node) && node != null)
            {
                // Handle both single references and arrays of references
                if (fieldMetadata.IsCollection)
                {
                    ExtractReferencesFromJsonArray(node, fieldMetadata, references);
                }
                else
                {
                    ExtractReferenceFromJsonNode(node, fieldMetadata, references);
                }
            }
        }

        return references;
    }

    /// <summary>
    /// Updates a reference value in a ResourceJsonNode at the specified path.
    /// </summary>
    /// <param name="resource">The resource to update.</param>
    /// <param name="elementPath">The element path (e.g., "subject", "generalPractitioner").</param>
    /// <param name="newReferenceValue">The new reference value (e.g., "Patient/456").</param>
    /// <param name="arrayIndex">Optional array index if updating a reference within a collection (0-based). Null for single references.</param>
    /// <returns>True if the reference was updated; false if the path was not found.</returns>
    public static bool UpdateReference(ResourceJsonNode resource, string elementPath, string newReferenceValue, int? arrayIndex = null)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(elementPath);
        ArgumentNullException.ThrowIfNull(newReferenceValue);

        var jsonObject = resource.MutableNode;

        // Check if this field exists in the resource's JsonObject
        if (!jsonObject.TryGetPropertyValue(elementPath, out var node) || node == null)
        {
            return false;
        }

        // Handle array references
        if (arrayIndex.HasValue)
        {
            if (node is not JsonArray array)
            {
                return false;
            }

            if (arrayIndex.Value < 0 || arrayIndex.Value >= array.Count)
            {
                return false;
            }

            // Update the specific array element
            array[arrayIndex.Value] = CreateReferenceJsonObject(newReferenceValue);
            return true;
        }

        // Handle single reference
        if (node is JsonObject)
        {
            jsonObject[elementPath] = CreateReferenceJsonObject(newReferenceValue);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Updates all references in a ResourceJsonNode that match a specific value.
    /// </summary>
    /// <param name="resource">The resource to update.</param>
    /// <param name="resourceType">The FHIR resource type.</param>
    /// <param name="oldReferenceValue">The reference value to find and replace.</param>
    /// <param name="newReferenceValue">The new reference value.</param>
    /// <param name="metadataProvider">The metadata provider for reference field information.</param>
    /// <returns>The number of references that were updated.</returns>
    public static int UpdateAllReferences(ResourceJsonNode resource, string resourceType, string oldReferenceValue, string newReferenceValue, IReferenceMetadataProvider metadataProvider)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(resourceType);
        ArgumentNullException.ThrowIfNull(oldReferenceValue);
        ArgumentNullException.ThrowIfNull(newReferenceValue);
        ArgumentNullException.ThrowIfNull(metadataProvider);

        var currentReferences = GetReferences(resource, resourceType, metadataProvider);
        int updateCount = 0;
        var jsonObject = resource.MutableNode;

        foreach (var reference in currentReferences)
        {
            if (reference.Value.Equals(oldReferenceValue, StringComparison.Ordinal))
            {
                // Determine if this is in an array
                if (reference.IsCollection)
                {
                    // Find the index in the array
                    if (jsonObject.TryGetPropertyValue(reference.ElementPath, out var node) && node is JsonArray array)
                    {
                        for (int i = 0; i < array.Count; i++)
                        {
                            if (TryExtractReferenceValue(array[i], out var refValue) &&
                                refValue.Equals(oldReferenceValue, StringComparison.Ordinal))
                            {
                                if (UpdateReference(resource, reference.ElementPath, newReferenceValue, i))
                                {
                                    updateCount++;
                                }
                            }
                        }
                    }
                }
                else
                {
                    if (UpdateReference(resource, reference.ElementPath, newReferenceValue))
                    {
                        updateCount++;
                    }
                }
            }
        }

        return updateCount;
    }

    private static void ExtractReferencesFromJsonArray(JsonNode node, ReferenceFieldMetadata fieldMetadata, List<ResourceReference> references)
    {
        if (node is not JsonArray array)
        {
            return;
        }

        foreach (var item in array)
        {
            if (item != null && TryExtractReferenceValue(item, out var referenceValue))
            {
                references.Add(CreateResourceReference(referenceValue, fieldMetadata));
            }
        }
    }

    private static void ExtractReferenceFromJsonNode(JsonNode node, ReferenceFieldMetadata fieldMetadata, List<ResourceReference> references)
    {
        if (TryExtractReferenceValue(node, out var referenceValue))
        {
            references.Add(CreateResourceReference(referenceValue, fieldMetadata));
        }
    }

    private static bool TryExtractReferenceValue(JsonNode node, out string referenceValue)
    {
        referenceValue = string.Empty;

        // Reference objects should have a "reference" property
        if (node is JsonObject obj && obj.TryGetPropertyValue("reference", out var refNode) && refNode != null)
        {
            referenceValue = refNode.GetValue<string>();
            return !string.IsNullOrEmpty(referenceValue);
        }

        return false;
    }

    private static ResourceReference CreateResourceReference(string referenceValue, ReferenceFieldMetadata fieldMetadata)
    {
        // Parse the reference value to determine type and extract resource type/id
        var (refType, resourceType, resourceId) = ParseReferenceValue(referenceValue);

        return new ResourceReference
        {
            ElementPath = fieldMetadata.ElementPath,
            Value = referenceValue,
            TargetResourceTypes = fieldMetadata.TargetResourceTypes,
            IsCollection = fieldMetadata.IsCollection,
            Type = refType,
            ResourceType = resourceType,
            ResourceId = resourceId,
        };
    }

    private static (ReferenceType Type, string? ResourceType, string? ResourceId) ParseReferenceValue(string referenceValue)
    {
        // Logical identifier (urn:uuid:...)
        if (referenceValue.StartsWith("urn:", StringComparison.OrdinalIgnoreCase))
        {
            return (ReferenceType.Logical, null, null);
        }

        // Absolute URL
        if (referenceValue.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            referenceValue.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            // Try to extract resource type and ID from URL (e.g., ".../Patient/123")
            var lastSlashIndex = referenceValue.LastIndexOf('/');
            if (lastSlashIndex > 0 && lastSlashIndex < referenceValue.Length - 1)
            {
                var resourceId = referenceValue.Substring(lastSlashIndex + 1);
                var secondLastSlashIndex = referenceValue.LastIndexOf('/', lastSlashIndex - 1);
                if (secondLastSlashIndex > 0)
                {
                    var resourceType = referenceValue.Substring(secondLastSlashIndex + 1, lastSlashIndex - secondLastSlashIndex - 1);
                    return (ReferenceType.Absolute, resourceType, resourceId);
                }
            }

            return (ReferenceType.Absolute, null, null);
        }

        // Relative reference (ResourceType/id)
        var slashIndex = referenceValue.IndexOf('/', StringComparison.Ordinal);
        if (slashIndex > 0 && slashIndex < referenceValue.Length - 1)
        {
            var resourceType = referenceValue.Substring(0, slashIndex);
            var resourceId = referenceValue.Substring(slashIndex + 1);
            return (ReferenceType.Relative, resourceType, resourceId);
        }

        // Unknown format
        return (ReferenceType.Relative, null, null);
    }

    private static JsonObject CreateReferenceJsonObject(string referenceValue)
    {
        // Create a JSON object: { "reference": "Patient/123" }
        return new JsonObject
        {
            ["reference"] = referenceValue,
        };
    }
}
