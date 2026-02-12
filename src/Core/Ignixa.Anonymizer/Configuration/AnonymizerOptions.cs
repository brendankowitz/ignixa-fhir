// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Ignixa.Anonymizer.Configuration;

/// <summary>
/// Immutable configuration options for the FHIR anonymizer.
/// </summary>
public sealed record AnonymizerOptions
{
    /// <summary>
    /// FHIR version (e.g., "R4", "R4B", "R5").
    /// </summary>
    [JsonPropertyName("fhirVersion")]
    public required string FhirVersion { get; init; }

    /// <summary>
    /// FHIRPath-based anonymization rules.
    /// </summary>
    [JsonPropertyName("fhirPathRules")]
    public required ImmutableArray<FhirPathRule> Rules { get; init; }

    /// <summary>
    /// Optional parameters for anonymization processors (keys, scopes, etc.).
    /// </summary>
    [JsonPropertyName("parameters")]
    public ParameterOptions? Parameters { get; init; }

    /// <summary>
    /// Optional processing behavior options.
    /// </summary>
    [JsonPropertyName("processing")]
    public ProcessingOptions? Processing { get; init; }
}
