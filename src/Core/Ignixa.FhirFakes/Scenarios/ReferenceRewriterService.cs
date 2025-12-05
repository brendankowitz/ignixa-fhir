// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.FhirFakes.Scenarios;

/// <summary>
/// Rewrites references in generated resources based on configured format.
/// Uses IReferenceMetadataProvider to discover reference fields.
/// </summary>
internal sealed class ReferenceRewriterService
{
    private readonly IReferenceMetadataProvider _metadataProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReferenceRewriterService"/> class.
    /// </summary>
    /// <param name="metadataProvider">Provider for reference field metadata.</param>
    public ReferenceRewriterService(IReferenceMetadataProvider metadataProvider)
    {
        ArgumentNullException.ThrowIfNull(metadataProvider);
        _metadataProvider = metadataProvider;
    }

    /// <summary>
    /// Rewrites all references in a collection of resources.
    /// </summary>
    /// <param name="resources">Resources to rewrite.</param>
    /// <param name="identities">Map of resource IDs to ResourceIdentity.</param>
    /// <param name="targetFormat">Target reference format.</param>
    public void RewriteReferences(
        IEnumerable<ResourceJsonNode> resources,
        IReadOnlyDictionary<string, ResourceIdentity> identities,
        ReferenceFormat targetFormat)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(identities);

        foreach (var resource in resources)
        {
            RewriteResourceReferences(resource, identities, targetFormat);
        }
    }

    private void RewriteResourceReferences(
        ResourceJsonNode resource,
        IReadOnlyDictionary<string, ResourceIdentity> identities,
        ReferenceFormat targetFormat)
    {
        var metadata = _metadataProvider.GetMetadata(resource.ResourceType);
        if (metadata.Count == 0)
        {
            return; // No references in this resource type
        }

        var node = resource.MutableNode;

        foreach (var field in metadata)
        {
            RewriteReferenceField(node, field, identities, targetFormat);
        }
    }

    private void RewriteReferenceField(
        JsonNode node,
        ReferenceFieldMetadata field,
        IReadOnlyDictionary<string, ResourceIdentity> identities,
        ReferenceFormat targetFormat)
    {
        // Navigate to the field using ElementPath
        var fieldNode = NavigateToField(node, field.ElementPath);
        if (fieldNode is null)
        {
            return; // Field doesn't exist, skip
        }

        // Handle array vs single reference
        if (field.IsCollection)
        {
            // Array of references (e.g., reasonReference[])
            if (fieldNode is JsonArray array)
            {
                for (int i = 0; i < array.Count; i++)
                {
                    if (array[i] is JsonObject refObj)
                    {
                        RewriteReferenceObject(refObj, identities, targetFormat);
                    }
                }
            }
        }
        else
        {
            // Single reference
            if (fieldNode is JsonObject refObj)
            {
                RewriteReferenceObject(refObj, identities, targetFormat);
            }
        }
    }

    private static void RewriteReferenceObject(
        JsonObject referenceObject,
        IReadOnlyDictionary<string, ResourceIdentity> identities,
        ReferenceFormat targetFormat)
    {
        // Get the reference value
        if (referenceObject["reference"] is not JsonValue refValue)
        {
            return; // No reference field
        }

        var referenceString = refValue.GetValue<string>();
        if (string.IsNullOrEmpty(referenceString))
        {
            return;
        }

        // Skip fragment references (e.g., "#contained-resource")
        if (referenceString.StartsWith('#'))
        {
            return;
        }

        // Extract resource ID from the reference
        var resourceId = ExtractResourceId(referenceString);
        if (string.IsNullOrEmpty(resourceId))
        {
            return; // Can't extract ID
        }

        // Look up in registry
        if (!identities.TryGetValue(resourceId, out var identity))
        {
            return; // Not in registry, assume external reference
        }

        // Rewrite to target format
        referenceObject["reference"] = identity.GetReference(targetFormat);
    }

    private static string? ExtractResourceId(string reference)
    {
        // Handle urn:uuid:xxx format
        if (reference.StartsWith("urn:uuid:", StringComparison.OrdinalIgnoreCase))
        {
            return reference.Substring("urn:uuid:".Length);
        }

        // Handle ResourceType/id format
        var slashIndex = reference.IndexOf('/', StringComparison.Ordinal);
        if (slashIndex > 0 && slashIndex < reference.Length - 1)
        {
            var idPart = reference.Substring(slashIndex + 1);

            // Handle versioned references (Patient/123/_history/2)
            var historyIndex = idPart.IndexOf("/_history/", StringComparison.OrdinalIgnoreCase);
            if (historyIndex > 0)
            {
                return idPart.Substring(0, historyIndex);
            }

            return idPart;
        }

        // Handle absolute URLs (http://example.com/fhir/Patient/123)
        if (reference.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            reference.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var lastSlash = reference.LastIndexOf('/');
            if (lastSlash > 0 && lastSlash < reference.Length - 1)
            {
                return reference.Substring(lastSlash + 1);
            }
        }

        return null;
    }

    private static JsonNode? NavigateToField(JsonNode node, string path)
    {
        // Simple navigation for single-level paths (e.g., "subject", "encounter")
        // ElementPath from metadata is typically a simple field name
        if (node is JsonObject obj)
        {
            return obj.TryGetPropertyValue(path, out var fieldNode) ? fieldNode : null;
        }

        return null;
    }
}
