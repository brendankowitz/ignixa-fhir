// <copyright file="ResourceReferenceExtensions.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System;
using Ignixa.Serialization.Helpers;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Providers;

namespace Ignixa.Specification.Extensions;

/// <summary>
/// Extension methods for working with ResourceReference values in R4 resources.
/// </summary>
[CLSCompliant(false)]
public static class ResourceReferenceExtensions
{
    /// <summary>
    /// Gets all ResourceReference values from an R4 ResourceJsonNode.
    /// </summary>
    /// <param name="resource">The resource to search for references.</param>
    /// <returns>A list of all references found in the resource.</returns>
    public static IReadOnlyList<ResourceReference> GetReferences(this ResourceJsonNode resource)
    {
        return ResourceReferenceHelper.GetReferences(
            resource,
            resource.ResourceType,
            R4ReferenceMetadataProvider.Instance);
    }

    /// <summary>
    /// Updates a reference value in an R4 ResourceJsonNode at the specified path.
    /// </summary>
    /// <param name="resource">The resource to update.</param>
    /// <param name="elementPath">The element path (e.g., "subject", "generalPractitioner").</param>
    /// <param name="newReferenceValue">The new reference value (e.g., "Patient/456").</param>
    /// <param name="arrayIndex">Optional array index if updating a reference within a collection (0-based). Null for single references.</param>
    /// <returns>True if the reference was updated; false if the path was not found.</returns>
    public static bool UpdateReference(this ResourceJsonNode resource, string elementPath, string newReferenceValue, int? arrayIndex = null)
    {
        return ResourceReferenceHelper.UpdateReference(resource, elementPath, newReferenceValue, arrayIndex);
    }

    /// <summary>
    /// Updates all references in an R4 ResourceJsonNode that match a specific value.
    /// </summary>
    /// <param name="resource">The resource to update.</param>
    /// <param name="oldReferenceValue">The reference value to find and replace.</param>
    /// <param name="newReferenceValue">The new reference value.</param>
    /// <returns>The number of references that were updated.</returns>
    public static int UpdateAllReferences(this ResourceJsonNode resource, string oldReferenceValue, string newReferenceValue)
    {
        return ResourceReferenceHelper.UpdateAllReferences(
            resource,
            resource.ResourceType,
            oldReferenceValue,
            newReferenceValue,
            R4ReferenceMetadataProvider.Instance);
    }
}
