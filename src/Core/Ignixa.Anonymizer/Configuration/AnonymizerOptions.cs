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

/// <summary>
/// A FHIRPath-based rule defining an anonymization operation.
/// </summary>
public sealed record FhirPathRule
{
    /// <summary>
    /// FHIRPath expression to match nodes (e.g., "Patient.name").
    /// </summary>
    [JsonPropertyName("path")]
    public required string Path { get; init; }

    /// <summary>
    /// Anonymization method to apply (e.g., "REDACT", "DATESHIFT").
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

/// <summary>
/// Parameters for anonymization processors (keys, scopes, etc.).
/// </summary>
public sealed record ParameterOptions
{
    [JsonPropertyName("dateShiftKey")]
    public string? DateShiftKey { get; init; }

    [JsonPropertyName("dateShiftScope")]
    public DateShiftScope? DateShiftScope { get; init; }

    [JsonPropertyName("dateShiftFixedOffsetInDays")]
    public int? DateShiftFixedOffsetInDays { get; init; }

    [JsonPropertyName("cryptoHashKey")]
    public string? CryptoHashKey { get; init; }

    [JsonPropertyName("encryptKey")]
    public string? EncryptKey { get; init; }

    [JsonPropertyName("enablePartialAgesForRedact")]
    public bool EnablePartialAgesForRedact { get; init; }

    [JsonPropertyName("enablePartialDatesForRedact")]
    public bool EnablePartialDatesForRedact { get; init; }

    [JsonPropertyName("enablePartialZipCodesForRedact")]
    public bool EnablePartialZipCodesForRedact { get; init; }

    [JsonPropertyName("restrictedZipCodeTabulationAreas")]
    public ImmutableArray<string>? RestrictedZipCodeTabulationAreas { get; init; }
}

/// <summary>
/// Processing behavior options.
/// </summary>
public sealed record ProcessingOptions
{
    /// <summary>
    /// Whether to validate input FHIR resources before anonymization.
    /// </summary>
    public bool ValidateInput { get; init; }

    /// <summary>
    /// Whether to validate output FHIR resources after anonymization.
    /// </summary>
    public bool ValidateOutput { get; init; }

    /// <summary>
    /// Whether to format output JSON with indentation.
    /// </summary>
    public bool IsPrettyOutput { get; init; }

    /// <summary>
    /// How to handle processing errors.
    /// </summary>
    [JsonPropertyName("processingErrors")]
    public ErrorHandlingMode ErrorHandling { get; init; } = ErrorHandlingMode.StopOnError;
}

/// <summary>
/// Error handling strategies for anonymization processing.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ErrorHandlingMode
{
    /// <summary>
    /// Stop processing and return error on first failure.
    /// </summary>
    StopOnError,

    /// <summary>
    /// Log errors and continue processing (skip failed nodes).
    /// </summary>
    LogAndContinue,

    /// <summary>
    /// Fail immediately on any error (no partial results).
    /// </summary>
    FailFast
}
