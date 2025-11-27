// <copyright file="ResourceReference.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

#nullable enable

namespace Ignixa.Serialization.Models;

/// <summary>
/// Represents a FHIR ResourceReference found within a resource.
/// </summary>
public sealed class ResourceReference
{
    /// <summary>
    /// Gets the element path where this reference was found (e.g., "subject", "generalPractitioner").
    /// </summary>
    public required string ElementPath { get; init; }

    /// <summary>
    /// Gets the full reference value (e.g., "Patient/123", "Organization/abc", "urn:uuid:...").
    /// </summary>
    public required string Value { get; init; }

    /// <summary>
    /// Gets the allowed target resource types for this reference field according to FHIR specification.
    /// Empty list means any resource type is allowed.
    /// </summary>
    public required IReadOnlyList<string> TargetResourceTypes { get; init; }

    /// <summary>
    /// Gets a value indicating whether this reference field can contain multiple references (cardinality max != "1").
    /// </summary>
    public bool IsCollection { get; init; }

    /// <summary>
    /// Gets the type of reference (Relative, Absolute, or Logical).
    /// </summary>
    public ReferenceType Type { get; init; }

    /// <summary>
    /// Gets the resource type extracted from the reference value (e.g., "Patient" from "Patient/123").
    /// Null if the reference is a logical reference (urn:uuid:) or absolute URL.
    /// </summary>
    public string? ResourceType { get; init; }

    /// <summary>
    /// Gets the resource ID extracted from the reference value (e.g., "123" from "Patient/123").
    /// Null if the reference is a logical reference or cannot be parsed.
    /// </summary>
    public string? ResourceId { get; init; }
}
