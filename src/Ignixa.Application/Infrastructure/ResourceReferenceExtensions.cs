// <copyright file="ResourceReferenceExtensions.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Ignixa.Serialization.Helpers;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Application.Infrastructure;

/// <summary>
/// Extension methods for working with ResourceReference values in R4 resources.
/// </summary>
public static class ResourceReferenceExtensions
{
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
}
