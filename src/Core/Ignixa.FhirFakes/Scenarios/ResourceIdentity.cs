// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes.Scenarios;

/// <summary>
/// Tracks identity information for a generated resource in a scenario.
/// </summary>
/// <param name="ResourceType">The FHIR resource type (e.g., "Patient", "Observation").</param>
/// <param name="Id">The unique identifier for the resource (GUID format).</param>
/// <param name="LogicalName">Optional logical name for the resource (e.g., "current-patient", "primary-encounter").</param>
internal sealed record ResourceIdentity(
    string ResourceType,
    string Id,
    string? LogicalName = null)
{
    /// <summary>
    /// Gets the resolved reference format (ResourceType/Id).
    /// Example: "Patient/a1b2c3d4-e5f6-7890-abcd-1234567890ab"
    /// </summary>
    public string ResolvedReference => $"{ResourceType}/{Id}";

    /// <summary>
    /// Gets the urn:uuid reference format.
    /// Example: "urn:uuid:a1b2c3d4-e5f6-7890-abcd-1234567890ab"
    /// </summary>
    public string UrnUuidReference => $"urn:uuid:{Id}";

    /// <summary>
    /// Gets the reference in the specified format.
    /// </summary>
    /// <param name="format">The desired reference format.</param>
    /// <returns>The reference string in the specified format.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when an invalid format is specified.</exception>
    public string GetReference(ReferenceFormat format) => format switch
    {
        ReferenceFormat.UrnUuid => UrnUuidReference,
        ReferenceFormat.Resolved => ResolvedReference,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Invalid reference format")
    };
}
