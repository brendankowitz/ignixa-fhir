// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Text.Json.Serialization;

namespace Ignixa.Anonymizer.Configuration;

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
