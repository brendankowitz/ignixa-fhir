// <copyright file="ResourceReferenceHelper.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

#nullable enable

using System.Text.Json;
using Sparky.SourceNodeSerialization.Abstractions;
using Sparky.SourceNodeSerialization.SourceNodes.Models;

namespace Sparky.SourceNodeSerialization.Helpers;

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

        // Iterate through all reference fields defined in metadata
        foreach (var fieldMetadata in metadata)
        {
            // Check if this field exists in the resource's ExtensionData
            if (resource.ExtensionData.TryGetValue(fieldMetadata.ElementPath, out var element))
            {
                // Handle both single references and arrays of references
                if (fieldMetadata.IsCollection)
                {
                    ExtractReferencesFromArray(element, fieldMetadata, references);
                }
                else
                {
                    ExtractReferenceFromElement(element, fieldMetadata, references);
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

        // Check if this field exists in the resource's ExtensionData
        if (!resource.ExtensionData.TryGetValue(elementPath, out var element))
        {
            return false;
        }

        // Handle array references
        if (arrayIndex.HasValue)
        {
            if (element.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            // Rebuild the array with the updated reference
            var arrayItems = element.EnumerateArray().ToList();
            if (arrayIndex.Value < 0 || arrayIndex.Value >= arrayItems.Count)
            {
                return false;
            }

            // Update the specific array element
            arrayItems[arrayIndex.Value] = CreateReferenceElement(newReferenceValue);
            resource.ExtensionData[elementPath] = SerializeArrayToJsonElement(arrayItems);
            return true;
        }

        // Handle single reference
        if (element.ValueKind == JsonValueKind.Object)
        {
            resource.ExtensionData[elementPath] = CreateReferenceElement(newReferenceValue);
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

        foreach (var reference in currentReferences)
        {
            if (reference.Value.Equals(oldReferenceValue, StringComparison.Ordinal))
            {
                // Determine if this is in an array
                if (reference.IsCollection)
                {
                    // Find the index in the array
                    if (resource.ExtensionData.TryGetValue(reference.ElementPath, out var element))
                    {
                        var arrayItems = element.EnumerateArray().ToList();
                        for (int i = 0; i < arrayItems.Count; i++)
                        {
                            if (TryExtractReferenceValue(arrayItems[i], out var refValue) &&
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

    private static void ExtractReferencesFromArray(JsonElement element, ReferenceFieldMetadata fieldMetadata, List<ResourceReference> references)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in element.EnumerateArray())
        {
            if (TryExtractReferenceValue(item, out var referenceValue))
            {
                references.Add(CreateResourceReference(referenceValue, fieldMetadata));
            }
        }
    }

    private static void ExtractReferenceFromElement(JsonElement element, ReferenceFieldMetadata fieldMetadata, List<ResourceReference> references)
    {
        if (TryExtractReferenceValue(element, out var referenceValue))
        {
            references.Add(CreateResourceReference(referenceValue, fieldMetadata));
        }
    }

    private static bool TryExtractReferenceValue(JsonElement element, out string referenceValue)
    {
        referenceValue = string.Empty;

        // Reference objects should have a "reference" property
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("reference", out var refProperty))
        {
            if (refProperty.ValueKind == JsonValueKind.String)
            {
                referenceValue = refProperty.GetString() ?? string.Empty;
                return !string.IsNullOrEmpty(referenceValue);
            }
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

    private static JsonElement CreateReferenceElement(string referenceValue)
    {
        // Create a JSON object: { "reference": "Patient/123" }
        var json = $"{{\"reference\":\"{referenceValue}\"}}";
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static JsonElement SerializeArrayToJsonElement(List<JsonElement> items)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        writer.WriteStartArray();
        foreach (var item in items)
        {
            item.WriteTo(writer);
        }

        writer.WriteEndArray();
        writer.Flush();

        stream.Position = 0;
        using var doc = JsonDocument.Parse(stream);
        return doc.RootElement.Clone();
    }
}
