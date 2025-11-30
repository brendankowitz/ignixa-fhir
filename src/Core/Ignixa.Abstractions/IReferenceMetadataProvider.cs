// <copyright file="IReferenceMetadataProvider.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

#nullable enable

namespace Ignixa.Abstractions;

/// <summary>
/// Provides metadata about reference fields in FHIR resources.
/// </summary>
public interface IReferenceMetadataProvider
{
    /// <summary>
    /// Gets reference field metadata for a specific resource type.
    /// </summary>
    /// <param name="resourceType">The FHIR resource type (e.g., "Patient", "Observation").</param>
    /// <returns>A list of reference field metadata, or an empty list if the resource type has no references.</returns>
    IReadOnlyList<ReferenceFieldMetadata> GetMetadata(string resourceType);

    /// <summary>
    /// Determines whether a resource type has any reference fields.
    /// </summary>
    /// <param name="resourceType">The FHIR resource type.</param>
    /// <returns>True if the resource type has reference fields; otherwise, false.</returns>
    bool HasReferences(string resourceType);
}
