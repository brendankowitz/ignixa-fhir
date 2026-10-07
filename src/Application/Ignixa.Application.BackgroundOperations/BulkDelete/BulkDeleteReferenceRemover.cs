// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;

namespace Ignixa.Application.BackgroundOperations.BulkDelete;

/// <summary>
/// Removes references to a deleted resource, matching microsoft/fhir-server's ReferenceRemover semantics.
/// For any JSON object whose "reference" string property equals the target (OrdinalIgnoreCase),
/// removes the "reference" property and sets the "display" property to "Referenced resource deleted".
/// Recurses through all objects and arrays; other siblings like "identifier" and "type" are preserved.
/// </summary>
public static class BulkDeleteReferenceRemover
{
    public const string RemovedReferenceDisplay = "Referenced resource deleted";

    /// <summary>
    /// Removes all references to the target resource from a JSON resource tree.
    /// </summary>
    /// <param name="resource">The JSON resource node to process. Must not be null.</param>
    /// <param name="target">The reference value to match and remove. Must not be null or whitespace.</param>
    /// <returns>true if at least one reference was removed; false if no references matched.</returns>
    /// <exception cref="ArgumentNullException">Thrown when resource or target is null.</exception>
    /// <exception cref="ArgumentException">Thrown when target is whitespace only.</exception>
    public static bool RemoveReferences(JsonNode resource, string target)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);

        return RemoveReferencesInternal(resource, target);
    }

    private static bool RemoveReferencesInternal(JsonNode? node, string target)
    {
        var changed = false;

        if (node is JsonObject obj)
        {
            // First, collect all changes to make to this object, and recurse into children.
            // We cannot modify obj while iterating its properties.
            var hasMatchingReference = false;

            // Check if this object has a "reference" property that matches the target.
            if (obj["reference"] is JsonValue value && value.TryGetValue<string>(out var reference))
            {
                if (string.Equals(reference, target, StringComparison.OrdinalIgnoreCase))
                {
                    hasMatchingReference = true;
                }
            }

            // Recurse into all child properties.
            foreach (var property in obj)
            {
                changed |= RemoveReferencesInternal(property.Value, target);
            }

            // Now apply the change to this object if needed.
            if (hasMatchingReference)
            {
                obj.Remove("reference");
                obj["display"] = RemovedReferenceDisplay;
                changed = true;
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                changed |= RemoveReferencesInternal(item, target);
            }
        }

        return changed;
    }
}
