// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Ignixa.DeId.Configuration;

/// <summary>
/// Immutable configuration options for the FHIR de-identifier.
/// </summary>
public sealed record DeIdOptions
{
    /// <summary>
    /// FHIR version (e.g., "R4", "R4B", "R5").
    /// </summary>
    [JsonPropertyName("fhirVersion")]
    public required string FhirVersion { get; init; }

    /// <summary>
    /// FHIRPath-based de-identification rules.
    /// </summary>
    [JsonPropertyName("fhirPathRules")]
    public required ImmutableArray<FhirPathRule> Rules { get; init; }

    /// <summary>
    /// Optional parameters for de-identification processors (keys, scopes, etc.).
    /// </summary>
    [JsonPropertyName("parameters")]
    public ParameterOptions? Parameters { get; init; }

    /// <summary>
    /// Optional processing behavior options.
    /// </summary>
    [JsonPropertyName("processing")]
    public ProcessingOptions? Processing { get; init; }
}
