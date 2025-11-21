// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;

namespace Ignixa.Extensions.ProfileBehaviors.Abstractions;

/// <summary>
/// Context information for the visitor.
/// </summary>
public sealed class VisitorContext
{
    /// <summary>
    /// The FHIR resource type being visited (e.g., "Patient").
    /// </summary>
    public required string ResourceType { get; init; }

    /// <summary>
    /// FHIR version (R4, R4B, R5).
    /// </summary>
    public required FhirSpecification FhirVersion { get; init; }

    /// <summary>
    /// Maximum depth to visit (0 = root only, -1 = unlimited).
    /// </summary>
    public int MaxDepth { get; init; } = -1;

    /// <summary>
    /// Custom options for visitor implementations.
    /// </summary>
    public Dictionary<string, object> Options { get; init; } = new();

    /// <summary>
    /// Gets a typed option value.
    /// </summary>
    public T? GetOption<T>(string key) where T : class
    {
        return Options.TryGetValue(key, out var value) ? value as T : null;
    }
}
