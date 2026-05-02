// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Ignixa.DeId.Configuration;

/// <summary>
/// A FHIRPath-based rule defining an de-identification operation.
/// </summary>
public sealed record FhirPathRule
{
    /// <summary>
    /// FHIRPath expression to match nodes (e.g., "Patient.name").
    /// </summary>
    [JsonPropertyName("path")]
    public required string Path { get; init; }

    /// <summary>
    /// DeId method to apply (e.g., "REDACT", "DATESHIFT").
    /// </summary>
    [JsonPropertyName("method")]
    public required string Method { get; init; }

    /// <summary>
    /// Optional resource type filter (e.g., "Patient").
    /// </summary>
    [JsonPropertyName("resourceType")]
    public string? ResourceType { get; init; }

    /// <summary>
    /// Optional processor-specific settings.
    /// </summary>
    [JsonPropertyName("settings")]
    public ImmutableDictionary<string, object>? Settings { get; init; }
}
